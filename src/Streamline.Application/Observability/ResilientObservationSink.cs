using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Streamline.Core.Observations;

namespace Streamline.Application.Observability;

/// <summary>
/// Decorator over <see cref="IObservationSink"/> that adds retry-with-
/// backoff for transient failures and graceful degradation for
/// sustained failures. Resolves parked decision P-1 from sub-phase 1b.
/// </summary>
/// <remarks>
/// <para>
/// <b>Behaviour.</b>
/// <list type="number">
///   <item>Try the underlying sink. On success, reset the per-batch
///     degradation state's consecutive-failure counter.</item>
///   <item>On failure (any non-cancellation exception), retry up to
///     <see cref="MaxRetries"/> times with exponential backoff
///     (50ms, 200ms, 800ms — see <see cref="DefaultBackoffDelays"/>).</item>
///   <item>If every retry fails, log the final exception via
///     <see cref="ILogger"/> and increment the degradation state's
///     consecutive-failure counter.</item>
///   <item>When the state's counter reaches
///     <see cref="ObservationDegradationState.FailureThreshold"/>,
///     the batch is marked degraded for the rest of its lifetime.
///     Subsequent <see cref="RecordAsync"/> calls drop non-critical
///     observations silently and route critical observations through
///     <see cref="ILogger"/> only.</item>
/// </list>
/// </para>
/// <para>
/// <b>Cancellation propagates.</b>
/// <see cref="OperationCanceledException"/> is never swallowed; it
/// surfaces to the caller per the
/// <see cref="IObservationSink"/> contract and the 1f Q7 cancellation
/// semantics.
/// </para>
/// <para>
/// <b>"Critical" detection</b> uses <see cref="Observation.Severity"/>
/// rather than a parallel code list. Single source of truth — if a
/// future code is added at <see cref="ObservationSeverity.Critical"/>,
/// this decorator gives it the right treatment automatically.
/// </para>
/// <para>
/// <b>Tunability.</b> <see cref="MaxRetries"/> and
/// <see cref="DefaultBackoffDelays"/> are hardcoded constants for v1
/// simplicity. If Phase 4 production tuning shows different values
/// are needed, they become constructor parameters; YAGNI on
/// configurability until a real case shows up.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "This decorator emits at most ~12 log lines per failing batch (3 retries × 3 " +
                    "consecutive failures + a few critical-fallback paths). The LoggerMessage " +
                    "source-generator pattern's perf benefit only matters in tight per-row hot " +
                    "loops; here it's noise-to-signal cost.")]
[SuppressMessage(
    "Performance",
    "CA1873:Avoid potentially expensive logging argument evaluation",
    Justification = "All log arguments here are trivial property accesses (Code, Message, BatchId, " +
                    "ConsecutiveFailures). The analyzer cannot statically prove that and warns " +
                    "conservatively; the cost is zero on this path.")]
public sealed class ResilientObservationSink : IObservationSink
{
    /// <summary>
    /// V1 default backoff delays applied before each retry: 50ms,
    /// 200ms, 800ms. Tests construct with shorter (or zero) delays
    /// via the explicit constructor; production sites use
    /// <see cref="WithDefaults"/>.
    /// </summary>
    public static IReadOnlyList<TimeSpan> DefaultBackoffDelays { get; } =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(800),
    ];

    /// <summary>
    /// Number of retry attempts after the initial try. Derived from
    /// the length of the backoff-delays sequence — one delay per
    /// retry.
    /// </summary>
    public int MaxRetries => _backoffDelays.Count;

    private readonly IObservationSink _inner;
    private readonly ILogger<ResilientObservationSink> _logger;
    private readonly ObservationDegradationState _state;
    private readonly IReadOnlyList<TimeSpan> _backoffDelays;

    /// <summary>
    /// Construct with explicit backoff delays. Most production
    /// callers should use <see cref="WithDefaults"/> instead, which
    /// applies the v1 defaults; this constructor is primarily for
    /// tests that want zero-delay or accelerated backoff.
    /// </summary>
    public ResilientObservationSink(
        IObservationSink inner,
        ILogger<ResilientObservationSink> logger,
        ObservationDegradationState state,
        IReadOnlyList<TimeSpan> backoffDelays)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(backoffDelays);

        foreach (var d in backoffDelays)
        {
            if (d < TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Backoff delays must be non-negative.", nameof(backoffDelays));
            }
        }

        _inner = inner;
        _logger = logger;
        _state = state;
        _backoffDelays = backoffDelays;
    }

    /// <summary>
    /// Production factory: applies the v1 default backoff delays
    /// (<see cref="DefaultBackoffDelays"/>).
    /// </summary>
    public static ResilientObservationSink WithDefaults(
        IObservationSink inner,
        ILogger<ResilientObservationSink> logger,
        ObservationDegradationState state) =>
        new(inner, logger, state, DefaultBackoffDelays);

    public async Task RecordAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (_state.IsDegraded)
        {
            // Once degraded, drop non-critical observations silently.
            // Critical observations go through ILogger as fallback so
            // the operator sees the failure even when the sink isn't
            // working.
            if (observation.Severity == ObservationSeverity.Critical)
            {
                _logger.LogCritical(
                    "Critical observation in degraded mode: {Code} {Message} (batch {BatchId})",
                    observation.Code, observation.Message, observation.BatchId);
            }
            return;
        }

        Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            // Wait before retries (not before the first attempt).
            if (attempt > 0)
            {
                var delay = _backoffDelays[attempt - 1];
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                await _inner.RecordAsync(observation, cancellationToken).ConfigureAwait(false);
                _state.RecordSuccess();
                return;
            }
            catch (OperationCanceledException)
            {
                // Cancellation always propagates.
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "Observation sink failure (attempt {Attempt}/{MaxAttempts}) for code {Code} in batch {BatchId}",
                    attempt + 1, MaxRetries + 1, observation.Code, observation.BatchId);
            }
        }

        // All attempts exhausted. Count this as one consecutive-batch
        // failure; if the threshold is crossed, the state will report
        // degraded on the next call.
        _state.RecordFailure();

        if (_state.IsDegraded)
        {
            _logger.LogWarning(
                "Observation sink degraded for batch {BatchId} after {ConsecutiveFailures} consecutive failures",
                observation.BatchId, _state.ConsecutiveFailures);
        }

        if (observation.Severity == ObservationSeverity.Critical)
        {
            _logger.LogCritical(lastException,
                "Critical observation could not be recorded after {Attempts} attempts: {Code} {Message} (batch {BatchId})",
                MaxRetries + 1, observation.Code, observation.Message, observation.BatchId);
        }

        // Don't rethrow — graceful degradation. The decorator is the
        // resilience boundary.
    }
}
