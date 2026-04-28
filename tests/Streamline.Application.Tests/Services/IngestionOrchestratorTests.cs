using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Services;

public class IngestionOrchestratorTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Construct_NullMappings_Throws()
    {
        var act = () => new IngestionOrchestrator(
            null!,
            Substitute.For<IRegistryRepository>(),
            Substitute.For<IFileReaderRegistry>(),
            Substitute.For<IStagingRepository>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullRegistry_Throws()
    {
        var act = () => new IngestionOrchestrator(
            Substitute.For<IFileMappingRepository>(),
            null!,
            Substitute.For<IFileReaderRegistry>(),
            Substitute.For<IStagingRepository>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullReaders_Throws()
    {
        var act = () => new IngestionOrchestrator(
            Substitute.For<IFileMappingRepository>(),
            Substitute.For<IRegistryRepository>(),
            null!,
            Substitute.For<IStagingRepository>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullStaging_Throws()
    {
        var act = () => new IngestionOrchestrator(
            Substitute.For<IFileMappingRepository>(),
            Substitute.For<IRegistryRepository>(),
            Substitute.For<IFileReaderRegistry>(),
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_NullBatch_Throws()
    {
        var fixture = new Fixture();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(
            null!, [], fixture.NewScope());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_NullFilePaths_Throws()
    {
        var fixture = new Fixture();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(
            Batch.Create(AnyBatch, "src"), null!, fixture.NewScope());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_NullScope_Throws()
    {
        var fixture = new Fixture();
        var act = async () => await fixture.Orchestrator.ExecuteAsync(
            Batch.Create(AnyBatch, "src"), [], null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- batch lifecycle ----------------------------------------------

    [Fact]
    public async Task Execute_NoFiles_TransitionsBatchThroughStartAndIngested()
    {
        var fixture = new Fixture();
        var batch = Batch.Create(AnyBatch, "src");

        var result = await fixture.Orchestrator.ExecuteAsync(batch, [], fixture.NewScope());

        batch.Status.Should().Be(BatchStatus.Ingested);
        result.Files.Should().BeEmpty();
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_STARTED);
    }

    [Fact]
    public async Task Execute_NoFiles_DrainsAggregateEvents()
    {
        var fixture = new Fixture();
        var batch = Batch.Create(AnyBatch, "src");

        await fixture.Orchestrator.ExecuteAsync(batch, [], fixture.NewScope());

        // After execution, the orchestrator should have drained both
        // post-Start and post-MarkIngested events.
        batch.DrainEvents().Should().BeEmpty();
    }

    // ---- mapping resolution -------------------------------------------

    [Fact]
    public async Task Execute_FileWithNoMapping_SkipsSilentlyWithNoOutcome()
    {
        var fixture = new Fixture();
        var batch = Batch.Create(AnyBatch, "src");

        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/unknown.csv"], fixture.NewScope());

        result.Files.Should().BeEmpty();
        result.Observations.Should().NotContain(o =>
            o.Code == ObservationCodes.FILE_READ_FAILED && o.Message.Contains("unknown.csv"));
    }

    [Fact]
    public async Task Execute_FileWithMappingButNoRegistryEntry_EmitsCriticalOutcome()
    {
        var fixture = new Fixture();
        var mapping = new FileMapping("brokers\\.csv", "broker", "delimited");
        fixture.Mappings.ResolveAsync("brokers.csv", Arg.Any<CancellationToken>())
            .Returns(mapping);
        fixture.Registry.GetActiveAsync("broker", Arg.Any<CancellationToken>())
            .Returns((RegistryEntry?)null);

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/brokers.csv"], fixture.NewScope());

        result.Files.Should().HaveCount(1);
        result.Files[0].FileName.Should().Be("brokers.csv");
        result.Files[0].RowsRead.Should().Be(0);
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.FILE_READ_FAILED
            && o.Severity == ObservationSeverity.Critical
            && o.Message.Contains("No active registry entry"));
    }

    [Fact]
    public async Task Execute_FileWithMappingButNoReader_EmitsCriticalOutcome()
    {
        var fixture = new Fixture();
        var mapping = new FileMapping("brokers\\.csv", "broker", "delimited");
        var entry = new RegistryEntry("broker", BuildSchema()) { TargetSchema = "core" };
        fixture.Mappings.ResolveAsync("brokers.csv", Arg.Any<CancellationToken>())
            .Returns(mapping);
        fixture.Registry.GetActiveAsync("broker", Arg.Any<CancellationToken>())
            .Returns(entry);
        fixture.Readers.ResolveReader("delimited").Returns((IFileReader?)null);

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/brokers.csv"], fixture.NewScope());

        result.Files.Should().HaveCount(1);
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.FILE_READ_FAILED
            && o.Severity == ObservationSeverity.Critical
            && o.Message.Contains("No reader registered"));
    }

    // ---- happy path ---------------------------------------------------

    [Fact]
    public async Task Execute_HappyPath_StagesValidRowsAndEmitsFileIngested()
    {
        var fixture = new Fixture();
        var entry = new RegistryEntry("broker", BuildSchema()) { TargetSchema = "core" };
        fixture.WireMapping("brokers.csv", "broker", "delimited", entry);
        var reader = new FakeReader(
            ["broker_id", "name"],
            [
                MakeRecord("brokers.csv", 0, [("broker_id", "1"), ("name", "Acme")]),
                MakeRecord("brokers.csv", 1, [("broker_id", "2"), ("name", "Globex")]),
            ]);
        fixture.Readers.ResolveReader("delimited").Returns(reader);
        fixture.Staging.OpenFileLogAsync(AnyBatch, "brokers.csv", Arg.Any<CancellationToken>())
            .Returns(101L);
        fixture.Staging.BulkInsertIncomingAsync(
            AnyBatch, 101L, "broker", Arg.Any<IAsyncEnumerable<Record>>(), Arg.Any<CancellationToken>())
            .Returns(call => CountAsync(call.Arg<IAsyncEnumerable<Record>>()));

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/brokers.csv"], fixture.NewScope());

        result.Files.Should().HaveCount(1);
        result.Files[0].RowsRead.Should().Be(2);
        result.Files[0].RowsStaged.Should().Be(2);
        result.Files[0].RowsQuarantined.Should().Be(0);
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.FILE_INGESTED && o.FileLogId == 101L);
    }

    [Fact]
    public async Task Execute_RowFailsValidation_EmitsErrorObservationsAndExcludesFromBulkInsert()
    {
        var fixture = new Fixture();
        var entry = new RegistryEntry("broker", BuildSchema()) { TargetSchema = "core" };
        fixture.WireMapping("brokers.csv", "broker", "delimited", entry);
        var reader = new FakeReader(
            ["broker_id", "name"],
            [
                MakeRecord("brokers.csv", 0, [("broker_id", "1"), ("name", "Acme")]),
                MakeRecord("brokers.csv", 1, [("broker_id", "abc"), ("name", "Bad")]),
                MakeRecord("brokers.csv", 2, [("broker_id", null), ("name", "Missing")]),
            ]);
        fixture.Readers.ResolveReader("delimited").Returns(reader);
        fixture.Staging.OpenFileLogAsync(AnyBatch, "brokers.csv", Arg.Any<CancellationToken>())
            .Returns(101L);
        fixture.Staging.BulkInsertIncomingAsync(
            AnyBatch, 101L, "broker", Arg.Any<IAsyncEnumerable<Record>>(), Arg.Any<CancellationToken>())
            .Returns(call => CountAsync(call.Arg<IAsyncEnumerable<Record>>()));

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/brokers.csv"], fixture.NewScope());

        result.Files[0].RowsRead.Should().Be(3);
        result.Files[0].RowsStaged.Should().Be(1);
        result.Files[0].RowsQuarantined.Should().Be(2);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.INVALID_TYPE);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.MISSING_REQUIRED);
    }

    // ---- drift handling -----------------------------------------------

    [Fact]
    public async Task Execute_DriftBlocksFile_DoesNotOpenFileLogAndOutcomeIsZero()
    {
        var fixture = new Fixture();
        var entry = new RegistryEntry("broker", BuildSchema())
        {
            TargetSchema = "core",
            NewColumnsDriftPolicy = DriftPolicy.Block,
        };
        fixture.WireMapping("brokers.csv", "broker", "delimited", entry);
        var reader = new FakeReader(
            ["broker_id", "name", "extra"], // "extra" is a new column
            []);
        fixture.Readers.ResolveReader("delimited").Returns(reader);

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/brokers.csv"], fixture.NewScope());

        result.Files[0].RowsRead.Should().Be(0);
        result.Files[0].RowsStaged.Should().Be(0);
        await fixture.Staging.DidNotReceive().OpenFileLogAsync(
            Arg.Any<BatchId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.SCHEMA_DRIFT_BLOCKED
            && o.Severity == ObservationSeverity.Critical);
    }

    [Fact]
    public async Task Execute_DriftWarnsButDoesNotBlock_FileStillStaged()
    {
        var fixture = new Fixture();
        var entry = new RegistryEntry("broker", BuildSchema())
        {
            TargetSchema = "core",
            NewColumnsDriftPolicy = DriftPolicy.Warn,
        };
        fixture.WireMapping("brokers.csv", "broker", "delimited", entry);
        var reader = new FakeReader(
            ["broker_id", "name", "extra"],
            [MakeRecord("brokers.csv", 0, [("broker_id", "1"), ("name", "Acme"), ("extra", "x")])]);
        fixture.Readers.ResolveReader("delimited").Returns(reader);
        fixture.Staging.OpenFileLogAsync(AnyBatch, "brokers.csv", Arg.Any<CancellationToken>())
            .Returns(101L);
        fixture.Staging.BulkInsertIncomingAsync(
            AnyBatch, 101L, "broker", Arg.Any<IAsyncEnumerable<Record>>(), Arg.Any<CancellationToken>())
            .Returns(call => CountAsync(call.Arg<IAsyncEnumerable<Record>>()));

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/brokers.csv"], fixture.NewScope());

        result.Files[0].RowsStaged.Should().Be(1);
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN
            && o.Severity == ObservationSeverity.Warning);
    }

    // ---- cancellation -------------------------------------------------

    [Fact]
    public async Task Execute_CancellationRequested_ThrowsOperationCanceled()
    {
        var fixture = new Fixture();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var batch = Batch.Create(AnyBatch, "src");

        var act = async () => await fixture.Orchestrator.ExecuteAsync(
            batch, ["/data/x.csv"], fixture.NewScope(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- degradation reporting ----------------------------------------

    [Fact]
    public async Task Execute_PropagatesDegradationFlagToResult()
    {
        var fixture = new Fixture();
        var scope = fixture.NewScope();
        // Simulate degradation by tripping the state directly.
        for (var i = 0; i < ObservationDegradationState.FailureThreshold; i++)
        {
            scope.DegradationState.RecordFailure();
        }

        var batch = Batch.Create(AnyBatch, "src");
        var result = await fixture.Orchestrator.ExecuteAsync(batch, [], scope);

        result.ObservabilityDegraded.Should().BeTrue();
    }

    // ---- helpers ------------------------------------------------------

    private sealed class Fixture
    {
        public IFileMappingRepository Mappings { get; } = Substitute.For<IFileMappingRepository>();
        public IRegistryRepository Registry { get; } = Substitute.For<IRegistryRepository>();
        public IFileReaderRegistry Readers { get; } = Substitute.For<IFileReaderRegistry>();
        public IStagingRepository Staging { get; } = Substitute.For<IStagingRepository>();
        public IObservationSink InnerSink { get; } = Substitute.For<IObservationSink>();
        public ILogger<ResilientObservationSink> Logger { get; } = Substitute.For<ILogger<ResilientObservationSink>>();

        public Fixture()
        {
            InnerSink.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }

        public IngestionOrchestrator Orchestrator => new(Mappings, Registry, Readers, Staging);

        public PerBatchScope NewScope() => PerBatchScope.CreateForBatch(InnerSink, Logger);

        public void WireMapping(string fileName, string targetTable, string format, RegistryEntry entry)
        {
            var mapping = new FileMapping(fileName.Replace(".", "\\."), targetTable, format);
            Mappings.ResolveAsync(fileName, Arg.Any<CancellationToken>()).Returns(mapping);
            Registry.GetActiveAsync(targetTable, Arg.Any<CancellationToken>()).Returns(entry);
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

    private static Record MakeRecord(
        string sourceFile, long rowIndex, params (string Key, object? Value)[] kvps)
    {
        var dict = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in kvps)
        {
            dict.Add(k, v);
        }
        return new Record(sourceFile, rowIndex, dict.ToImmutable());
    }

    private static async Task<long> CountAsync(IAsyncEnumerable<Record> records)
    {
        var count = 0L;
        await foreach (var _ in records.ConfigureAwait(false))
        {
            count++;
        }
        return count;
    }

    private sealed class FakeReader : IFileReader
    {
        private readonly IReadOnlyList<string> _headers;
        private readonly IReadOnlyList<Record> _rows;

        public FakeReader(IReadOnlyList<string> headers, IReadOnlyList<Record> rows)
        {
            _headers = headers;
            _rows = rows;
        }

        public string Format => "delimited";

        public bool CanRead(FileMapping mapping, string filePath) => true;

        public Task<IReadOnlyList<string>> GetHeadersAsync(
            FileMapping mapping, string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_headers);

        public IAsyncEnumerable<Record> ReadRowsAsync(
            FileMapping mapping, string filePath, CancellationToken cancellationToken = default) =>
            ToAsync(_rows, cancellationToken);

        public Task<IReadOnlyList<string>> GetHeadersAsync(
            FileMapping mapping, Stream content, string logicalName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_headers);

        public IAsyncEnumerable<Record> ReadRowsAsync(
            FileMapping mapping, Stream content, string logicalName, CancellationToken cancellationToken = default) =>
            ToAsync(_rows, cancellationToken);

        private static async IAsyncEnumerable<Record> ToAsync(
            IReadOnlyList<Record> rows, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
                await Task.Yield();
            }
        }
    }
}
