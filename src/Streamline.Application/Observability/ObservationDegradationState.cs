namespace Streamline.Application.Observability;

/// <summary>
/// Per-batch state for tracking consecutive observation-sink
/// failures. <see cref="ResilientObservationSink"/> consults this
/// to decide whether the batch's observability should remain in
/// normal operation or shift to degraded mode (dropping non-
/// critical observations silently).
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-batch instance.</b> The orchestrator constructs one of
/// these per batch, hands it to the decorator, and reads
/// <see cref="IsDegraded"/> at end-of-batch to populate the result
/// type's <c>ObservabilityDegraded</c> flag. Sharing a single
/// instance across batches would leak degradation: a network blip
/// during batch A would make every subsequent batch see itself as
/// degraded from the start. That's the whole point of per-batch
/// scoping.
/// </para>
/// <para>
/// <b>Thread safety.</b> The decorator's calls into this class
/// happen from whatever thread the orchestrator's loop is running
/// on. Phase 1 orchestrators are single-threaded; if Phase 4
/// introduces parallel processing across rows, this state needs
/// either an explicit lock or atomic counters. For now, plain
/// non-volatile fields are correct because there's no concurrent
/// access.
/// </para>
/// </remarks>
public sealed class ObservationDegradationState
{
    /// <summary>
    /// Threshold at which the state shifts to degraded. Hardcoded
    /// for v1; if Phase 4 production tuning shows different values
    /// are needed, this becomes a constructor parameter.
    /// </summary>
    public const int FailureThreshold = 3;

    private int _consecutiveFailures;

    /// <summary>
    /// True once <see cref="RecordFailure"/> has been called
    /// <see cref="FailureThreshold"/> times consecutively without
    /// an intervening <see cref="RecordSuccess"/>. Once true, stays
    /// true for the rest of the batch — degradation does not
    /// recover within a batch.
    /// </summary>
    public bool IsDegraded { get; private set; }

    /// <summary>
    /// Current count of consecutive failures since the last success.
    /// Exposed primarily for testing; orchestrator code should use
    /// <see cref="IsDegraded"/>.
    /// </summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>
    /// Reset the consecutive-failure counter. Called by the decorator
    /// after a successful sink call.
    /// </summary>
    public void RecordSuccess()
    {
        _consecutiveFailures = 0;
    }

    /// <summary>
    /// Increment the consecutive-failure counter. Called by the
    /// decorator after all retries on a single observation are
    /// exhausted. When the counter reaches
    /// <see cref="FailureThreshold"/>, the batch is marked degraded
    /// for the remainder of its lifetime.
    /// </summary>
    public void RecordFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= FailureThreshold)
        {
            IsDegraded = true;
        }
    }
}
