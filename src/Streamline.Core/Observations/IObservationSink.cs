namespace Streamline.Core.Observations;

/// <summary>
/// The contract for recording <see cref="Observation"/>s during a
/// batch. The engine's orchestrators, validators, adapters, and
/// transformers all depend on <see cref="IObservationSink"/>; Phase 1
/// provides an in-memory fake, Phase 2 provides a Postgres-backed
/// implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stateless.</b> One sink instance serves every batch in a
/// process. The observation carries its own <see cref="Observation.BatchId"/>
/// and scope fields — the sink does not track "current batch". This
/// avoids the three-ways-to-express-scope problem a scoped sink
/// would create (scope on sink, scope on observation, or both).
/// </para>
/// <para>
/// <b>Single observation per call.</b> Batching is not part of the
/// contract. Implementations may buffer internally and flush on a
/// timer or on a batch-size threshold; callers never see that
/// buffering. If Phase 4 performance testing shows per-call overhead
/// matters, a <c>RecordBatchAsync</c> overload can be added as an
/// additive, non-breaking extension.
/// </para>
/// <para>
/// <b>Failure handling (normative).</b> When recording fails, the
/// task returned by <see cref="RecordAsync"/> faults — exceptions
/// propagate to the caller. Implementations are expected to honour
/// the following contract:
/// <list type="bullet">
///   <item>
///     <b>Production implementations</b> buffer observations
///     internally so hot-path callers (validators running per row,
///     file readers running per line) never block on persistence.
///     Transient failures (connection loss, serialisation hiccup)
///     trigger internal retry; the task faults only when retries
///     are exhausted.
///   </item>
///   <item>
///     <b>Test fakes</b> throw eagerly — tests want to see
///     misuse immediately. An in-memory fake that silently swallows
///     a duplicate-observation-id or malformed-context bug is a
///     fake that lies.
///   </item>
///   <item>
///     <b>Callers</b> generally do not try/catch around
///     <see cref="RecordAsync"/>. An emission failure signals that
///     the engine has lost the ability to observe, which is a
///     batch-level issue — it surfaces as a batch failure rather
///     than a secondary best-effort concern. The explicit exception
///     is when a caller is emitting an observation about an
///     already-failing state and wants to ensure the original cause
///     surfaces first; in that case catching and logging the
///     emission failure is acceptable.
///   </item>
/// </list>
/// The sink itself is <b>not</b> expected to swallow exceptions
/// silently (option 2 in design discussion — hiding failures is the
/// opposite of what an observation-stream engine should do) nor to
/// return a result object (option 3 — over-engineered for v1).
/// </para>
/// </remarks>
public interface IObservationSink
{
    /// <summary>
    /// Record <paramref name="observation"/> to the sink. See the
    /// type-level remarks for the failure-mode contract.
    /// </summary>
    /// <param name="observation">The observation to record. Not null.</param>
    /// <param name="cancellationToken">
    /// Cancellation signal. Implementations honour cancellation on
    /// best-effort semantics — a cancelled task does not guarantee
    /// the observation was or was not persisted.
    /// </param>
    Task RecordAsync(Observation observation, CancellationToken cancellationToken = default);
}
