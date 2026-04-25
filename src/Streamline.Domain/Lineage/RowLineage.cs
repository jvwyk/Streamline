using System.Collections.Immutable;

namespace Streamline.Domain.Lineage;

/// <summary>
/// The link between a staging <c>incoming_id</c> and the destination
/// row(s) it produced. Engine-driven upserts (replication mode)
/// populate lineage automatically; transformers populate lineage by
/// convention via <see cref="Streamline.Domain.Abstractions.ILineageWriter"/>.
/// Stored in <c>staging.row_lineage</c>. See plan §5.3 "RowLineage".
/// </summary>
/// <param name="IncomingId">
/// The staging row this lineage entry traces back to. Must be > 0
/// (staging IDs are auto-generated, so 0 means "not yet inserted").
/// </param>
/// <param name="DestinationSchema">
/// The schema of the destination table the row was written to (e.g.
/// <c>"intembeko"</c>).
/// </param>
/// <param name="DestinationTable">
/// The destination table name (e.g. <c>"dim_broker"</c>). Pair with
/// <see cref="DestinationSchema"/> for a fully-qualified reference.
/// </param>
/// <param name="DestinationPkValues">
/// The primary-key column values of the destination row, in the order
/// the destination table declares its PK. Stored as
/// <see cref="ImmutableArray{T}"/> to capture composite keys without
/// flattening to strings.
/// </param>
public sealed record class RowLineage(
    long IncomingId,
    string DestinationSchema,
    string DestinationTable,
    ImmutableArray<object> DestinationPkValues)
{
    public long IncomingId { get; } = RequirePositive(IncomingId, nameof(IncomingId));
    public string DestinationSchema { get; } = RequireNonBlank(DestinationSchema, nameof(DestinationSchema));
    public string DestinationTable { get; } = RequireNonBlank(DestinationTable, nameof(DestinationTable));
    public ImmutableArray<object> DestinationPkValues { get; } = RequireNonEmpty(DestinationPkValues, nameof(DestinationPkValues));

    public bool Equals(RowLineage? other) =>
        other is not null
        && IncomingId == other.IncomingId
        && DestinationSchema == other.DestinationSchema
        && DestinationTable == other.DestinationTable
        && DestinationPkValues.SequenceEqual(other.DestinationPkValues);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(IncomingId);
        hash.Add(DestinationSchema);
        hash.Add(DestinationTable);
        foreach (var pk in DestinationPkValues)
        {
            hash.Add(pk);
        }
        return hash.ToHashCode();
    }

    private static long RequirePositive(long value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
        return value;
    }

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    private static ImmutableArray<object> RequireNonEmpty(ImmutableArray<object> values, string name)
    {
        if (values.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "DestinationPkValues must contain at least one value (rows must be addressable).", name);
        }
        return values;
    }
}
