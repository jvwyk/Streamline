using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Batches.StateMachine;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IStagingRepository"/>. Mutable
/// list/dictionary state guarded by a single <see cref="_lock"/>.
/// Faithful enough that contract-parity tests in Phase 2 can run
/// the same scenarios against this and against the Postgres
/// implementation and get the same observable end state.
/// </summary>
/// <remarks>
/// <para>
/// <b>Test infrastructure, not production.</b> Lives in the test
/// project; the production implementation is
/// <c>Streamline.Infrastructure.Postgres.PostgresStagingRepository</c>
/// (Phase 2). Marked <c>public</c> rather than <c>internal</c>+IVT
/// so 1h tests can construct without ceremony.
/// </para>
/// <para>
/// <b>Thread safety.</b> Per the contract, "implementations must be
/// safe for concurrent processors operating on disjoint batches."
/// Implemented via a single <see cref="_lock"/> covering all
/// mutations and reads. Simple beats fancy at this scale; if 1h or
/// later phases show measurable contention, optimize then.
/// </para>
/// <para>
/// <b>Faithfulness highlights.</b>
/// <list type="bullet">
///   <item><see cref="GetPendingRowsAsync"/> atomically transitions
///     each yielded row from <see cref="RowStatus.Pending"/> to
///     <see cref="RowStatus.Processing"/> (T2) before yielding,
///     under <see cref="_lock"/>. Two concurrent consumers cannot
///     double-claim the same row.</item>
///   <item><see cref="TransitionAsync"/> rejects status mismatches
///     (the persisted status differs from <c>fromStatus</c>) with
///     <see cref="InvalidOperationException"/> per the contract,
///     and validates the transition via <see cref="RowStateMachine"/>.</item>
///   <item><see cref="ResetForRetryAsync"/> transitions only
///     <see cref="RowStatus.RolledBack"/> rows to
///     <see cref="RowStatus.Pending"/> (T7); quarantine rows are
///     untouched per the contract.</item>
///   <item><see cref="GetRowCountsAsync"/> returns a dictionary with
///     an entry for every <see cref="RowStatus"/> (zero counts for
///     absent statuses) so callers don't need defensive
///     <c>TryGetValue</c>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class InMemoryStagingRepository : IStagingRepository
{
    private readonly object _lock = new();

    private long _batchCounter;
    private long _fileLogCounter;
    private long _incomingCounter;

    private readonly Dictionary<BatchId, BatchEntry> _batches = new();
    private readonly Dictionary<long, FileLogEntry> _fileLogs = new();
    private readonly Dictionary<long, IncomingRow> _incoming = new();
    private readonly Dictionary<long, QuarantineEntry> _quarantine = new();

    public Task<BatchId> StartBatchAsync(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        lock (_lock)
        {
            var id = new BatchId($"batch-{++_batchCounter}");
            _batches[id] = new BatchEntry(
                id, source, BatchStatus.Created, DateTimeOffset.UtcNow, CompletedAt: null);
            return Task.FromResult(id);
        }
    }

    public Task<BatchSnapshot?> GetBatchAsync(BatchId batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_batches.TryGetValue(batch, out var entry))
            {
                return Task.FromResult<BatchSnapshot?>(null);
            }
            return Task.FromResult<BatchSnapshot?>(new BatchSnapshot(
                entry.Id, entry.Source, entry.Status, entry.StartedAt, entry.CompletedAt));
        }
    }

    public Task<long> OpenFileLogAsync(BatchId batch, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        lock (_lock)
        {
            EnsureBatchExists(batch);
            var id = ++_fileLogCounter;
            _fileLogs[id] = new FileLogEntry(id, batch, fileName);
            return Task.FromResult(id);
        }
    }

    public async Task<long> BulkInsertIncomingAsync(
        BatchId batch,
        long fileLogId,
        string targetTable,
        IAsyncEnumerable<Record> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);
        cancellationToken.ThrowIfCancellationRequested();

        long inserted = 0;
        await foreach (var record in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            ArgumentNullException.ThrowIfNull(record);
            lock (_lock)
            {
                EnsureBatchExists(batch);
                EnsureFileLogExists(fileLogId);
                var id = ++_incomingCounter;
                _incoming[id] = new IncomingRow(
                    id, batch, fileLogId, targetTable, record, RowStatus.Pending);
                inserted++;
            }
        }
        return inserted;
    }

    public async IAsyncEnumerable<StagedRow> GetPendingRowsAsync(
        BatchId batch,
        string targetTable,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        cancellationToken.ThrowIfCancellationRequested();

        // Snapshot-and-claim under the lock: pick up to `limit`
        // candidate rows in (file_log_id, incoming_id) order, atomically
        // transition each Pending → Processing, then release the lock
        // and yield. Two concurrent consumers serialize on the lock and
        // see disjoint claims — no row is double-claimed.
        List<StagedRow> claimed = [];
        lock (_lock)
        {
            var ordered = _incoming.Values
                .Where(r => r.BatchId.Equals(batch)
                         && r.TargetTable == targetTable
                         && r.Status == RowStatus.Pending)
                .OrderBy(r => r.FileLogId)
                .ThenBy(r => r.IncomingId)
                .Take(limit);

            foreach (var row in ordered)
            {
                RowStateMachine.RequireLegal(row.Status, RowStatus.Processing,
                    reason: $"incoming_id={row.IncomingId}");
                _incoming[row.IncomingId] = row with { Status = RowStatus.Processing };
                claimed.Add(new StagedRow(
                    row.IncomingId, row.BatchId, row.TargetTable, row.Record,
                    RowStatus.Processing, row.FileLogId));
            }
        }

        foreach (var row in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    public Task TransitionAsync(
        long incomingId,
        RowStatus fromStatus,
        RowStatus toStatus,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_incoming.TryGetValue(incomingId, out var row))
            {
                throw new InvalidOperationException(
                    $"No incoming row with id {incomingId}.");
            }
            if (row.Status != fromStatus)
            {
                throw new InvalidOperationException(
                    $"Row {incomingId} is in status {row.Status}, not {fromStatus}; concurrent transition.");
            }
            RowStateMachine.RequireLegal(fromStatus, toStatus, reason: $"incoming_id={incomingId}");
            _incoming[incomingId] = row with { Status = toStatus };
        }
        return Task.CompletedTask;
    }

    public Task QuarantineAsync(
        long incomingId,
        string code,
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_lock)
        {
            if (!_incoming.TryGetValue(incomingId, out var row))
            {
                throw new InvalidOperationException(
                    $"No incoming row with id {incomingId}.");
            }
            RowStateMachine.RequireLegal(row.Status, RowStatus.Quarantined,
                reason: $"incoming_id={incomingId}");
            _incoming[incomingId] = row with { Status = RowStatus.Quarantined };
            _quarantine[incomingId] = new QuarantineEntry(incomingId, code, message);
        }
        return Task.CompletedTask;
    }

    public Task ResetForRetryAsync(BatchId batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            EnsureBatchExists(batch);
            var rolledBack = _incoming.Values
                .Where(r => r.BatchId.Equals(batch) && r.Status == RowStatus.RolledBack)
                .ToList();
            foreach (var row in rolledBack)
            {
                RowStateMachine.RequireLegal(RowStatus.RolledBack, RowStatus.Pending,
                    reason: $"incoming_id={row.IncomingId}");
                _incoming[row.IncomingId] = row with { Status = RowStatus.Pending };
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<RowStatus, long>> GetRowCountsAsync(
        BatchId batch,
        string targetTable,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var counts = new Dictionary<RowStatus, long>();
            foreach (var status in Enum.GetValues<RowStatus>())
            {
                counts[status] = 0;
            }
            foreach (var row in _incoming.Values
                .Where(r => r.BatchId.Equals(batch) && r.TargetTable == targetTable))
            {
                counts[row.Status]++;
            }
            return Task.FromResult<IReadOnlyDictionary<RowStatus, long>>(counts);
        }
    }

    /// <summary>
    /// Test-only: drive the persisted batch's status forward without
    /// going through the orchestrator. The aggregate is the source of
    /// truth for production code; this helper lets tests put the
    /// fake into a target state for assertions
    /// (e.g., "what does GetBatchAsync return for a Failed batch?").
    /// </summary>
    public void ForTestingOnly_SetBatchStatus(
        BatchId batch, BatchStatus status, DateTimeOffset? completedAt = null)
    {
        lock (_lock)
        {
            if (!_batches.TryGetValue(batch, out var entry))
            {
                throw new InvalidOperationException($"No batch with id {batch}.");
            }
            _batches[batch] = entry with { Status = status, CompletedAt = completedAt };
        }
    }

    /// <summary>
    /// Test-only: snapshot of every staged row, ordered by
    /// <see cref="StagedRow.IncomingId"/>. Tests use this to assert
    /// the full audit trail when the production query API doesn't
    /// expose enough detail for a particular check.
    /// </summary>
    public ImmutableArray<StagedRow> ForTestingOnly_AllRows()
    {
        lock (_lock)
        {
            return _incoming.Values
                .OrderBy(r => r.IncomingId)
                .Select(r => new StagedRow(
                    r.IncomingId, r.BatchId, r.TargetTable, r.Record, r.Status, r.FileLogId))
                .ToImmutableArray();
        }
    }

    /// <summary>
    /// Test-only: snapshot of every quarantined row's recorded code
    /// and message, keyed by incoming id.
    /// </summary>
    public ImmutableDictionary<long, (string Code, string Message)> ForTestingOnly_AllQuarantine()
    {
        lock (_lock)
        {
            return _quarantine.Values.ToImmutableDictionary(
                q => q.IncomingId,
                q => (q.Code, q.Message));
        }
    }

    private void EnsureBatchExists(BatchId batch)
    {
        if (!_batches.ContainsKey(batch))
        {
            throw new InvalidOperationException($"No batch with id {batch}.");
        }
    }

    private void EnsureFileLogExists(long fileLogId)
    {
        if (!_fileLogs.ContainsKey(fileLogId))
        {
            throw new InvalidOperationException($"No file log with id {fileLogId}.");
        }
    }

    private sealed record class BatchEntry(
        BatchId Id,
        string Source,
        BatchStatus Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt);

    private sealed record class FileLogEntry(long Id, BatchId BatchId, string FileName);

    private sealed record class IncomingRow(
        long IncomingId,
        BatchId BatchId,
        long FileLogId,
        string TargetTable,
        Record Record,
        RowStatus Status);

    private sealed record class QuarantineEntry(long IncomingId, string Code, string Message);
}
