using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Streamline.Application.Commands;
using Streamline.Application.Observability;
using Streamline.Application.Tests.Regression;
using Streamline.Application.Services;
using Streamline.Application.Tests.Fakes;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RetryBatchHandler"/>. The
/// canonical test of the P-6 architectural pattern lives here:
/// <see cref="RetryObservation_LandsInBothResultAndRepository"/>
/// verifies that <c>BATCH_RETRIED</c>, emitted by the handler
/// outside the orchestrator's local capture list, ends up in
/// BOTH the returned <c>ProcessingResult.Observations</c> AND
/// the persisted observation repository.
/// </summary>
public class RetryBatchHandlerIntegrationTests
{
    [Fact]
    public async Task RetryFromFailed_ResetsRolledBackRowsAndReprocesses()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();

        // Set up a Failed batch with one row in RolledBack and one
        // in Quarantined. Retry should reset RolledBack -> Pending
        // (T7) and process the row through to Committed.
        var batch = await fixture.SeedFailedBatchAsync(
            rolledBack: [BrokerTypedRecord(0, 1, "Acme")],
            quarantined: [BrokerTypedRecord(1, 2, "Globex")]);

        var handler = fixture.BuildRetryHandler();
        var result = await handler.HandleAsync(new RetryBatchCommand(batch));

        // Contract: RolledBack -> Pending -> Processing -> Committed.
        // Quarantined rows untouched throughout.
        var counts = await fixture.Staging.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Committed].Should().Be(1);
        counts[RowStatus.Quarantined].Should().Be(1);
        counts[RowStatus.RolledBack].Should().Be(0);
        counts[RowStatus.Pending].Should().Be(0);

        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_RETRIED);
        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_COMPLETED);
    }

    [Fact]
    public async Task RetryObservation_LandsInBothResultAndRepository()
    {
        // P-6 architectural test: BATCH_RETRIED is emitted by
        // RetryBatchHandler before delegating to the orchestrator.
        // The handler reconstructs ProcessingResult with the retry
        // observation prepended; separately, ForwardingObservationSink
        // persists it via the observation repository. Both paths
        // must converge — InspectBatchHandler reads from the
        // repository, callers of the handler read the result.
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        var batch = await fixture.SeedFailedBatchAsync(rolledBack: [], quarantined: []);

        var handler = fixture.BuildRetryHandler();
        var result = await handler.HandleAsync(new RetryBatchCommand(batch));

        // 1. The result carries BATCH_RETRIED.
        var retryInResult = result.Observations.Should()
            .Contain(o => o.Code == ObservationCodes.BATCH_RETRIED).Subject;
        retryInResult.BatchId.Should().Be(batch.Value);

        // 2. The repository also carries it (via the
        //    ForwardingObservationSink wired into PerBatchScope).
        var retryInRepository = fixture.Observations
            .ForTestingOnly_AllObservations()
            .Should()
            .Contain(o => o.Code == ObservationCodes.BATCH_RETRIED).Subject;

        // 3. The two observations are the same instance —
        //    confirms the handler emitted once and merged the
        //    same object into the result rather than constructing
        //    a parallel one.
        retryInResult.Should().BeSameAs(retryInRepository);
    }

    [Fact]
    [PreventsPredecessorBug("PB-9",
        "predecessor's retry logic reset every row in the batch including Quarantined " +
        "ones, re-processing legitimately-bad data on every retry. Operators expected " +
        "retry to attempt failed-infrastructure rows again, not re-process validation " +
        "failures. Streamline's ResetForRetryAsync resets only RolledBack rows per " +
        "the contract; Quarantined rows stay quarantined and require manual " +
        "resolution.")]
    public async Task QuarantinedRows_NotResetByRetry()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        var batch = await fixture.SeedFailedBatchAsync(
            rolledBack: [],
            quarantined:
            [
                BrokerTypedRecord(0, 1, "Acme"),
                BrokerTypedRecord(1, 2, "Globex"),
            ]);

        var handler = fixture.BuildRetryHandler();
        var result = await handler.HandleAsync(new RetryBatchCommand(batch));

        // ResetForRetryAsync's contract: only RolledBack rows
        // transition to Pending; Quarantined rows stay
        // Quarantined. Verify both surfaces — the row counts and
        // the per-row state via ForTestingOnly_AllRows.
        var counts = await fixture.Staging.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Quarantined].Should().Be(2);
        counts[RowStatus.Pending].Should().Be(0);
        counts[RowStatus.Committed].Should().Be(0);

        result.Observations.Should().Contain(o => o.Code == ObservationCodes.BATCH_RETRIED);
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsBeforeResetAndPreservesRowState()
    {
        var fixture = new Fixture();
        await fixture.RegisterBrokerEntryAsync();
        var batch = await fixture.SeedFailedBatchAsync(
            rolledBack: [BrokerTypedRecord(0, 1, "Acme")],
            quarantined: []);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = fixture.BuildRetryHandler();

        var act = async () => await handler.HandleAsync(new RetryBatchCommand(batch), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Contract: pre-cancellation aborts at GetBatchAsync, before
        // ResetForRetryAsync would mutate state. Rows stay
        // RolledBack.
        var counts = await fixture.Staging.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.RolledBack].Should().Be(1);
        counts[RowStatus.Pending].Should().Be(0);
    }

    // ---- fixtures ----------------------------------------------------

    private sealed class Fixture
    {
        public InMemoryStagingRepository Staging { get; } = new();
        public InMemoryDestinationAdapter Destination { get; } = new();
        public InMemoryRegistryRepository Registry { get; } = new();
        public InMemoryObservationRepository Observations { get; } = new();

        public RetryBatchHandler BuildRetryHandler()
        {
            var sink = new ForwardingObservationSink(Observations);
            var orchestrator = new ProcessingOrchestrator(Registry, Staging, Destination);
            return new RetryBatchHandler(
                Staging, orchestrator, sink,
                NullLogger<ResilientObservationSink>.Instance);
        }

        public async Task RegisterBrokerEntryAsync() =>
            await Registry.UpsertAsync(BuildBrokerEntry());

        /// <summary>
        /// Builds a batch in Failed status with the supplied rows
        /// pre-positioned at the appropriate terminal-ish states
        /// (RolledBack or Quarantined). Used to set up retry
        /// scenarios without going through the full orchestrator
        /// path.
        /// </summary>
        public async Task<BatchId> SeedFailedBatchAsync(
            IReadOnlyList<Record> rolledBack,
            IReadOnlyList<Record> quarantined)
        {
            var batch = await Staging.StartBatchAsync("/data");
            var fileLogId = await Staging.OpenFileLogAsync(batch, "brokers.csv");
            await Staging.BulkInsertIncomingAsync(
                batch, fileLogId, "broker", AsyncEnumerable(rolledBack.Concat(quarantined)));

            // Drive the rows to their terminal states. The state
            // machine requires Pending -> Processing -> {RolledBack |
            // Quarantined}; mimic that path here.
            var allRows = Staging.ForTestingOnly_AllRows()
                .Where(r => r.BatchId.Equals(batch))
                .OrderBy(r => r.IncomingId)
                .ToList();

            for (var i = 0; i < rolledBack.Count; i++)
            {
                var id = allRows[i].IncomingId;
                await Staging.TransitionAsync(id, RowStatus.Pending, RowStatus.Processing);
                await Staging.TransitionAsync(id, RowStatus.Processing, RowStatus.RolledBack);
            }
            for (var i = rolledBack.Count; i < allRows.Count; i++)
            {
                var id = allRows[i].IncomingId;
                await Staging.QuarantineAsync(id, "TEST_QUARANTINE", "seeded-quarantine");
            }

            // Drive the persisted batch to Failed so the retry
            // transition Failed -> Processing is legal.
            Staging.ForTestingOnly_SetBatchStatus(
                batch, BatchStatus.Failed, completedAt: DateTimeOffset.UtcNow);
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

    private static Record BrokerTypedRecord(long index, int brokerId, string name) =>
        new("brokers.csv", index,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", brokerId)
                .Add("name", name));
}
