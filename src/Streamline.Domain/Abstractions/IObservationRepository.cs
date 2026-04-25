using Streamline.Core.Observations;
using Streamline.Domain.Batches;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Persistence and query contract for observations. Backs
/// <see cref="IObservationSink"/> (which is the hot-path emission
/// API) — sinks typically buffer observations and flush them
/// through this repository's batch path.
/// </summary>
/// <remarks>
/// <para>
/// <b>Method set is intentionally minimal.</b> v1 needs write paths
/// (single + batch, for the sink's flush) and read paths scoped to a
/// batch (for <c>--inspect</c>, sub-phase 1f and Phase 4).
/// Observation-id lookup, code-filtered queries, and count queries
/// are deliberately omitted — add them when a real caller needs
/// them, not speculatively.
/// </para>
/// <para>
/// <b>Failure semantics</b> follow <see cref="IObservationSink"/>'s
/// contract: exceptions propagate. Production implementations buffer
/// and retry to keep hot-path callers from blocking on persistence;
/// fakes throw eagerly so test bugs surface.
/// </para>
/// </remarks>
public interface IObservationRepository
{
    /// <summary>
    /// Persist a single observation. Atomic with respect to
    /// observers reading the same batch — either the observation is
    /// visible to subsequent reads or it isn't.
    /// </summary>
    Task RecordAsync(Observation observation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist a batch of observations in one round-trip. Used by
    /// the sink's flush path. Atomic per call: implementations
    /// either persist all or none.
    /// </summary>
    Task RecordBatchAsync(IReadOnlyList<Observation> observations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream every observation recorded for a batch, in
    /// <see cref="Observation.RaisedAt"/> order. The
    /// <c>--inspect &lt;batch&gt;</c> command consumes this directly.
    /// </summary>
    IAsyncEnumerable<Observation> GetForBatchAsync(BatchId batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream observations recorded for a batch at or above
    /// <paramref name="minSeverity"/>, in
    /// <see cref="Observation.RaisedAt"/> order. Useful for surfacing
    /// only the operator-relevant subset
    /// (<see cref="ObservationSeverity.Warning"/> and above).
    /// </summary>
    IAsyncEnumerable<Observation> GetForBatchAsync(
        BatchId batch,
        ObservationSeverity minSeverity,
        CancellationToken cancellationToken = default);
}
