using System.Collections.Immutable;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Transforms;

namespace Streamline.Domain.Registry;

/// <summary>
/// One entry in the registry. Describes a logical table's source
/// schema, its target (replication mode) or transformer (transform
/// mode), and the policies governing how the engine reacts to drift
/// and FK references during ingestion. See plan §5.3 and Appendices
/// A / A' / A''.
/// </summary>
/// <remarks>
/// <para>
/// <b>Replication vs transform mode.</b> Detected by
/// <see cref="Transform"/>: null means replication (the engine writes
/// to <see cref="TargetSchema"/>); non-null means transform (the
/// referenced transformer owns the write). In replication mode
/// <see cref="TargetSchema"/> must be set; in transform mode it MAY
/// be set (the engine records it for lineage even though the
/// transformer is responsible for the actual write).
/// </para>
/// <para>
/// <b>Primary key.</b> Sourced from
/// <see cref="SchemaDefinition.PrimaryKey"/> (the columns where
/// <see cref="ColumnDefinition.IsPrimaryKey"/> is true). The entry
/// does not duplicate this list; a duplicate field would drift from
/// the column-level flag and silently disagree.
/// </para>
/// <para>
/// <b>Drift policies.</b> Three independent <see cref="DriftPolicy"/>
/// fields rather than a bundled type. The triple has not yet shown
/// up as a unit elsewhere; YAGNI on the bundle.
/// </para>
/// <para>
/// <b>Invariants.</b> The constructor only checks structural
/// requirements (non-blank table name, non-null schema, valid date
/// range). Cross-field invariants (replication mode requires
/// <see cref="TargetSchema"/>, etc.) are surfaced lazily via
/// <see cref="DescribeInvariantViolations"/> — the same pattern as
/// <see cref="ColumnDefinition"/>. <c>RegistryValidator</c>
/// (sub-phase 1e) collects every violation across every entry before
/// reporting, so a YAML editor sees all their mistakes in one
/// round-trip.
/// </para>
/// </remarks>
public sealed record class RegistryEntry
{
    public string TableName { get; }
    public SchemaDefinition Schema { get; }
    public string? TargetSchema { get; init; }
    public TransformReference? Transform { get; init; }
    public DriftPolicy NewColumnsDriftPolicy { get; init; } = DriftPolicy.Warn;
    public DriftPolicy MissingRequiredDriftPolicy { get; init; } = DriftPolicy.Block;
    public DriftPolicy MissingOptionalDriftPolicy { get; init; } = DriftPolicy.Warn;
    public ImmutableArray<string> DependsOn { get; init; } = [];
    public DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }
    public bool IsActive { get; init; } = true;

    public RegistryEntry(string tableName, SchemaDefinition schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName, nameof(tableName));
        ArgumentNullException.ThrowIfNull(schema);
        TableName = tableName;
        Schema = schema;
    }

    /// <summary>
    /// True when the entry routes through replication (no transform).
    /// </summary>
    public bool IsReplication => Transform is null;

    /// <summary>
    /// True when the entry invokes a transformer.
    /// </summary>
    public bool IsTransform => Transform is not null;

    /// <summary>
    /// Returns every cross-field invariant violation. Empty when the
    /// entry is fully consistent. <c>RegistryValidator</c> calls this
    /// across all entries in a registry and reports the union, so
    /// editors see every problem at once.
    /// </summary>
    public IEnumerable<string> DescribeInvariantViolations()
    {
        if (IsReplication && string.IsNullOrWhiteSpace(TargetSchema))
        {
            yield return
                $"{TableName}: replication-mode entries (no transform) must declare a TargetSchema.";
        }

        if (ValidTo.HasValue && ValidTo.Value < ValidFrom)
        {
            yield return
                $"{TableName}: ValidTo ({ValidTo}) must be on or after ValidFrom ({ValidFrom}).";
        }

        foreach (var dep in DependsOn)
        {
            if (string.IsNullOrWhiteSpace(dep))
            {
                yield return $"{TableName}: DependsOn contains a blank entry.";
            }
            else if (string.Equals(dep, TableName, StringComparison.Ordinal))
            {
                yield return $"{TableName}: DependsOn references itself.";
            }
        }
    }

    public bool Equals(RegistryEntry? other) =>
        other is not null
        && TableName == other.TableName
        && Equals(Schema, other.Schema)
        && TargetSchema == other.TargetSchema
        && Equals(Transform, other.Transform)
        && NewColumnsDriftPolicy == other.NewColumnsDriftPolicy
        && MissingRequiredDriftPolicy == other.MissingRequiredDriftPolicy
        && MissingOptionalDriftPolicy == other.MissingOptionalDriftPolicy
        && DependsOn.SequenceEqual(other.DependsOn)
        && ValidFrom == other.ValidFrom
        && ValidTo == other.ValidTo
        && IsActive == other.IsActive;

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(TableName);
        hash.Add(Schema);
        hash.Add(TargetSchema);
        hash.Add(Transform);
        hash.Add(NewColumnsDriftPolicy);
        hash.Add(MissingRequiredDriftPolicy);
        hash.Add(MissingOptionalDriftPolicy);
        foreach (var dep in DependsOn)
        {
            hash.Add(dep);
        }
        hash.Add(ValidFrom);
        hash.Add(ValidTo);
        hash.Add(IsActive);
        return hash.ToHashCode();
    }
}
