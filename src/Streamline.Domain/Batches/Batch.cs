using Streamline.Core.Enums;
using Streamline.Domain.Batches.DomainEvents;
using Streamline.Domain.Batches.StateMachine;

namespace Streamline.Domain.Batches;

/// <summary>
/// The aggregate that authorises every row state transition in a
/// batch and records what happened as domain events.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Batch aggregate is a policy gate, not a row store.</b> It
/// validates state transitions and emits events. Row state is owned
/// by <c>IStagingRepository</c>; counts come from
/// <c>IStagingRepository.GetRowCountsAsync</c>. The aggregate does
/// not cache either. Holding rows in the aggregate doesn't survive
/// Phase 2: a million-row batch can't fit in memory, and "the
/// aggregate is the system of record" stops being true the moment
/// persistence is real.
/// </para>
/// <para>
/// <b>How this looks in code.</b>
/// <see cref="TransitionRow"/> takes a <see cref="StagedRow"/> as
/// input, validates the aggregate identity match, validates
/// state-machine legality, and (for quarantine) emits an event. It
/// returns nothing — it does NOT return an updated
/// <see cref="StagedRow"/>. The orchestrator (sub-phase 1g) is
/// responsible for: fetching the row from staging, calling the
/// aggregate, then calling <c>IStagingRepository.TransitionAsync</c>.
/// The aggregate never reads from any repository.
/// </para>
/// <para>
/// <b>Events are fire-and-collect.</b> Mutating methods accumulate
/// events into a private list; <see cref="DrainEvents"/> returns the
/// snapshot and clears the list. The orchestrator drains after the
/// operation block completes — events are never visible mid-mutation.
/// </para>
/// <para>
/// <b>Time.</b> The aggregate uses
/// <see cref="DateTimeOffset.UtcNow"/> directly for event
/// timestamps. Phase 1 doesn't need controllable time; later phases
/// inject a <c>TimeProvider</c> when a test actually requires it.
/// </para>
/// </remarks>
public sealed class Batch
{
    private readonly List<IDomainEvent> _events = [];

    public BatchId Id { get; }
    public string Source { get; }
    public BatchStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Batch(BatchId id, string source, DateTimeOffset startedAt)
    {
        Id = id;
        Source = source;
        StartedAt = startedAt;
        Status = BatchStatus.Created;
    }

    /// <summary>
    /// Open a fresh aggregate. The batch starts in
    /// <see cref="BatchStatus.Created"/>; call <see cref="Start"/>
    /// to begin ingestion.
    /// </summary>
    public static Batch Create(BatchId id, string source)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException(
                "BatchId must be initialised; default(BatchId) is not a valid value.",
                nameof(id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(source, nameof(source));

        return new Batch(id, source, DateTimeOffset.UtcNow);
    }

    // ---- batch-status transitions ---------------------------------------

    /// <summary>
    /// <see cref="BatchStatus.Created"/> → <see cref="BatchStatus.Ingesting"/>.
    /// Emits <see cref="BatchStartedEvent"/>.
    /// </summary>
    public void Start()
    {
        BatchStateMachine.RequireLegal(Status, BatchStatus.Ingesting,
            reason: $"batch_id={Id}");
        Status = BatchStatus.Ingesting;
        _events.Add(new BatchStartedEvent(Id, DateTimeOffset.UtcNow, Source));
    }

    /// <summary>
    /// <see cref="BatchStatus.Ingesting"/> → <see cref="BatchStatus.Ingested"/>.
    /// No event — the orchestrator decides which observation to emit.
    /// </summary>
    public void MarkIngested()
    {
        BatchStateMachine.RequireLegal(Status, BatchStatus.Ingested,
            reason: $"batch_id={Id}");
        Status = BatchStatus.Ingested;
    }

    /// <summary>
    /// <see cref="BatchStatus.Ingested"/> → <see cref="BatchStatus.Processing"/>
    /// (initial processing) OR
    /// <see cref="BatchStatus.Failed"/> → <see cref="BatchStatus.Processing"/>
    /// (retry). No event — the orchestrator emits
    /// <c>BATCH_RETRIED</c> on the retry path; first-pass entry is
    /// implicit in <see cref="BatchStartedEvent"/>'s context.
    /// </summary>
    public void BeginProcessing()
    {
        BatchStateMachine.RequireLegal(Status, BatchStatus.Processing,
            reason: $"batch_id={Id}");
        Status = BatchStatus.Processing;
    }

    /// <summary>
    /// <see cref="BatchStatus.Processing"/> → <see cref="BatchStatus.Completed"/>.
    /// Sets <see cref="CompletedAt"/>; emits <see cref="BatchCompletedEvent"/>.
    /// Terminal — every transition out of <see cref="BatchStatus.Completed"/>
    /// is illegal.
    /// </summary>
    public void Complete()
    {
        BatchStateMachine.RequireLegal(Status, BatchStatus.Completed,
            reason: $"batch_id={Id}");
        var completedAt = DateTimeOffset.UtcNow;
        Status = BatchStatus.Completed;
        CompletedAt = completedAt;
        _events.Add(new BatchCompletedEvent(Id, completedAt));
    }

    /// <summary>
    /// <see cref="BatchStatus.Processing"/> → <see cref="BatchStatus.Failed"/>.
    /// Sets <see cref="CompletedAt"/>; emits <see cref="BatchFailedEvent"/>
    /// with <paramref name="reason"/>. The only escape is
    /// <see cref="BeginProcessing"/> via retry; <c>Failed → Completed</c>
    /// directly is illegal.
    /// </summary>
    public void Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));
        BatchStateMachine.RequireLegal(Status, BatchStatus.Failed,
            reason: $"batch_id={Id}");
        var completedAt = DateTimeOffset.UtcNow;
        Status = BatchStatus.Failed;
        CompletedAt = completedAt;
        _events.Add(new BatchFailedEvent(Id, completedAt, reason));
    }

    // ---- row-status transitions -----------------------------------------

    /// <summary>
    /// Validate a row state transition. Verifies
    /// <paramref name="row"/> belongs to this batch and that the
    /// transition <c>row.Status → toStatus</c> is permitted by
    /// <see cref="RowStateMachine"/>. Does not emit an event;
    /// non-quarantine row transitions surface through orchestrator-
    /// emitted observations rather than typed domain events.
    /// </summary>
    /// <remarks>
    /// Returns nothing. The aggregate is a policy gate, not a row
    /// store — it does not produce an updated row reflecting the new
    /// status. The orchestrator constructs whatever post-transition
    /// shape it needs from its own context.
    /// </remarks>
    public void TransitionRow(StagedRow row, RowStatus toStatus)
    {
        EnsureRowBelongsToThisBatch(row);
        RowStateMachine.RequireLegal(row.Status, toStatus,
            reason: $"incoming_id={row.IncomingId}");
    }

    /// <summary>
    /// Quarantine a row. Validates the row belongs to this batch and
    /// that <c>row.Status → Quarantined</c> is legal (Pending or
    /// Processing). Emits <see cref="RowQuarantinedEvent"/> with the
    /// failure code and message — the orchestrator picks these up
    /// and turns them into observations.
    /// </summary>
    public void QuarantineRow(StagedRow row, string code, string message)
    {
        EnsureRowBelongsToThisBatch(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        RowStateMachine.RequireLegal(row.Status, RowStatus.Quarantined,
            reason: $"incoming_id={row.IncomingId}");

        _events.Add(new RowQuarantinedEvent(
            Id,
            DateTimeOffset.UtcNow,
            row.IncomingId,
            row.TargetTable,
            code,
            message));
    }

    // ---- event accumulator ----------------------------------------------

    /// <summary>
    /// Snapshot the accumulated events and clear the buffer. The
    /// orchestrator calls this after the operation block completes;
    /// callers must not mutate the returned list.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DrainEvents()
    {
        var snapshot = _events.ToArray();
        _events.Clear();
        return snapshot;
    }

    // ---- equality (by id) -----------------------------------------------

    public override bool Equals(object? obj) =>
        obj is Batch other && Id.Equals(other.Id);

    public override int GetHashCode() => Id.GetHashCode();

    // ---- private --------------------------------------------------------

    private void EnsureRowBelongsToThisBatch(StagedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.BatchId.Equals(Id))
        {
            throw new ArgumentException(
                $"StagedRow belongs to batch '{row.BatchId}'; this aggregate is '{Id}'.",
                nameof(row));
        }
    }
}
