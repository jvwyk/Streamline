using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Application.Tests.Fakes;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Streamline.Domain.Transforms;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="ProcessBatchHandler"/> wired
/// against real orchestrator, real domain types, real
/// <see cref="FkResolver"/>, and the in-memory fakes from 1g. Each
/// test seeds staging directly through the staging repository's
/// public API (no IngestBatchHandler in the path) so the
/// processing surface is exercised in isolation; the
/// IngestThenProcess flow tests in commit 6 cover the joined path.
/// </summary>
public class ProcessBatchHandlerIntegrationTests
{
    [Fact]
    public async Task HappyPath_CommitsRowsToDestinationAndCompletesBatch()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        var batch = await fixture.SeedPendingBrokerRowsAsync(
        [
            BrokerTypedRecord(0, 1, "Acme"),
            BrokerTypedRecord(1, 2, "Globex"),
        ]);

        var handler = fixture.BuildProcessHandler();
        var result = await handler.HandleAsync(new ProcessBatchCommand(batch));

        // Contract: rows transition Pending -> Processing -> Committed
        // and land in the destination. Verify both the per-row state
        // and the destination contents.
        result.Tables.Should().ContainSingle();
        result.Tables[0].RowsCommitted.Should().Be(2);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.UPSERT_COMPLETED);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);

        var counts = await fixture.Staging.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Committed].Should().Be(2);
        counts[RowStatus.Pending].Should().Be(0);

        var committed = fixture.Destination.ForTestingOnly_GetCommitted("core", "broker");
        committed.Should().HaveCount(2);
    }

    [Fact]
    public async Task FKViolation_QuarantinesChildRowsButCommitsValidOnes()
    {
        var fixture = new Fixture();
        // Broker entry (parent) and broker_address entry (child with FK)
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerAddressEntryAsync();

        // Seed parent into destination so the FK preload finds value "1".
        // (No pending broker rows; that table contributes a zero-row
        // outcome but its TargetSchema is needed for FK preload.)
        fixture.Destination.ForTestingOnly_SeedCommitted(
            "core", "broker", BuildBrokerSchema(),
            [BrokerTypedRecord(0, 1, "Acme")]);

        var batch = await fixture.SeedPendingAddressRowsAsync(
        [
            AddressTypedRecord(0, 100, brokerId: 1, "addr-1"),  // valid FK
            AddressTypedRecord(1, 101, brokerId: 99, "addr-2"), // FK violation
        ]);

        var handler = fixture.BuildProcessHandler();
        var result = await handler.HandleAsync(new ProcessBatchCommand(batch));

        var addrOutcome = result.Tables.First(t => t.TableName == "broker_address");
        addrOutcome.RowsCommitted.Should().Be(1);
        addrOutcome.RowsQuarantined.Should().Be(1);

        var addrCounts = await fixture.Staging.GetRowCountsAsync(batch, "broker_address");
        addrCounts[RowStatus.Committed].Should().Be(1);
        addrCounts[RowStatus.Quarantined].Should().Be(1);

        var quarantine = fixture.Staging.ForTestingOnly_AllQuarantine();
        quarantine.Values.Should().AllSatisfy(q => q.Code.Should().Be(ObservationCodes.FK_VIOLATION));
    }

    [Fact]
    public async Task TransformModeEntry_SkippedWithDeferredObservation()
    {
        var fixture = new Fixture();
        var transformReference = new TransformReference(
            TransformKind.SqlFunction, "domain.calc", "core.derived",
            TransformInvocation.PerBatch);
        var entry = new RegistryEntry("derived", BuildBrokerSchema())
        {
            TargetSchema = "core",
            Transform = transformReference,
            ValidFrom = ActiveWindow().From,
            ValidTo = ActiveWindow().To,
        };
        await fixture.Registry.UpsertAsync(entry);
        var batch = await fixture.Staging.StartBatchAsync("/data");
        // Drive the persisted batch to Ingested so BeginProcessing
        // is a legal transition. Production code paths reach this
        // state via IngestBatchHandler; ProcessBatchHandler tests
        // can short-circuit since they're not exercising ingestion.
        fixture.Staging.ForTestingOnly_SetBatchStatus(batch, BatchStatus.Ingested);

        var handler = fixture.BuildProcessHandler();
        var result = await handler.HandleAsync(new ProcessBatchCommand(batch));

        // Contract for v1: transform-mode entry skipped with
        // TRANSFORM_MODE_DEFERRED Warning, batch reaches Completed.
        // Distinct from TRANSFORMER_NOT_FOUND, which is the Phase 4
        // Critical for unresolvable transformer references.
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.TRANSFORM_MODE_DEFERRED
            && o.Severity == ObservationSeverity.Warning
            && o.TableName == "derived");
        result.Observations.Should().NotContain(o =>
            o.Code == ObservationCodes.TRANSFORMER_NOT_FOUND);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
    }

    [Fact]
    public async Task EmptyRegistry_CompletesBatchWithNoTables()
    {
        var fixture = new Fixture();
        var batch = await fixture.Staging.StartBatchAsync("/data");
        fixture.Staging.ForTestingOnly_SetBatchStatus(batch, BatchStatus.Ingested);

        var handler = fixture.BuildProcessHandler();
        var result = await handler.HandleAsync(new ProcessBatchCommand(batch));

        result.Tables.Should().BeEmpty();
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsWithoutMarkingBatchFailed()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        var batch = await fixture.SeedPendingBrokerRowsAsync(
            [BrokerTypedRecord(0, 1, "Acme")]);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = fixture.BuildProcessHandler();

        var act = async () => await handler.HandleAsync(new ProcessBatchCommand(batch), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Cancellation contract from 1f-ii: ProcessingOrchestrator
        // does NOT mark the batch Failed on cancellation; the
        // handler decides. After pre-cancellation the batch should
        // still be in its pre-call status (Created — the staging
        // fake doesn't auto-advance batch status without orchestrator
        // engagement).
        var snapshot = await fixture.Staging.GetBatchAsync(batch);
        snapshot!.Status.Should().NotBe(BatchStatus.Failed);
    }

    // ---- fixtures ----------------------------------------------------

    private sealed class Fixture
    {
        public InMemoryStagingRepository Staging { get; } = new();
        public InMemoryDestinationAdapter Destination { get; } = new();
        public InMemoryRegistryRepository Registry { get; } = new();
        public InMemoryObservationRepository Observations { get; } = new();

        public ProcessBatchHandler BuildProcessHandler()
        {
            var sink = new ForwardingObservationSink(Observations);
            var orchestrator = new ProcessingOrchestrator(Registry, Staging, Destination);
            return new ProcessBatchHandler(
                Staging, orchestrator, sink,
                NullLogger<ResilientObservationSink>.Instance);
        }

        public async Task RegisterBrokerEntryAsync() =>
            await Registry.UpsertAsync(BuildBrokerEntry());

        public async Task RegisterBrokerAddressEntryAsync() =>
            await Registry.UpsertAsync(BuildBrokerAddressEntry());

        public async Task<BatchId> SeedPendingBrokerRowsAsync(IEnumerable<Record> records)
        {
            var batch = await Staging.StartBatchAsync("/data");
            var fileLogId = await Staging.OpenFileLogAsync(batch, "brokers.csv");
            await Staging.BulkInsertIncomingAsync(
                batch, fileLogId, "broker", AsyncEnumerable(records));
            // Drive the persisted batch to Ingested so the
            // orchestrator's BeginProcessing transition is legal.
            Staging.ForTestingOnly_SetBatchStatus(batch, BatchStatus.Ingested);
            return batch;
        }

        public async Task<BatchId> SeedPendingAddressRowsAsync(IEnumerable<Record> records)
        {
            var batch = await Staging.StartBatchAsync("/data");
            var fileLogId = await Staging.OpenFileLogAsync(batch, "addresses.csv");
            await Staging.BulkInsertIncomingAsync(
                batch, fileLogId, "broker_address", AsyncEnumerable(records));
            Staging.ForTestingOnly_SetBatchStatus(batch, BatchStatus.Ingested);
            return batch;
        }

        private static async IAsyncEnumerable<Record> AsyncEnumerable(IEnumerable<Record> records)
        {
            foreach (var r in records)
            {
                yield return r;
                await Task.Yield();
            }
        }
    }

    private static RegistryEntry BuildBrokerEntry() =>
        new("broker", BuildBrokerSchema())
        {
            TargetSchema = "core",
            ValidFrom = ActiveWindow().From,
            ValidTo = ActiveWindow().To,
        };

    private static RegistryEntry BuildBrokerAddressEntry() =>
        new("broker_address", new SchemaDefinition(
        [
            new ColumnDefinition("address_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
            new ColumnDefinition("broker_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                FkReference = new FkReference("broker", "broker_id", FkEnforcementMode.Always),
            },
            new ColumnDefinition("address", ColumnTypeCode.String)
            {
                IsRequired = true,
                MaxLength = 200,
            },
        ]))
        {
            TargetSchema = "core",
            ValidFrom = ActiveWindow().From,
            ValidTo = ActiveWindow().To,
            DependsOn = ["broker"],
        };

    private static SchemaDefinition BuildBrokerSchema() =>
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

    private static (DateOnly From, DateOnly To) ActiveWindow()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        return (today.AddYears(-1), today.AddYears(1));
    }

    private static Record BrokerTypedRecord(long index, int brokerId, string name) =>
        new("brokers.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", brokerId)
                .Add("name", name));

    private static Record AddressTypedRecord(
        long index, int addressId, int brokerId, string address) =>
        new("addresses.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("address_id", addressId)
                .Add("broker_id", brokerId)
                .Add("address", address));
}
