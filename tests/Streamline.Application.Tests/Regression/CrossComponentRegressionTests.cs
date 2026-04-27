using System.Collections.Immutable;
using System.Reflection;
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
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Regression;

/// <summary>
/// Cross-component predecessor-bug regression tests. Each test is
/// tagged with <see cref="PreventsPredecessorBugAttribute"/> naming
/// the bug it guards against. The meta-test in this file (commit 5)
/// asserts every <c>[Fact]</c> here carries the attribute.
/// </summary>
public class CrossComponentRegressionTests
{
    [Fact]
    [PreventsPredecessorBug("PB-1",
        "predecessor's FkResolver was scoped longer than a single batch; cached FK " +
        "values from batch N validated batch N+1's rows. When batch N+1 had a new " +
        "parent value, child rows referencing it were quarantined as FK violations " +
        "because the cache didn't reflect the new parent. Streamline constructs a " +
        "fresh FkResolver per ProcessingOrchestrator.ExecuteAsync call so cache state " +
        "cannot leak across batches; the lazy per-entry preload from 1h reinforces " +
        "this by reading destination state at process-time, not orchestrator-startup.")]
    public async Task FkResolver_CacheDoesNotLeakAcrossBatches()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerAndBrokerAddressEntriesAsync();

        // Pre-seed broker_id=1 into the destination — the only
        // parent value visible to batch 1's FK preload.
        fixture.Destination.ForTestingOnly_SeedCommitted(
            "core", "broker", BuildBrokerSchema(),
            [BrokerTypedRecord(0, brokerId: 1, "Acme")]);

        // Batch 1: a broker_address referencing broker_id=1. Should
        // commit (FK satisfied).
        var batch1 = await fixture.SeedAddressBatchAsync(
            [AddressTypedRecord(0, addressId: 100, brokerId: 1, "addr-1")]);
        var processHandler = fixture.BuildProcessHandler();
        var result1 = await processHandler.HandleAsync(new ProcessBatchCommand(batch1));
        result1.Tables.First(t => t.TableName == "broker_address").RowsCommitted.Should().Be(1,
            "the broker_address row references broker_id=1 which is in the destination; " +
            "it must commit");

        // Add a fresh parent value to the destination, NOT visible
        // to batch 1's hypothetical leaked cache.
        fixture.Destination.ForTestingOnly_SeedCommitted(
            "core", "broker", BuildBrokerSchema(),
            [BrokerTypedRecord(0, brokerId: 4, "Globex")]);

        // Batch 2: a broker_address referencing broker_id=4. With a
        // fresh FkResolver, the FK preload re-reads the destination
        // and sees {1, 4}. Without the fresh-resolver guarantee the
        // cache would still hold {1} only, and broker_id=4 would
        // quarantine.
        var batch2 = await fixture.SeedAddressBatchAsync(
            [AddressTypedRecord(0, addressId: 200, brokerId: 4, "addr-2")]);
        var result2 = await processHandler.HandleAsync(new ProcessBatchCommand(batch2));
        result2.Tables.First(t => t.TableName == "broker_address").RowsCommitted.Should().Be(1,
            "the broker_address row references broker_id=4 which was added to the " +
            "destination after batch 1 committed; if the FkResolver cache leaked " +
            "from batch 1, broker_id=4 would not be in the cache and the row would " +
            "quarantine. Streamline's per-batch FkResolver must see both broker_id=1 " +
            "and broker_id=4 on this preload");

        // Sanity: nothing was quarantined.
        var counts = await fixture.Staging.GetRowCountsAsync(batch2, "broker_address");
        counts[RowStatus.Quarantined].Should().Be(0);
    }

    [Fact]
    [PreventsPredecessorBug("PB-4",
        "predecessor's quarantine write happened inside the same transaction as the " +
        "regular row write; if the transaction rolled back, the quarantine record was " +
        "lost and the next retry re-encountered the same bad row without record of " +
        "the prior failure. Streamline writes quarantine through IStagingRepository, " +
        "which is independent of the destination's ITransactionScope — quarantine " +
        "writes survive any destination-side rollback (savepoint or outer transaction).")]
    public async Task QuarantineWritesSurviveDestinationUpsertFailure()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerAndBrokerAddressEntriesAsync();

        // Pre-seed broker_id=1 so the FK preload returns a non-empty
        // cache. The valid row references broker_id=1 (will pass FK
        // and be queued for upsert); the bad row references
        // broker_id=99 (will fail FK and quarantine BEFORE the upsert
        // attempt).
        fixture.Destination.ForTestingOnly_SeedCommitted(
            "core", "broker", BuildBrokerSchema(),
            [BrokerTypedRecord(0, brokerId: 1, "Acme")]);

        var batch = await fixture.SeedAddressBatchAsync(
        [
            AddressTypedRecord(0, addressId: 100, brokerId: 1, "addr-valid"),
            AddressTypedRecord(1, addressId: 101, brokerId: 99, "addr-bad-fk"),
        ]);

        // Wire the failing adapter. The orchestrator will FK-validate
        // (good row passes, bad row quarantines), then attempt the
        // upsert which will throw. The orchestrator's per-table catch
        // rolls back the savepoint and marks the validated row as
        // RolledBack. The quarantine write happened earlier and is
        // not in the savepoint's scope.
        var failingDestination = new FailingOnUpsertDestinationAdapter(fixture.Destination);
        var sink = new ForwardingObservationSink(fixture.Observations);
        var orchestrator = new ProcessingOrchestrator(
            fixture.Registry, fixture.Staging, failingDestination);
        var handler = new ProcessBatchHandler(
            fixture.Staging, orchestrator, sink,
            NullLogger<ResilientObservationSink>.Instance);

        var result = await handler.HandleAsync(new ProcessBatchCommand(batch));

        // Contract: quarantine records persist after the per-table
        // upsert failure. Verify via ForTestingOnly_AllQuarantine
        // (the audit-trail surface) — the bad-FK row's quarantine
        // record is there.
        var quarantine = fixture.Staging.ForTestingOnly_AllQuarantine();
        quarantine.Should().HaveCount(1,
            "exactly one row had a bad FK and should be quarantined; the upsert " +
            "failure must not erase the quarantine record");
        quarantine.Values.Single().Code.Should().Be(ObservationCodes.FK_VIOLATION);

        // The valid row was rolled back because the upsert failed.
        var counts = await fixture.Staging.GetRowCountsAsync(batch, "broker_address");
        counts[RowStatus.Quarantined].Should().Be(1);
        counts[RowStatus.RolledBack].Should().Be(1);
        counts[RowStatus.Committed].Should().Be(0);

        // UPSERT_FAILED observation fired so operators see the
        // failure mode.
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.UPSERT_FAILED);
    }

    // ---- meta-test ---------------------------------------------------

    /// <summary>
    /// Self-policing discipline: every <c>[Fact]</c> and
    /// <c>[Theory]</c> method in this class must carry a
    /// <see cref="PreventsPredecessorBugAttribute"/> naming the
    /// predecessor bug it guards against. Mirrors the same meta-
    /// test in <c>Streamline.Domain.Tests.Batches.StateMachine.RowStateMachineRegressionTests</c>;
    /// per Q4 of the 1i plan, per-assembly meta-tests rather than
    /// a centralized cross-assembly scan.
    /// </summary>
    [Fact]
    public void All_regression_tests_carry_a_predecessor_bug_attribute()
    {
        var regressionTests = typeof(CrossComponentRegressionTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m =>
                m.GetCustomAttributes<FactAttribute>(true).Any() ||
                m.GetCustomAttributes<TheoryAttribute>(true).Any())
            .Where(m => m.Name != nameof(All_regression_tests_carry_a_predecessor_bug_attribute))
            .ToArray();

        regressionTests.Should().NotBeEmpty(
            "the class must contain at least one regression test (sanity check)");

        var missingAttribute = regressionTests
            .Where(m => m.GetCustomAttribute<PreventsPredecessorBugAttribute>() is null)
            .Select(m => m.Name)
            .ToArray();

        missingAttribute.Should().BeEmpty(
            "every regression test must carry a [PreventsPredecessorBug] attribute " +
            "naming the bug it guards against. A future contributor adding a test " +
            "here without the attribute fails this meta-test immediately. " +
            "Missing on: {0}",
            string.Join(", ", missingAttribute));
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

        public async Task RegisterBrokerAndBrokerAddressEntriesAsync()
        {
            await Registry.UpsertAsync(BuildBrokerEntry());
            await Registry.UpsertAsync(BuildBrokerAddressEntry());
        }

        public async Task<BatchId> SeedAddressBatchAsync(IReadOnlyList<Record> records)
        {
            var batch = await Staging.StartBatchAsync("/data");
            var fileLogId = await Staging.OpenFileLogAsync(batch, "addresses.csv");
            await Staging.BulkInsertIncomingAsync(
                batch, fileLogId, "broker_address", AsyncEnumerable(records));
            // Drive batch to Ingested so processing's BeginProcessing
            // is legal.
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
