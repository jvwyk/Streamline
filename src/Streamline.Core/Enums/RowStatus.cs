namespace Streamline.Core.Enums;

/// <summary>
/// Lifecycle of a single staged row as it moves through ingestion and
/// processing. Transitions are enforced by the Domain-layer state machine;
/// illegal transitions throw. See docs/streamline-plan.md §5.3.
/// </summary>
public enum RowStatus
{
    /// <summary>
    /// Entered via staging insert during ingestion (T1). The row is queued
    /// for processing but no worker has claimed it yet.
    /// </summary>
    Pending,

    /// <summary>
    /// Entered from <see cref="Pending"/> when a worker claims the row
    /// under an exclusive lock (T2). Processing is in progress.
    /// </summary>
    Processing,

    /// <summary>
    /// Entered from <see cref="Processing"/> (T3) after the upsert
    /// (replication) or the transformer invocation (transform) succeeds
    /// and the transaction commits. Terminal — illegal to transition
    /// out of.
    /// </summary>
    Committed,

    /// <summary>
    /// Entered from <see cref="Processing"/> (T4) when a savepoint rolls
    /// back or a transformer fails. Retryable: <c>RetryBatchCommand</c>
    /// transitions back to <see cref="Pending"/> (T7).
    /// </summary>
    RolledBack,

    /// <summary>
    /// Entered from <see cref="Pending"/> (T5, during ingestion) or from
    /// <see cref="Processing"/> (T6, during processing) on validation or
    /// DB error. Survives rollback (the quarantine write is autocommitted).
    /// Requires manual resolution to move back to <see cref="Pending"/> (T8).
    /// </summary>
    Quarantined,
}
