using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Queries;
using Streamline.Application.Services;
using Streamline.Application.Tests.Fakes;
using Streamline.Core.Enums;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Integration;

/// <summary>
/// Cross-handler negative-path tests. Per-handler negative cases
/// (one bad input throws, one bad input is rejected) live in the
/// per-handler integration test classes; this file holds the cases
/// that span multiple handlers OR document a parked-decision
/// behavior the test suite has to keep stable until that decision
/// is resolved.
/// </summary>
public class NegativePathTests
{
    [Fact]
    public async Task ProcessBatch_UnknownBatchId_ThrowsKeyNotFound()
    {
        var fixture = new Fixture();
        var processHandler = fixture.BuildProcessHandler();

        var act = async () => await processHandler.HandleAsync(
            new ProcessBatchCommand(new BatchId("batch-missing")));

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*batch-missing*");
    }

    [Fact]
    public async Task RetryBatch_UnknownBatchId_ThrowsKeyNotFound()
    {
        var fixture = new Fixture();
        var retryHandler = fixture.BuildRetryHandler();

        var act = async () => await retryHandler.HandleAsync(
            new RetryBatchCommand(new BatchId("batch-missing")));

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*batch-missing*");
    }

    [Fact]
    public async Task InspectBatch_UnknownBatchId_ThrowsKeyNotFound()
    {
        var fixture = new Fixture();
        var inspectHandler = new InspectBatchHandler(fixture.Staging, fixture.Observations);

        var act = async () => await inspectHandler.HandleAsync(
            new InspectBatchQuery(new BatchId("batch-missing")));

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*batch-missing*");
    }

    [Fact]
    public async Task ProcessingRows_AreNotResetByRetry_DocumentingP8()
    {
        // Documents the P-8 parked behavior. When a batch is
        // cancelled mid-processing, rows transitioned Pending ->
        // Processing remain in Processing indefinitely — the
        // ResetForRetryAsync contract only handles RolledBack rows.
        // This test asserts the CURRENT (correct-but-gappy)
        // behavior so the suite stays stable until Phase 4 lands a
        // recovery path. When that ships, this test will be
        // updated to reflect the new contract.
        var staging = new InMemoryStagingRepository();
        var batch = await staging.StartBatchAsync("/data");
        var fileLogId = await staging.OpenFileLogAsync(batch, "x.csv");
        await staging.BulkInsertIncomingAsync(
            batch, fileLogId, "broker", AsyncEnumerable(BrokerRecord(0, 1, "Acme")));

        // Simulate mid-processing claim: GetPendingRowsAsync
        // atomically transitions Pending -> Processing.
        await foreach (var _ in staging.GetPendingRowsAsync(batch, "broker", limit: 100))
        {
        }

        // Drive the batch to Failed (simulating the orchestrator's
        // batch.Fail() call after a non-cancellation exception).
        staging.ForTestingOnly_SetBatchStatus(
            batch, BatchStatus.Failed, completedAt: DateTimeOffset.UtcNow);

        // Retry. Only RolledBack rows reset to Pending; the
        // Processing row is left untouched.
        await staging.ResetForRetryAsync(batch);

        var counts = await staging.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Processing].Should().Be(1,
            "P-8: rows transitioned Pending -> Processing during a cancelled or failed " +
            "batch remain stuck in Processing; ResetForRetryAsync only resets RolledBack " +
            "rows by contract. When Phase 4 introduces a separate recovery path " +
            "(ResetClaimedAsync or claim-timeout) this test gets updated.");
        counts[RowStatus.Pending].Should().Be(0);
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

        public RetryBatchHandler BuildRetryHandler()
        {
            var sink = new ForwardingObservationSink(Observations);
            var orchestrator = new ProcessingOrchestrator(Registry, Staging, Destination);
            return new RetryBatchHandler(
                Staging, orchestrator, sink,
                NullLogger<ResilientObservationSink>.Instance);
        }
    }

    private static Record BrokerRecord(long index, int brokerId, string name) =>
        new("brokers.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", brokerId)
                .Add("name", name));

    private static async IAsyncEnumerable<Record> AsyncEnumerable(params Record[] records)
    {
        foreach (var r in records)
        {
            yield return r;
            await Task.Yield();
        }
    }
}
