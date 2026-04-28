namespace Streamline.Core.Enums;

/// <summary>
/// Lifecycle of a <c>Batch</c> aggregate from creation to terminal state.
/// The batch state is coarser than the per-row state in
/// <see cref="RowStatus"/>; it tracks which phase of the pipeline the
/// batch is in, not the fate of individual rows. See
/// docs/streamline-plan.md §5.3.
///
/// The batch model is intentionally not symmetric with the row model.
/// There is an <see cref="Ingesting"/> / <see cref="Ingested"/> split
/// because ingestion and processing are distinct phases (retry
/// re-enters processing without re-ingesting), but there is no
/// corresponding "Processed" intermediate — a batch in
/// <see cref="Processing"/> transitions directly to <see cref="Completed"/>
/// or <see cref="Failed"/>. Row terminal states (Committed, Quarantined,
/// RolledBack) carry the per-row resolution; the batch state carries
/// the phase.
/// </summary>
public enum BatchStatus
{
    /// <summary>
    /// Initial state. Entered when a batch row is first inserted; no
    /// files have been ingested yet.
    /// </summary>
    Created,

    /// <summary>
    /// Entered from <see cref="Created"/> when <c>IngestBatch</c> begins
    /// staging files. Remains until every file in the batch has been
    /// fully staged or rejected.
    /// </summary>
    Ingesting,

    /// <summary>
    /// Entered from <see cref="Ingesting"/> when every file has finished
    /// staging (including any quarantined rows). Processing has not
    /// started.
    /// </summary>
    Ingested,

    /// <summary>
    /// Entered from <see cref="Ingested"/> when <c>ProcessBatch</c> starts
    /// working through pending rows. Also re-entered from <see cref="Failed"/>
    /// (or from <see cref="Completed"/> if still-retryable rows exist)
    /// via <c>RetryBatchCommand</c>.
    /// </summary>
    Processing,

    /// <summary>
    /// Terminal success. Entered from <see cref="Processing"/> when every
    /// row has reached a terminal state (<see cref="RowStatus.Committed"/>
    /// or <see cref="RowStatus.Quarantined"/>) and no <c>Critical</c>
    /// observation has been raised.
    /// </summary>
    Completed,

    /// <summary>
    /// Terminal failure. Entered when a <c>Critical</c> observation is
    /// raised or processing is aborted. Retryable via
    /// <c>RetryBatchCommand</c>, which moves the batch back to
    /// <see cref="Processing"/>.
    /// </summary>
    Failed,
}
