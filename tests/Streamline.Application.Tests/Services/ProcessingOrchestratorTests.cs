using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Streamline.Domain.Transforms;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Services;

public class ProcessingOrchestratorTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Construct_NullRegistry_Throws()
    {
        var act = () => new ProcessingOrchestrator(
            null!,
            Substitute.For<IStagingRepository>(),
            Substitute.For<IDestinationAdapter>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullStaging_Throws()
    {
        var act = () => new ProcessingOrchestrator(
            Substitute.For<IRegistryRepository>(),
            null!,
            Substitute.For<IDestinationAdapter>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullDestination_Throws()
    {
        var act = () => new ProcessingOrchestrator(
            Substitute.For<IRegistryRepository>(),
            Substitute.For<IStagingRepository>(),
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_NullBatch_Throws()
    {
        var fixture = new Fixture();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(null!, fixture.NewScope());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_NullScope_Throws()
    {
        var fixture = new Fixture();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(IngestedBatch(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- batch lifecycle ----------------------------------------------

    [Fact]
    public async Task Execute_NoEntries_TransitionsBatchToCompleted()
    {
        var fixture = new Fixture();
        fixture.Registry.GetLoadOrderAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        var batch = IngestedBatch();
        var result = await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope());

        batch.Status.Should().Be(BatchStatus.Completed);
        result.Tables.Should().BeEmpty();
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
        await fixture.Txn.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_RegistryThrows_FailsBatchAndRethrows()
    {
        var fixture = new Fixture();
        fixture.Registry.GetLoadOrderAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new InvalidOperationException("boom"));

        var batch = IngestedBatch();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope());

        await act.Should().ThrowAsync<InvalidOperationException>();
        batch.Status.Should().Be(BatchStatus.Failed);
    }

    [Fact]
    public async Task Execute_CancellationDoesNotFailBatch()
    {
        var fixture = new Fixture();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        fixture.Registry.GetLoadOrderAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new OperationCanceledException(cts.Token));

        var batch = IngestedBatch();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Cancellation shouldn't transition batch to Failed — handler decides.
        batch.Status.Should().Be(BatchStatus.Processing);
    }

    // ---- replication happy path ---------------------------------------

    [Fact]
    public async Task Execute_ReplicationEntry_UpsertsAndMarksRowsCommitted()
    {
        var fixture = new Fixture();
        var entry = ReplicationEntry("broker", BuildSchema());
        fixture.WireRegistry([entry]);

        var rows = new[]
        {
            BuildRow(1, "broker", BuildRecord(0, ("broker_id", 1), ("name", "Acme"))),
            BuildRow(2, "broker", BuildRecord(1, ("broker_id", 2), ("name", "Globex"))),
        };
        fixture.WirePendingRows("broker", rows);
        fixture.Txn.UpsertAsync(
            "core", "broker", entry.Schema, Arg.Any<IReadOnlyList<Record>>(), Arg.Any<CancellationToken>())
            .Returns(new UpsertOutcome(2, 0, 0, TimeSpan.FromMilliseconds(10)));

        var batch = IngestedBatch();
        var result = await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope());

        batch.Status.Should().Be(BatchStatus.Completed);
        result.Tables.Should().HaveCount(1);
        result.Tables[0].RowsCommitted.Should().Be(2);
        result.Tables[0].RowsRolledBack.Should().Be(0);
        result.Tables[0].RowsQuarantined.Should().Be(0);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.UPSERT_COMPLETED);

        await fixture.Staging.Received(1).TransitionAsync(
            1L, RowStatus.Processing, RowStatus.Committed, Arg.Any<CancellationToken>());
        await fixture.Staging.Received(1).TransitionAsync(
            2L, RowStatus.Processing, RowStatus.Committed, Arg.Any<CancellationToken>());
    }

    // ---- per-table failure isolates -----------------------------------

    [Fact]
    public async Task Execute_TableUpsertThrows_RollsBackSavepointAndContinues()
    {
        var fixture = new Fixture();
        var brokerEntry = ReplicationEntry("broker", BuildSchema());
        var trustEntry = ReplicationEntry("trust", BuildSchema());
        fixture.WireRegistry([brokerEntry, trustEntry]);

        fixture.WirePendingRows("broker", [
            BuildRow(1, "broker", BuildRecord(0, ("broker_id", 1), ("name", "Acme"))),
        ]);
        fixture.WirePendingRows("trust", [
            BuildRow(2, "trust", BuildRecord(0, ("broker_id", 2), ("name", "Acme"))),
        ]);

        // First table throws.
        fixture.Txn.UpsertAsync(
            "core", "broker", Arg.Any<SchemaDefinition>(), Arg.Any<IReadOnlyList<Record>>(), Arg.Any<CancellationToken>())
            .Returns<Task<UpsertOutcome>>(_ => throw new InvalidOperationException("dup key"));
        // Second table succeeds.
        fixture.Txn.UpsertAsync(
            "core", "trust", Arg.Any<SchemaDefinition>(), Arg.Any<IReadOnlyList<Record>>(), Arg.Any<CancellationToken>())
            .Returns(new UpsertOutcome(1, 0, 0, TimeSpan.FromMilliseconds(5)));

        var batch = IngestedBatch();
        var result = await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope());

        batch.Status.Should().Be(BatchStatus.Completed);
        result.Tables.Should().HaveCount(2);
        result.Tables.First(t => t.TableName == "broker").RowsRolledBack.Should().Be(1);
        result.Tables.First(t => t.TableName == "broker").RowsCommitted.Should().Be(0);
        result.Tables.First(t => t.TableName == "trust").RowsCommitted.Should().Be(1);
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.UPSERT_FAILED && o.Severity == ObservationSeverity.Error);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.SAVEPOINT_ROLLED_BACK);

        await fixture.Staging.Received(1).TransitionAsync(
            1L, RowStatus.Processing, RowStatus.RolledBack, Arg.Any<CancellationToken>());
    }

    // ---- transform-mode skipped ---------------------------------------

    [Fact]
    public async Task Execute_TransformEntry_SkippedWithWarningObservation()
    {
        var fixture = new Fixture();
        var transform = new TransformReference(
            TransformKind.SqlFunction, "domain.calc", "trust_metric", TransformInvocation.PerBatch);
        var entry = new RegistryEntry("trust_metric", BuildSchema())
        {
            Transform = transform,
        };
        fixture.WireRegistry([entry]);

        var batch = IngestedBatch();
        var result = await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope());

        batch.Status.Should().Be(BatchStatus.Completed);
        result.Tables.Should().BeEmpty();
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.TRANSFORMER_NOT_FOUND
            && o.Severity == ObservationSeverity.Warning
            && o.TableName == "trust_metric");
    }

    // ---- FK validation ------------------------------------------------

    [Fact]
    public async Task Execute_RowFailsFk_QuarantinesRowAndContinues()
    {
        var fixture = new Fixture();
        var brokerEntry = ReplicationEntry("broker", BuildSchema());
        var schemaWithFk = new SchemaDefinition(
        [
            new ColumnDefinition("trust_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
            new ColumnDefinition("broker_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                FkReference = new FkReference("broker", "broker_id", FkEnforcementMode.Always),
            },
        ]);
        var trustEntry = ReplicationEntry("trust", schemaWithFk);
        fixture.WireRegistry([brokerEntry, trustEntry]);

        // Parent FK cache: only broker_id "1" exists.
        fixture.Txn.GetDistinctColumnValuesAsync(
            "core", "broker", "broker_id", Arg.Any<CancellationToken>())
            .Returns(new[] { "1" });

        fixture.WirePendingRows("broker", []);
        fixture.WirePendingRows("trust", [
            BuildRow(10, "trust", BuildRecord(0, ("trust_id", 100), ("broker_id", 1))),
            BuildRow(11, "trust", BuildRecord(1, ("trust_id", 101), ("broker_id", 999))),
        ]);

        fixture.Txn.UpsertAsync(
            "core", "trust", Arg.Any<SchemaDefinition>(), Arg.Any<IReadOnlyList<Record>>(), Arg.Any<CancellationToken>())
            .Returns(new UpsertOutcome(1, 0, 0, TimeSpan.FromMilliseconds(5)));

        var batch = IngestedBatch();
        var result = await fixture.Orchestrator.ExecuteAsync(batch, fixture.NewScope());

        batch.Status.Should().Be(BatchStatus.Completed);
        var trustOutcome = result.Tables.First(t => t.TableName == "trust");
        trustOutcome.RowsCommitted.Should().Be(1);
        trustOutcome.RowsQuarantined.Should().Be(1);

        await fixture.Staging.Received(1).QuarantineAsync(
            11L, ObservationCodes.FK_VIOLATION, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---- degradation flag ---------------------------------------------

    [Fact]
    public async Task Execute_DegradationFlagPropagates()
    {
        var fixture = new Fixture();
        fixture.Registry.GetLoadOrderAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var scope = fixture.NewScope();
        for (var i = 0; i < ObservationDegradationState.FailureThreshold; i++)
        {
            scope.DegradationState.RecordFailure();
        }

        var batch = IngestedBatch();
        var result = await fixture.Orchestrator.ExecuteAsync(batch, scope);

        result.ObservabilityDegraded.Should().BeTrue();
    }

    // ---- helpers ------------------------------------------------------

    private static Batch IngestedBatch()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();
        batch.MarkIngested();
        // Drain to clear pre-existing events; the orchestrator
        // operates from Ingested.
        _ = batch.DrainEvents();
        return batch;
    }

    private sealed class Fixture
    {
        public IRegistryRepository Registry { get; } = Substitute.For<IRegistryRepository>();
        public IStagingRepository Staging { get; } = Substitute.For<IStagingRepository>();
        public IDestinationAdapter Destination { get; } = Substitute.For<IDestinationAdapter>();
        public ITransactionScope Txn { get; } = Substitute.For<ITransactionScope>();
        public IObservationSink InnerSink { get; } = Substitute.For<IObservationSink>();
        public ILogger<ResilientObservationSink> Logger { get; } = Substitute.For<ILogger<ResilientObservationSink>>();

        public Fixture()
        {
            Destination.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(Txn);
            var nested = Substitute.For<INestedScope>();
            Txn.BeginNestedScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(nested);
            Txn.GetDistinctColumnValuesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<string>());
            InnerSink.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }

        public ProcessingOrchestrator Orchestrator => new(Registry, Staging, Destination);

        public PerBatchScope NewScope() => PerBatchScope.CreateForBatch(InnerSink, Logger);

        public void WireRegistry(IReadOnlyList<RegistryEntry> entries)
        {
            Registry.GetLoadOrderAsync(Arg.Any<CancellationToken>())
                .Returns(entries.Select(e => e.TableName).ToArray());
            foreach (var entry in entries)
            {
                Registry.GetActiveAsync(entry.TableName, Arg.Any<CancellationToken>())
                    .Returns(entry);
            }
        }

        public void WirePendingRows(string tableName, IReadOnlyList<StagedRow> rows)
        {
            Staging.GetPendingRowsAsync(
                Arg.Any<BatchId>(), tableName, Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => ToAsync(rows));
        }

        private static async IAsyncEnumerable<StagedRow> ToAsync(
            IReadOnlyList<StagedRow> rows,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
                await Task.Yield();
            }
        }
    }

    private static SchemaDefinition BuildSchema() =>
        new(
        [
            new ColumnDefinition("broker_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
            new ColumnDefinition("name", ColumnTypeCode.String)
            {
                IsRequired = true,
                MaxLength = 200,
            },
        ]);

    private static RegistryEntry ReplicationEntry(string tableName, SchemaDefinition schema) =>
        new(tableName, schema) { TargetSchema = "core" };

    private static Record BuildRecord(long rowIndex, params (string Key, object? Value)[] kvps)
    {
        var dict = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in kvps)
        {
            dict.Add(k, v);
        }
        return new Record($"file-{rowIndex}.csv", rowIndex, dict.ToImmutable());
    }

    private static StagedRow BuildRow(long incomingId, string tableName, Record record) =>
        new(incomingId, AnyBatch, tableName, record, RowStatus.Processing, fileLogId: 1);
}
