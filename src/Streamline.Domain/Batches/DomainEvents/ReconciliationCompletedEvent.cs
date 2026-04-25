namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Emitted after the built-in row-count reconciliation runs at batch
/// completion. Phase 4 introduces the actual reconciliation runner
/// (<c>BuiltinReconciliationRunner</c>); the event shape lands here
/// in sub-phase 1d so the domain vocabulary is complete from the
/// state-machine layer up.
/// </summary>
/// <remarks>
/// <b>Phase-1 placeholder payload.</b> Phase 4 may extend this with
/// richer detail (per-table breakdowns, the actual count
/// dictionaries). For now: passed-or-not plus an optional summary.
/// </remarks>
/// <param name="BatchId">The batch.</param>
/// <param name="OccurredAt">When reconciliation finished, UTC.</param>
/// <param name="Passed">
/// True if the engine's built-in row-count reconciliation matched
/// expected counts. False produces a <c>RECONCILIATION_MISMATCH</c>
/// observation alongside this event.
/// </param>
/// <param name="Summary">
/// Optional human-readable summary of what reconciliation found.
/// Null when <paramref name="Passed"/> is true and there's nothing
/// to elaborate.
/// </param>
public sealed record class ReconciliationCompletedEvent(
    BatchId BatchId,
    DateTimeOffset OccurredAt,
    bool Passed,
    string? Summary = null) : IDomainEvent;
