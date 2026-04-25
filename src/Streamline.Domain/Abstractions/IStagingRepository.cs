using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Batches;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// The contract for the staging store: where the engine records
/// which batches exist, which files contributed to each, every row
/// staged from those files, the row's current
/// <see cref="RowStatus"/>, and the quarantine sidecar. First-party
/// implementations: in-memory fake (sub-phase 1h) and Postgres
/// (Phase 2).
/// </summary>
/// <remarks>
/// Implementations must be safe for concurrent processors operating
/// on disjoint batches. Within a single batch, only one processor at
/// a time should claim rows via
/// <see cref="GetPendingRowsAsync"/> + <see cref="TransitionAsync"/>;
/// the Postgres implementation enforces this with
/// <c>SELECT ... FOR UPDATE SKIP LOCKED</c> (Phase 2).
/// </remarks>
public interface IStagingRepository
{
    /// <summary>
    /// Open a new batch and return its identifier. Implementations
    /// must persist a row in <c>batch_log</c> (or the in-memory
    /// equivalent) before the returned task completes. The
    /// <c>source</c> argument is a free-form description of the
    /// batch's origin (typically the directory or invocation
    /// arguments) — recorded for observability, not otherwise
    /// interpreted by the engine.
    /// </summary>
    Task<BatchId> StartBatchAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a <c>file_log</c> entry for an incoming file under a
    /// batch and return its auto-assigned id. Every row staged from
    /// this file will reference the returned id; observations scoped
    /// to the file carry it as <c>FileLogId</c>.
    /// </summary>
    Task<long> OpenFileLogAsync(BatchId batch, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-insert a stream of records into <c>staging.incoming</c>
    /// for a target table under a specific file log. Returns the
    /// count of rows actually inserted (validated rows reach this
    /// path; quarantined rows go through
    /// <see cref="QuarantineAsync"/> instead). Implementations are
    /// expected to use COPY or the equivalent bulk-insert mechanism
    /// for performance.
    /// </summary>
    Task<long> BulkInsertIncomingAsync(
        BatchId batch,
        long fileLogId,
        string targetTable,
        IAsyncEnumerable<Record> rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch up to <paramref name="limit"/> pending rows for a
    /// target table, atomically transitioning each from
    /// <see cref="RowStatus.Pending"/> to
    /// <see cref="RowStatus.Processing"/> as it is yielded.
    /// </summary>
    /// <remarks>
    /// <b>Ordering is part of the contract.</b> Rows are yielded in
    /// file arrival order, then in source-row order within each
    /// file. The semantic guarantee — not the literal ORDER BY —
    /// matters: the Postgres implementation may use
    /// <c>file_log.file_date</c> when that column lands in Phase 2;
    /// the in-memory fake uses <c>file_log_id</c> (insertion order).
    /// Both deliver the same per-call sequence so contract-parity
    /// tests in Phase 2 hold.
    /// </remarks>
    IAsyncEnumerable<StagedRow> GetPendingRowsAsync(
        BatchId batch,
        string targetTable,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Move a row from <paramref name="fromStatus"/> to
    /// <paramref name="toStatus"/>. Implementations must reject
    /// illegal transitions (the row state machine in sub-phase 1d
    /// defines the legal set) and must not silently skip the call
    /// when the current status differs from
    /// <paramref name="fromStatus"/> — that's a concurrency bug
    /// surface and should throw <c>InvalidOperationException</c>.
    /// </summary>
    Task TransitionAsync(
        long incomingId,
        RowStatus fromStatus,
        RowStatus toStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Move a row to <see cref="RowStatus.Quarantined"/> and record
    /// the failure with a stable observation <paramref name="code"/>
    /// and a human-readable <paramref name="message"/>. The
    /// quarantine write is atomic with the status transition; both
    /// either land or neither does.
    /// </summary>
    Task QuarantineAsync(
        long incomingId,
        string code,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transition every <see cref="RowStatus.RolledBack"/> row in a
    /// batch back to <see cref="RowStatus.Pending"/>, re-queuing the
    /// batch for processing. Quarantined rows are NOT touched —
    /// quarantine resolution is a separate, manual operation.
    /// Invoked by <c>RetryBatchCommand</c>.
    /// </summary>
    Task ResetForRetryAsync(BatchId batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Snapshot the per-status row counts for a target table in a
    /// batch. The returned dictionary contains an entry for
    /// <em>every</em> <see cref="RowStatus"/> value, with zero counts
    /// for absent statuses, so callers don't need to defensively
    /// <c>TryGetValue</c>.
    /// </summary>
    Task<IReadOnlyDictionary<RowStatus, long>> GetRowCountsAsync(
        BatchId batch,
        string targetTable,
        CancellationToken cancellationToken = default);
}
