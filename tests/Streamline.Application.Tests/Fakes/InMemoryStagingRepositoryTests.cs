using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Streamline.Application.Tests.Regression;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Batches;
using Streamline.Domain.Batches.StateMachine;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryStagingRepositoryTests
{
    // ---- StartBatchAsync ----------------------------------------------

    [Fact]
    public async Task StartBatchAsync_AssignsSequentialBatchIds()
    {
        var repo = new InMemoryStagingRepository();

        var first = await repo.StartBatchAsync("/data");
        var second = await repo.StartBatchAsync("/data");
        var third = await repo.StartBatchAsync("/data");

        first.Value.Should().Be("batch-1");
        second.Value.Should().Be("batch-2");
        third.Value.Should().Be("batch-3");
    }

    [Fact]
    public async Task StartBatchAsync_BlankSource_Throws()
    {
        var repo = new InMemoryStagingRepository();
        var act = async () => await repo.StartBatchAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StartBatchAsync_ConcurrentCalls_ProduceDistinctIds()
    {
        var repo = new InMemoryStagingRepository();
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => repo.StartBatchAsync("/data")))
            .ToArray();

        var ids = await Task.WhenAll(tasks);

        ids.Distinct().Should().HaveCount(50);
    }

    // ---- GetBatchAsync ------------------------------------------------

    [Fact]
    public async Task GetBatchAsync_UnknownId_ReturnsNull()
    {
        var repo = new InMemoryStagingRepository();
        var snapshot = await repo.GetBatchAsync(new BatchId("batch-missing"));
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task GetBatchAsync_KnownId_ReturnsSnapshot()
    {
        var repo = new InMemoryStagingRepository();
        var id = await repo.StartBatchAsync("/data");

        var snapshot = await repo.GetBatchAsync(id);

        snapshot.Should().NotBeNull();
        snapshot!.BatchId.Should().Be(id);
        snapshot.Source.Should().Be("/data");
        snapshot.Status.Should().Be(BatchStatus.Created);
        snapshot.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetBatchAsync_ReflectsForTestingOnly_StatusOverride()
    {
        var repo = new InMemoryStagingRepository();
        var id = await repo.StartBatchAsync("/data");
        var completedAt = DateTimeOffset.UtcNow;
        repo.ForTestingOnly_SetBatchStatus(id, BatchStatus.Failed, completedAt);

        var snapshot = await repo.GetBatchAsync(id);

        snapshot!.Status.Should().Be(BatchStatus.Failed);
        snapshot.CompletedAt.Should().Be(completedAt);
    }

    // ---- OpenFileLogAsync ---------------------------------------------

    [Fact]
    public async Task OpenFileLogAsync_AssignsSequentialFileLogIds()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");

        var first = await repo.OpenFileLogAsync(batch, "a.csv");
        var second = await repo.OpenFileLogAsync(batch, "b.csv");

        first.Should().Be(1L);
        second.Should().Be(2L);
    }

    [Fact]
    public async Task OpenFileLogAsync_UnknownBatch_Throws()
    {
        var repo = new InMemoryStagingRepository();
        var act = async () => await repo.OpenFileLogAsync(new BatchId("batch-missing"), "a.csv");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- BulkInsertIncomingAsync --------------------------------------

    [Fact]
    public async Task BulkInsertIncomingAsync_ReturnsInsertedCount()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLogId = await repo.OpenFileLogAsync(batch, "a.csv");

        var inserted = await repo.BulkInsertIncomingAsync(
            batch, fileLogId, "broker", AsyncEnumerable(MakeRecord(0), MakeRecord(1), MakeRecord(2)));

        inserted.Should().Be(3L);
    }

    [Fact]
    public async Task BulkInsertIncomingAsync_RowsArePending()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLogId = await repo.OpenFileLogAsync(batch, "a.csv");
        await repo.BulkInsertIncomingAsync(
            batch, fileLogId, "broker", AsyncEnumerable(MakeRecord(0), MakeRecord(1)));

        var counts = await repo.GetRowCountsAsync(batch, "broker");

        counts[RowStatus.Pending].Should().Be(2);
        counts[RowStatus.Processing].Should().Be(0);
    }

    [Fact]
    public async Task BulkInsertIncomingAsync_UnknownFileLog_Throws()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");

        var act = async () => await repo.BulkInsertIncomingAsync(
            batch, fileLogId: 999L, "broker", AsyncEnumerable(MakeRecord(0)));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- GetPendingRowsAsync ------------------------------------------

    [Fact]
    public async Task GetPendingRowsAsync_TransitionsRowsToProcessingOnYield()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLogId = await repo.OpenFileLogAsync(batch, "a.csv");
        await repo.BulkInsertIncomingAsync(
            batch, fileLogId, "broker", AsyncEnumerable(MakeRecord(0), MakeRecord(1)));

        var rows = new List<StagedRow>();
        await foreach (var row in repo.GetPendingRowsAsync(batch, "broker", limit: 100))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(2);
        rows.Should().AllSatisfy(r => r.Status.Should().Be(RowStatus.Processing));
        var counts = await repo.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Pending].Should().Be(0);
        counts[RowStatus.Processing].Should().Be(2);
    }

    [Fact]
    public async Task GetPendingRowsAsync_RespectsLimit()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLogId = await repo.OpenFileLogAsync(batch, "a.csv");
        await repo.BulkInsertIncomingAsync(
            batch, fileLogId, "broker", AsyncEnumerable(MakeRecord(0), MakeRecord(1), MakeRecord(2)));

        var rows = new List<StagedRow>();
        await foreach (var row in repo.GetPendingRowsAsync(batch, "broker", limit: 2))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPendingRowsAsync_OrdersByFileLogThenIncomingId()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLog1 = await repo.OpenFileLogAsync(batch, "first.csv");
        var fileLog2 = await repo.OpenFileLogAsync(batch, "second.csv");
        // Insert into the SECOND file log first, then the first.
        await repo.BulkInsertIncomingAsync(batch, fileLog2, "broker", AsyncEnumerable(MakeRecord(0)));
        await repo.BulkInsertIncomingAsync(batch, fileLog1, "broker", AsyncEnumerable(MakeRecord(0)));

        var rows = new List<StagedRow>();
        await foreach (var row in repo.GetPendingRowsAsync(batch, "broker", limit: 100))
        {
            rows.Add(row);
        }

        rows.Select(r => r.FileLogId).Should().Equal(fileLog1, fileLog2);
    }

    [Fact]
    [PreventsPredecessorBug("PB-8",
        "predecessor allowed concurrent --process invocations on the same batch to " +
        "claim the same row twice: both invocations transitioned Pending -> Processing " +
        "without atomic check, both proceeded to upsert, producing duplicate destination " +
        "rows or insert errors. Streamline's GetPendingRowsAsync atomically transitions " +
        "Pending -> Processing under a lock (in-memory) or via SELECT FOR UPDATE SKIP " +
        "LOCKED (Postgres in Phase 2); two concurrent consumers see disjoint claims.")]
    public async Task GetPendingRowsAsync_TwoConcurrentConsumers_NoRowDoubleClaimed()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLogId = await repo.OpenFileLogAsync(batch, "a.csv");
        await repo.BulkInsertIncomingAsync(
            batch, fileLogId, "broker",
            AsyncEnumerable(Enumerable.Range(0, 100).Select(i => MakeRecord(i)).ToArray()));

        async Task<List<long>> Drain()
        {
            var ids = new List<long>();
            await foreach (var row in repo.GetPendingRowsAsync(batch, "broker", limit: 1000))
            {
                ids.Add(row.IncomingId);
            }
            return ids;
        }

        var consumerA = Task.Run(Drain);
        var consumerB = Task.Run(Drain);
        var resultA = await consumerA;
        var resultB = await consumerB;

        // Together the two consumers see every row exactly once.
        var combined = resultA.Concat(resultB).OrderBy(x => x).ToArray();
        combined.Distinct().Should().HaveCount(combined.Length);
        combined.Should().HaveCount(100);
    }

    // ---- TransitionAsync ----------------------------------------------

    [Fact]
    public async Task TransitionAsync_LegalTransition_UpdatesStatus()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 1);
        await ClaimRowsAsync(repo, batch);

        await repo.TransitionAsync(ids[0], RowStatus.Processing, RowStatus.Committed);

        var counts = await repo.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Committed].Should().Be(1);
    }

    [Fact]
    public async Task TransitionAsync_StatusMismatch_Throws()
    {
        var (repo, _, _, ids) = await SeedAsync(rowCount: 1);

        // Row is in Pending, not Processing.
        var act = async () => await repo.TransitionAsync(ids[0], RowStatus.Processing, RowStatus.Committed);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*concurrent transition*");
    }

    [Fact]
    public async Task TransitionAsync_IllegalRowStateTransition_Throws()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 1);
        await ClaimRowsAsync(repo, batch);
        await repo.TransitionAsync(ids[0], RowStatus.Processing, RowStatus.Committed);

        // Committed is terminal.
        var act = async () => await repo.TransitionAsync(ids[0], RowStatus.Committed, RowStatus.Pending);

        await act.Should().ThrowAsync<IllegalStateTransitionException>();
    }

    [Fact]
    public async Task TransitionAsync_UnknownRow_Throws()
    {
        var repo = new InMemoryStagingRepository();
        var act = async () => await repo.TransitionAsync(999L, RowStatus.Pending, RowStatus.Processing);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- QuarantineAsync ----------------------------------------------

    [Fact]
    public async Task QuarantineAsync_FromPending_TransitionsAndRecordsEntry()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 1);

        await repo.QuarantineAsync(ids[0], "MISSING_REQUIRED", "amount was null");

        var counts = await repo.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Quarantined].Should().Be(1);
        var quarantine = repo.ForTestingOnly_AllQuarantine();
        quarantine[ids[0]].Should().Be(("MISSING_REQUIRED", "amount was null"));
    }

    [Fact]
    public async Task QuarantineAsync_FromProcessing_Succeeds()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 1);
        await ClaimRowsAsync(repo, batch);

        await repo.QuarantineAsync(ids[0], "FK_VIOLATION", "broker_id has no parent");

        var counts = await repo.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Quarantined].Should().Be(1);
    }

    [Fact]
    public async Task QuarantineAsync_FromCommitted_Throws()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 1);
        await ClaimRowsAsync(repo, batch);
        await repo.TransitionAsync(ids[0], RowStatus.Processing, RowStatus.Committed);

        var act = async () => await repo.QuarantineAsync(ids[0], "X", "y");

        await act.Should().ThrowAsync<IllegalStateTransitionException>();
    }

    // ---- ResetForRetryAsync -------------------------------------------

    [Fact]
    public async Task ResetForRetryAsync_TransitionsRolledBackToPending()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 2);
        await ClaimRowsAsync(repo, batch);
        await repo.TransitionAsync(ids[0], RowStatus.Processing, RowStatus.RolledBack);
        await repo.TransitionAsync(ids[1], RowStatus.Processing, RowStatus.RolledBack);

        await repo.ResetForRetryAsync(batch);

        var counts = await repo.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Pending].Should().Be(2);
        counts[RowStatus.RolledBack].Should().Be(0);
    }

    [Fact]
    public async Task ResetForRetryAsync_LeavesQuarantinedRowsAlone()
    {
        var (repo, batch, _, ids) = await SeedAsync(rowCount: 2);
        await repo.QuarantineAsync(ids[0], "X", "y");
        await ClaimRowsAsync(repo, batch);  // claims the remaining Pending row
        await repo.TransitionAsync(ids[1], RowStatus.Processing, RowStatus.RolledBack);

        await repo.ResetForRetryAsync(batch);

        var counts = await repo.GetRowCountsAsync(batch, "broker");
        counts[RowStatus.Quarantined].Should().Be(1);
        counts[RowStatus.Pending].Should().Be(1);
    }

    // ---- GetRowCountsAsync --------------------------------------------

    [Fact]
    public async Task GetRowCountsAsync_AlwaysCarriesEverySeverity()
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");

        var counts = await repo.GetRowCountsAsync(batch, "broker");

        counts.Should().ContainKeys(
            RowStatus.Pending, RowStatus.Processing, RowStatus.Committed,
            RowStatus.RolledBack, RowStatus.Quarantined);
        counts.Values.Should().AllSatisfy(v => v.Should().Be(0));
    }

    // ---- cancellation -------------------------------------------------

    [Fact]
    public async Task StartBatchAsync_PreCancelledToken_Throws()
    {
        var repo = new InMemoryStagingRepository();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await repo.StartBatchAsync("/data", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetPendingRowsAsync_PreCancelledToken_Throws()
    {
        var (repo, batch, _, _) = await SeedAsync(rowCount: 1);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in repo.GetPendingRowsAsync(batch, "broker", 10, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- helpers ------------------------------------------------------

    private static async Task<(InMemoryStagingRepository Repo, BatchId Batch, long FileLogId, IReadOnlyList<long> IncomingIds)>
        SeedAsync(int rowCount)
    {
        var repo = new InMemoryStagingRepository();
        var batch = await repo.StartBatchAsync("/data");
        var fileLogId = await repo.OpenFileLogAsync(batch, "a.csv");
        var records = Enumerable.Range(0, rowCount).Select(MakeRecord).ToArray();
        await repo.BulkInsertIncomingAsync(batch, fileLogId, "broker", AsyncEnumerable(records));
        var ids = repo.ForTestingOnly_AllRows().Select(r => r.IncomingId).ToList();
        return (repo, batch, fileLogId, ids);
    }

    private static async Task ClaimRowsAsync(InMemoryStagingRepository repo, BatchId batch)
    {
        await foreach (var _ in repo.GetPendingRowsAsync(batch, "broker", limit: 1000))
        {
        }
    }

    private static Record MakeRecord(int rowIndex) =>
        new("a.csv", rowIndex,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", rowIndex)
                .Add("name", $"row-{rowIndex}"));

    private static async IAsyncEnumerable<Record> AsyncEnumerable(
        params Record[] records)
    {
        foreach (var r in records)
        {
            yield return r;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<Record> AsyncEnumerable(
        IReadOnlyList<Record> records,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var r in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return r;
            await Task.Yield();
        }
    }
}
