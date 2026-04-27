using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Application.Tests.Fakes;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Registry;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="IngestBatchHandler"/> wired
/// against real domain types, real validators, and the in-memory
/// fakes from sub-phase 1g. No mocks — every collaborator is the
/// real production implementation or its in-memory test double.
/// </summary>
/// <remarks>
/// Convention: registry entries use <see cref="ActiveWindow"/>'s
/// year-wide window (today − 1 year through today + 1 year) so the
/// "active right now" filter never flakes on edge-of-validity
/// timing.
/// </remarks>
public class IngestBatchHandlerIntegrationTests
{
    [Fact]
    public async Task HappyPath_StagesValidRowsAndEmitsLifecycleObservations()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerMappingAsync();
        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["broker_id", "name"],
            [
                BrokerRecord(0, "1", "Acme"),
                BrokerRecord(1, "2", "Globex"),
            ]));

        var handler = fixture.BuildIngestHandler(_ => ["/data/brokers.csv"]);
        var result = await handler.HandleAsync(new IngestBatchCommand("/data"));

        // Contract assertions, not artifact assertions: the contract
        // says "valid rows are staged in Pending"; verify via
        // GetRowCountsAsync (production query API), not by reaching
        // into the fake's internals.
        result.TotalRowsStaged.Should().Be(2);
        result.TotalRowsQuarantined.Should().Be(0);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_STARTED);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.FILE_INGESTED);

        // The architectural test (Q6): observations also land in
        // the repository so InspectBatchHandler can read them later.
        var stored = fixture.Observations.ForTestingOnly_AllObservations();
        stored.Should().Contain(o => o.Code == ObservationCodes.BATCH_STARTED);
        stored.Should().Contain(o => o.Code == ObservationCodes.FILE_INGESTED);
    }

    [Fact]
    public async Task FileWithNoMapping_SilentlySkipped()
    {
        var fixture = new Fixture();
        // Don't register any mappings. The file is unrecognized.
        var handler = fixture.BuildIngestHandler(_ => ["/data/unknown.csv"]);

        var result = await handler.HandleAsync(new IngestBatchCommand("/data"));

        result.Files.Should().BeEmpty();
        // No critical observation — silent skip is the contract.
        result.Observations.Should().NotContain(o =>
            o.Severity >= ObservationSeverity.Error);
    }

    [Fact]
    public async Task DriftPolicyBlock_RejectsFileWithoutStaging()
    {
        var fixture = new Fixture();
        var entry = BuildBrokerEntry() with
        {
            NewColumnsDriftPolicy = DriftPolicy.Block,
        };
        await fixture.Registry.UpsertAsync(entry);
        await fixture.RegisterBrokerMappingAsync();
        fixture.Readers.Register(FakeFileReader.WithMismatchedRecords(
            ["broker_id", "name", "extra_unexpected_column"],
            [BrokerRecord(0, "1", "Acme")]));

        var handler = fixture.BuildIngestHandler(_ => ["/data/brokers.csv"]);
        var result = await handler.HandleAsync(new IngestBatchCommand("/data"));

        // Contract: drift-blocked files don't stage rows.
        result.TotalRowsRead.Should().Be(0);
        result.TotalRowsStaged.Should().Be(0);
        result.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.SCHEMA_DRIFT_BLOCKED
            && o.Severity == ObservationSeverity.Critical);

        // Nothing in staging.incoming for this batch.
        fixture.Staging.ForTestingOnly_AllRows().Should().BeEmpty();
    }

    [Fact]
    public async Task RowValidationFailures_QuarantineCountAndObservations()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerMappingAsync();
        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["broker_id", "name"],
            [
                BrokerRecord(0, "1", "Acme"),       // valid
                BrokerRecord(1, "abc", "BadType"),  // INVALID_TYPE (broker_id is Integer)
                BrokerRecord(2, null, "Missing"),   // MISSING_REQUIRED
            ]));

        var handler = fixture.BuildIngestHandler(_ => ["/data/brokers.csv"]);
        var result = await handler.HandleAsync(new IngestBatchCommand("/data"));

        result.TotalRowsRead.Should().Be(3);
        result.TotalRowsStaged.Should().Be(1);
        result.TotalRowsQuarantined.Should().Be(2);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.INVALID_TYPE);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.MISSING_REQUIRED);
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsBeforeStateMutation()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerMappingAsync();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = fixture.BuildIngestHandler(_ => ["/data/brokers.csv"]);

        var act = async () => await handler.HandleAsync(new IngestBatchCommand("/data"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // State contract: pre-cancellation aborts before any
        // mutation. No batch created, no rows staged, no
        // observations emitted.
        fixture.Staging.ForTestingOnly_AllRows().Should().BeEmpty();
        fixture.Observations.ForTestingOnly_AllObservations().Should().BeEmpty();
    }

    // ---- fixtures ----------------------------------------------------

    private sealed class Fixture
    {
        public InMemoryStagingRepository Staging { get; } = new();
        public InMemoryDestinationAdapter Destination { get; } = new();
        public InMemoryRegistryRepository Registry { get; } = new();
        public InMemoryFileMappingRepository FileMappings { get; } = new();
        public InMemoryObservationRepository Observations { get; } = new();
        public InMemoryFileReaderRegistry Readers { get; } = new();

        public IngestBatchHandler BuildIngestHandler(
            Func<string, IReadOnlyList<string>> enumerateFiles)
        {
            var sink = new ForwardingObservationSink(Observations);
            var orchestrator = new IngestionOrchestrator(
                FileMappings, Registry, Readers, Staging);
            return new IngestBatchHandler(
                Staging, orchestrator, sink,
                NullLogger<ResilientObservationSink>.Instance,
                enumerateFiles);
        }

        public async Task RegisterBrokerEntryAsync() =>
            await Registry.UpsertAsync(BuildBrokerEntry());

        public async Task RegisterBrokerMappingAsync() =>
            await FileMappings.UpsertAsync(
                new FileMapping(@"^brokers\.csv$", "broker", "delimited"));
    }

    private static RegistryEntry BuildBrokerEntry() =>
        new("broker", new SchemaDefinition(
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
        ]))
        {
            TargetSchema = "core",
            ValidFrom = ActiveWindow().From,
            ValidTo = ActiveWindow().To,
        };

    private static (DateOnly From, DateOnly To) ActiveWindow()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        return (today.AddYears(-1), today.AddYears(1));
    }

    private static Record BrokerRecord(long index, string? brokerId, string name) =>
        new("brokers.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", brokerId)
                .Add("name", name));
}
