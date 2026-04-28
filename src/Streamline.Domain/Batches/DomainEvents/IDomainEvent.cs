namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Marker interface for events the domain emits as state mutates.
/// Events are facts that have happened — the aggregate or orchestrator
/// accumulates them; outer handlers drain them after the operation
/// block completes (fire-and-collect, not async dispatch). The domain
/// layer never reacts to its own events; that's the orchestrator's
/// job (sub-phase 1f).
/// </summary>
public interface IDomainEvent
{
    /// <summary>The batch the event belongs to.</summary>
    BatchId BatchId { get; }

    /// <summary>
    /// When the event occurred, in UTC. Set by the emitter at event
    /// construction time, never by the caller — the emitter is the
    /// authority on "when" so tests can't pass distorted timestamps.
    /// Phase 1 uses <see cref="DateTimeOffset.UtcNow"/>; later phases
    /// inject a <c>TimeProvider</c> when controllable time is
    /// required (YAGNI until then).
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
