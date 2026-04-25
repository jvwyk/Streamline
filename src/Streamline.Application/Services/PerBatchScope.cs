using Microsoft.Extensions.Logging;
using Streamline.Application.Observability;
using Streamline.Core.Observations;

namespace Streamline.Application.Services;

/// <summary>
/// Bundle of per-batch observability dependencies wired up for a
/// single <c>Batch</c>. Owned by the handler, passed to the
/// orchestrator. A new scope is constructed for every batch so that
/// degradation state, the resilient sink decorator, and the event
/// publisher can never leak across batches — predecessor flag #11
/// (cache persists across scopes) is the same kind of bug.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three pieces wired together.</b>
/// <list type="bullet">
///   <item><see cref="DegradationState"/> — fresh per batch; counts
///     consecutive sink failures and trips the
///     <see cref="ObservationDegradationState.IsDegraded"/> flag once
///     <see cref="ObservationDegradationState.FailureThreshold"/> is
///     crossed.</item>
///   <item><see cref="Sink"/> — a <see cref="ResilientObservationSink"/>
///     decorator built around the inner production sink. Retries
///     transient failures and routes critical observations through
///     <see cref="ILogger"/> in degraded mode.</item>
///   <item><see cref="EventPublisher"/> — a
///     <see cref="DomainEventPublisher"/> that translates aggregate
///     events to observations and forwards through
///     <see cref="Sink"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>How handlers use it.</b> Construct via
/// <see cref="CreateForBatch"/>, hand to the orchestrator, read
/// <see cref="ObservationDegradationState.IsDegraded"/> at end-of-
/// batch to populate the result type's
/// <c>ObservabilityDegraded</c> flag. The scope is not disposable;
/// drop it by going out of scope.
/// </para>
/// </remarks>
public sealed class PerBatchScope
{
    public ObservationDegradationState DegradationState { get; }
    public IObservationSink Sink { get; }
    public DomainEventPublisher EventPublisher { get; }

    private PerBatchScope(
        ObservationDegradationState degradationState,
        IObservationSink sink,
        DomainEventPublisher eventPublisher)
    {
        DegradationState = degradationState;
        Sink = sink;
        EventPublisher = eventPublisher;
    }

    /// <summary>
    /// Build a fresh scope for one batch. Wires a new
    /// <see cref="ObservationDegradationState"/>, a
    /// <see cref="ResilientObservationSink"/> with v1 default backoff
    /// around <paramref name="innerSink"/>, and a
    /// <see cref="DomainEventPublisher"/> over that decorator.
    /// </summary>
    public static PerBatchScope CreateForBatch(
        IObservationSink innerSink,
        ILogger<ResilientObservationSink> logger)
    {
        ArgumentNullException.ThrowIfNull(innerSink);
        ArgumentNullException.ThrowIfNull(logger);

        var state = new ObservationDegradationState();
        var resilient = ResilientObservationSink.WithDefaults(innerSink, logger, state);
        var publisher = new DomainEventPublisher(resilient);

        return new PerBatchScope(state, resilient, publisher);
    }
}
