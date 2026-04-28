using Streamline.Domain.Batches;

namespace Streamline.Domain.Transforms;

/// <summary>
/// The contract consumers implement for C# transformers. Resolved at
/// runtime by <c>ITransformerRegistry</c> (sub-phase 1c commit 6) via
/// the assembly-qualified type name in
/// <see cref="TransformReference.Reference"/>.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Streamline.Domain.Transforms</c> rather than
/// <c>Streamline.Domain.Abstractions</c> on purpose:
/// <see cref="ITransformer"/> is a contract <em>consumers</em>
/// implement (the engine calls it). Abstractions/ is reserved for
/// contracts the engine implements (the consumer calls).
/// </para>
/// <para>
/// <b>Lineage responsibility.</b> Implementations must emit a
/// <c>RowLineage</c> entry via <c>BatchContext.Lineage</c> for every
/// destination row they write. Engine-driven replication-mode upserts
/// populate lineage automatically; transformer writes do not. A
/// missing lineage entry is a transformer bug, not an engine bug —
/// it produces inconsistent <c>--lineage</c> query results in
/// production.
/// </para>
/// <para>
/// <b>Failure semantics.</b> Throwing from <see cref="ExecuteAsync"/>
/// fails the batch (the engine treats the throw as a critical
/// failure, transitions affected rows to <c>RolledBack</c>, and emits
/// <c>TRANSFORMER_THREW</c>). Returning a <see cref="TransformOutcome"/>
/// with non-zero <c>RowsFailed</c> is partial success — the engine
/// records <c>TRANSFORMER_PARTIAL_FAILURE</c> but does not
/// automatically fail the batch (that's the consumer's policy
/// decision, expressed via the outcome).
/// </para>
/// </remarks>
public interface ITransformer
{
    Task<TransformOutcome> ExecuteAsync(BatchContext context, CancellationToken cancellationToken = default);
}
