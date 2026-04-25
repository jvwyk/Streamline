namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Emitted when the batch transitions to its terminal-success state
/// <see cref="Streamline.Core.Enums.BatchStatus.Completed"/> — every
/// row reached a terminal status (Committed or Quarantined) and no
/// Critical observation was raised.
/// </summary>
/// <remarks>
/// Carries no row counts. The aggregate is a policy gate, not a row
/// store; counts come from
/// <c>IStagingRepository.GetRowCountsAsync</c> at the point the
/// orchestrator needs them.
/// </remarks>
/// <param name="BatchId">The batch.</param>
/// <param name="OccurredAt">When the transition happened, UTC.</param>
public sealed record class BatchCompletedEvent(
    BatchId BatchId,
    DateTimeOffset OccurredAt) : IDomainEvent;
