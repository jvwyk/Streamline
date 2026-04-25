using Streamline.Core.Enums;

namespace Streamline.Domain.Transforms;

/// <summary>
/// A registry-declared pointer to a transformer. Resolved at runtime
/// by <c>ITransformerRegistry</c>: <see cref="Kind"/> selects the
/// dispatcher, <see cref="Reference"/> identifies the target within
/// that dispatcher, <see cref="DestinationTable"/> records where the
/// transformer writes, <see cref="Invocation"/> says when it runs.
/// See plan §15 Appendix C.
/// </summary>
/// <param name="Kind">Which transformer family handles this reference.</param>
/// <param name="Reference">
/// For <see cref="TransformKind.SqlFunction"/>: the function name, optionally
/// schema-qualified (e.g. <c>"domain.transform_broker_v3"</c>).
/// For <see cref="TransformKind.CSharp"/>: the assembly-qualified C# type
/// name (e.g. <c>"YourDomain.Transforms.BrokerTransformer, YourDomain.Transforms"</c>).
/// </param>
/// <param name="DestinationTable">
/// The schema-qualified destination table the transformer writes to
/// (e.g. <c>"intembeko.dim_broker"</c>). Recorded for lineage and
/// observation context; the engine does not enforce that the
/// transformer actually writes only to this table.
/// </param>
/// <param name="Invocation">
/// When the engine invokes the transformer. v1 supports
/// <see cref="TransformInvocation.PerBatch"/> only.
/// </param>
public sealed record class TransformReference(
    TransformKind Kind,
    string Reference,
    string DestinationTable,
    TransformInvocation Invocation)
{
    public string Reference { get; } = RequireNonBlank(Reference, nameof(Reference));
    public string DestinationTable { get; } = RequireNonBlank(DestinationTable, nameof(DestinationTable));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
