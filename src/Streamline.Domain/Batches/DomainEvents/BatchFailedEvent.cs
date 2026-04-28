namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Emitted when the batch transitions to
/// <see cref="Streamline.Core.Enums.BatchStatus.Failed"/> — typically
/// when a Critical observation was raised or processing was aborted.
/// Symmetric with <see cref="BatchCompletedEvent"/>; the orchestrator
/// will emit different observations for completion vs failure, and
/// having a typed event makes that trivial to dispatch.
/// </summary>
/// <param name="BatchId">The batch.</param>
/// <param name="OccurredAt">When the transition happened, UTC.</param>
/// <param name="Reason">
/// Short description of why the batch failed — "advisory lock
/// contention", "schema drift blocked", "transformer threw", etc.
/// Free-form; not cross-checked against a code catalog. The
/// observation that accompanied the failure carries the full
/// structured context.
/// </param>
public sealed record class BatchFailedEvent(
    BatchId BatchId,
    DateTimeOffset OccurredAt,
    string Reason) : IDomainEvent
{
    public string Reason { get; } = RequireNonBlank(Reason, nameof(Reason));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
