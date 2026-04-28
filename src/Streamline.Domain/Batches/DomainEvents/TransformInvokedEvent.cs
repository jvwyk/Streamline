using Streamline.Domain.Transforms;

namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Emitted after a transformer invocation completes (successfully or
/// with partial failure). Records the reference that was dispatched
/// and the outcome the transformer returned. A transformer that
/// throws does NOT produce this event — that's a critical failure;
/// the orchestrator emits a <c>TRANSFORMER_THREW</c> observation
/// directly.
/// </summary>
/// <remarks>
/// Emitted by the orchestrator (sub-phase 1f) or the transformer
/// registry (Phase 4), not by the <c>Batch</c> aggregate itself —
/// transformer invocation isn't a row state transition.
/// </remarks>
/// <param name="BatchId">The batch.</param>
/// <param name="OccurredAt">When the transformer returned, UTC.</param>
/// <param name="Reference">The transformer that was invoked.</param>
/// <param name="Outcome">What the transformer reported.</param>
public sealed record class TransformInvokedEvent(
    BatchId BatchId,
    DateTimeOffset OccurredAt,
    TransformReference Reference,
    TransformOutcome Outcome) : IDomainEvent
{
    public TransformReference Reference { get; } = RequireNonNull(Reference, nameof(Reference));
    public TransformOutcome Outcome { get; } = RequireNonNull(Outcome, nameof(Outcome));

    private static T RequireNonNull<T>(T value, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(value, name);
        return value;
    }
}
