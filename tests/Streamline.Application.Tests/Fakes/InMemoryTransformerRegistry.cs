using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Transforms;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="ITransformerRegistry"/>. Maps a
/// <see cref="TransformReference"/> to a registered
/// <see cref="ITransformer"/> (typically a
/// <see cref="ScriptedTransformer"/>) by reference identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resolution.</b>
/// <see cref="TransformReference.Reference"/> is the lookup key:
/// for SqlFunction transforms it's the function name, for CSharp
/// transforms it's the assembly-qualified type name. The fake
/// matches on this string regardless of <see cref="TransformReference.Kind"/> —
/// tests are responsible for using consistent references when
/// registering and invoking.
/// </para>
/// <para>
/// <b>Failure semantics.</b> If a reference can't be resolved,
/// <see cref="InvokeAsync"/> throws
/// <see cref="InvalidOperationException"/>. Production
/// implementations would emit
/// <c>TRANSFORMER_NOT_FOUND</c> at <c>Critical</c> per the
/// <see cref="ITransformerRegistry"/> remarks; the fake throws
/// because the orchestrator path that would emit the observation
/// isn't yet wired in v1 (transform-mode is deferred per
/// <c>TRANSFORM_MODE_DEFERRED</c>). When Phase 4 wires it, the
/// fake's behaviour can be revisited then.
/// </para>
/// </remarks>
public sealed class InMemoryTransformerRegistry : ITransformerRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ITransformer> _transformers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Register a transformer for the given reference string. Calling
    /// twice with the same reference replaces the previous binding.
    /// </summary>
    public void Register(string reference, ITransformer transformer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(transformer);

        lock (_lock)
        {
            _transformers[reference] = transformer;
        }
    }

    public async Task<TransformOutcome> InvokeAsync(
        TransformReference reference,
        BatchContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(context);

        ITransformer transformer;
        lock (_lock)
        {
            if (!_transformers.TryGetValue(reference.Reference, out var resolved))
            {
                throw new InvalidOperationException(
                    $"No transformer registered for reference '{reference.Reference}' (kind {reference.Kind}).");
            }
            transformer = resolved;
        }

        return await transformer.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
