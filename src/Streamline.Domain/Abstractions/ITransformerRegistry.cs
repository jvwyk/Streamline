using Streamline.Domain.Batches;
using Streamline.Domain.Transforms;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Dispatches transformer invocations based on
/// <see cref="TransformReference.Kind"/>. The Phase 4 implementation
/// composes <c>SqlFunctionTransformer</c> (calls a registered
/// Postgres function) and <c>CSharpTransformer</c> (resolves
/// consumer-registered <c>ITransformer</c> implementations via DI by
/// fully-qualified type name).
/// </summary>
/// <remarks>
/// <para>
/// The registry is the single point at which the engine decides
/// "SQL function or C# class?" — handlers in
/// <c>Streamline.Application</c> call only this method; they do not
/// inspect <see cref="TransformReference.Kind"/> themselves. Adding a
/// new <see cref="Streamline.Core.Enums.TransformKind"/> value (per
/// plan §11 decision 8: "a new kind is only introduced when a real
/// job needs one") goes here, not in the handlers.
/// </para>
/// <para>
/// <b>Failure semantics</b> mirror <see cref="ITransformer"/>: a
/// throw means the engine emits <c>TRANSFORMER_THREW</c>; a non-zero
/// <see cref="TransformOutcome.RowsFailed"/> means
/// <c>TRANSFORMER_PARTIAL_FAILURE</c>; a missing transformer (the
/// reference cannot be resolved) means <c>TRANSFORMER_NOT_FOUND</c>.
/// </para>
/// </remarks>
public interface ITransformerRegistry
{
    Task<TransformOutcome> InvokeAsync(
        TransformReference reference,
        BatchContext context,
        CancellationToken cancellationToken = default);
}
