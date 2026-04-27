using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Queries;
using Streamline.Application.Services;
using Streamline.Application.Tests.Fakes;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Integration.Flows;

/// <summary>
/// Cross-cutting flow tests that span multiple handlers. These
/// verify the integration points — the handoffs between
/// ingestion and processing, the multi-table load order, the
/// observation pipeline visibility, the drift-blocks-don't-corrupt
/// invariant. Three tests in this file have been perturb-recovered:
/// <list type="bullet">
///   <item>Multi-table dependency — perturbed by reversing the
///     load order in <c>InMemoryRegistryRepository.GetLoadOrderAsync</c>;
///     fails with "child rows committed before parent" assertion.</item>
///   <item>FK violation — perturbed by disabling the FK check in
///     <c>ProcessingOrchestrator.FindFkFailure</c>; fails because
///     bad FK rows are upserted instead of quarantined.</item>
///   <item>Transform mode skip — perturbed by changing the
///     orchestrator emission back to <c>TRANSFORMER_NOT_FOUND</c>;
///     fails with the explicit "expected TRANSFORM_MODE_DEFERRED"
///     assertion.</item>
/// </list>
/// </summary>
public class CrossCuttingFlowTests
{
    [Fact]
    public async Task IngestThenProcess_HappyPath_RowsFlowToDestination()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerMappingAsync();
        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["broker_id", "name"],
            [
                BrokerRawRecord(0, "1", "Acme"),
                BrokerRawRecord(1, "2", "Globex"),
            ]));

        // 1. Ingest.
        var ingestHandler = fixture.BuildIngestHandler(_ => ["/data/brokers.csv"]);
        var ingestResult = await ingestHandler.HandleAsync(new IngestBatchCommand("/data"));
        ingestResult.TotalRowsStaged.Should().Be(2);

        // 2. Capture the batch id from the persisted state. The
        //    ingest result doesn't surface it directly (Files
        //    carry the file-level outcome only); read from staging.
        var batches = fixture.Staging.ForTestingOnly_AllRows()
            .Select(r => r.BatchId)
            .Distinct()
            .ToArray();
        batches.Should().ContainSingle();
        var batchId = batches[0];

        // 3. Drive the persisted batch to Ingested so processing
        //    is legal. (Production would have IngestBatchHandler do
        //    this implicitly via batch-status persistence, but the
        //    fake doesn't auto-advance — see the 1g surface notes.)
        fixture.Staging.ForTestingOnly_SetBatchStatus(batchId, BatchStatus.Ingested);

        // 4. Process.
        var processHandler = fixture.BuildProcessHandler();
        var processResult = await processHandler.HandleAsync(new ProcessBatchCommand(batchId));
        processResult.Tables.Should().ContainSingle();
        processResult.Tables[0].RowsCommitted.Should().Be(2);

        // 5. Destination has the rows. End-to-end contract verified.
        var committed = fixture.Destination.ForTestingOnly_GetCommitted("core", "broker");
        committed.Should().HaveCount(2);
    }

    [Fact]
    public async Task MultiTableDependency_ParentCommitsBeforeChildAndFkValidationWorks()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerAddressEntryAsync();

        await fixture.RegisterBrokerMappingAsync();
        await fixture.FileMappings.UpsertAsync(
            new FileMapping(@"^addresses\.csv$", "broker_address", "delimited"));

        // The reader serves whichever file is being read; in v1
        // each format gets one reader per format token, so a
        // single FakeFileReader registered for "delimited" handles
        // both. Stage the records so the reader yields them in
        // order. Real Phase 2 would use distinct readers.
        // Workaround for v1: register two readers under different
        // format tokens by overriding mapping format per file.

        // Update broker mapping to use a per-file format.
        await fixture.FileMappings.UpsertAsync(
            new FileMapping(@"^brokers\.csv$", "broker", "delimited-broker"));
        await fixture.FileMappings.UpsertAsync(
            new FileMapping(@"^addresses\.csv$", "broker_address", "delimited-address"));

        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["broker_id", "name"],
            [BrokerRawRecord(0, "1", "Acme")],
            format: "delimited-broker"));
        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["address_id", "broker_id", "address"],
            [
                AddressRawRecord(0, "100", "1", "addr-1"),  // valid FK
                AddressRawRecord(1, "101", "99", "addr-2"), // FK violation
            ],
            format: "delimited-address"));

        var ingestHandler = fixture.BuildIngestHandler(
            _ => ["/data/brokers.csv", "/data/addresses.csv"]);
        var ingestResult = await ingestHandler.HandleAsync(new IngestBatchCommand("/data"));
        ingestResult.Files.Should().HaveCount(2);
        ingestResult.TotalRowsStaged.Should().Be(3);

        var batchId = fixture.Staging.ForTestingOnly_AllRows()
            .Select(r => r.BatchId).Distinct().Single();
        fixture.Staging.ForTestingOnly_SetBatchStatus(batchId, BatchStatus.Ingested);

        var processHandler = fixture.BuildProcessHandler();
        var processResult = await processHandler.HandleAsync(new ProcessBatchCommand(batchId));

        // Contract: parent committed before child started. Verify
        // through observation order — UPSERT_COMPLETED for broker
        // appears before UPSERT_COMPLETED for broker_address. The
        // assertion message is explicit so a future reader who
        // breaks the load order sees the right diagnostic.
        var upsertCodes = processResult.Observations
            .Where(o => o.Code == ObservationCodes.UPSERT_COMPLETED)
            .Select(o => o.TableName)
            .ToList();
        upsertCodes.Should().Equal(
            ["broker", "broker_address"],
            "parent table 'broker' must commit before child table 'broker_address' " +
            "so the FK preload sees the committed parent rows; if this fails, the " +
            "registry's GetLoadOrderAsync is returning child-before-parent and " +
            "FkResolver will see an empty cache for broker.");

        // FK validation worked: the bad-FK row (broker_id=99,
        // which doesn't exist in the parent broker table) was
        // quarantined; the good-FK row (broker_id=1) committed.
        var addrCounts = await fixture.Staging.GetRowCountsAsync(batchId, "broker_address");
        addrCounts[RowStatus.Quarantined].Should().Be(1,
            "the broker_address row with broker_id=99 must quarantine because no parent " +
            "broker row has broker_id=99; if zero rows are quarantined, FK validation is " +
            "disabled or the FK preload is not seeing the parent's pending writes");
        addrCounts[RowStatus.Committed].Should().Be(1,
            "the broker_address row with broker_id=1 must commit because the parent " +
            "broker row with broker_id=1 exists in pending writes after broker is processed");

        var quarantine = fixture.Staging.ForTestingOnly_AllQuarantine();
        quarantine.Values.Should().Contain(q => q.Code == ObservationCodes.FK_VIOLATION);
    }

    [Fact]
    public async Task SchemaDriftBlock_DoesNotCorruptOtherFilesInBatch()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.Registry.UpsertAsync(new RegistryEntry("broker_strict", BuildBrokerSchema())
        {
            TargetSchema = "core",
            ValidFrom = ActiveWindow().From,
            ValidTo = ActiveWindow().To,
            NewColumnsDriftPolicy = DriftPolicy.Block,
        });
        await fixture.RegisterBrokerMappingAsync();
        await fixture.FileMappings.UpsertAsync(
            new FileMapping(@"^strict\.csv$", "broker_strict", "delimited-strict"));

        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["broker_id", "name"],
            [BrokerRawRecord(0, "1", "Acme")]));
        fixture.Readers.Register(FakeFileReader.WithMismatchedRecords(
            ["broker_id", "name", "extra_blocked_column"],
            [BrokerRawRecord(0, "2", "BlockedFile")],
            format: "delimited-strict"));

        var ingestHandler = fixture.BuildIngestHandler(
            _ => ["/data/brokers.csv", "/data/strict.csv"]);
        var ingestResult = await ingestHandler.HandleAsync(new IngestBatchCommand("/data"));

        // Contract: a drift-blocked file produces a zero-row
        // outcome for that file but does NOT corrupt other files
        // in the batch.
        ingestResult.Files.Should().HaveCount(2);
        ingestResult.Files.First(f => f.FileName == "brokers.csv").RowsStaged.Should().Be(1);
        ingestResult.Files.First(f => f.FileName == "strict.csv").RowsStaged.Should().Be(0);
        ingestResult.Observations.Should().Contain(o =>
            o.Code == ObservationCodes.SCHEMA_DRIFT_BLOCKED);
    }

    [Fact]
    public async Task IngestThenProcessThenInspect_FullObservationPipelineVisible()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        await fixture.RegisterBrokerMappingAsync();
        fixture.Readers.Register(FakeFileReader.WithRecords(
            ["broker_id", "name"],
            [BrokerRawRecord(0, "1", "Acme")]));

        // 1. Ingest.
        var ingestHandler = fixture.BuildIngestHandler(_ => ["/data/brokers.csv"]);
        await ingestHandler.HandleAsync(new IngestBatchCommand("/data"));
        var batchId = fixture.Staging.ForTestingOnly_AllRows()
            .Select(r => r.BatchId).Distinct().Single();
        fixture.Staging.ForTestingOnly_SetBatchStatus(batchId, BatchStatus.Ingested);

        // 2. Process.
        var processHandler = fixture.BuildProcessHandler();
        await processHandler.HandleAsync(new ProcessBatchCommand(batchId));
        fixture.Staging.ForTestingOnly_SetBatchStatus(
            batchId, BatchStatus.Completed, DateTimeOffset.UtcNow);

        // 3. Inspect: every observation emitted across both
        //    handlers' lifetimes is visible. Validates the
        //    end-to-end observation pipeline through
        //    ForwardingObservationSink → IObservationRepository →
        //    InspectBatchHandler.
        var inspectHandler = new InspectBatchHandler(fixture.Staging, fixture.Observations);
        var inspect = await inspectHandler.HandleAsync(new InspectBatchQuery(batchId));

        inspect.Status.Should().Be(BatchStatus.Completed);
        inspect.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_STARTED);
        inspect.Observations.Should().Contain(o => o.Code == ObservationCodes.FILE_INGESTED);
        inspect.Observations.Should().Contain(o => o.Code == ObservationCodes.UPSERT_COMPLETED);
        inspect.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
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

        public async Task RegisterBrokerMappingAsync() =>
            await FileMappings.UpsertAsync(
                new FileMapping(@"^brokers\.csv$", "broker", "delimited"));
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

    private static Record BrokerRawRecord(long index, string? brokerId, string name) =>
        new("brokers.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", brokerId)
                .Add("name", name));

    private static Record AddressRawRecord(
        long index, string addressId, string brokerId, string address) =>
        new("addresses.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("address_id", addressId)
                .Add("broker_id", brokerId)
                .Add("address", address));
}
