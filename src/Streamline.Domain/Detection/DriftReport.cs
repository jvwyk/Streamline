using System.Collections.Immutable;

namespace Streamline.Domain.Detection;

/// <summary>
/// Structured summary of how a source file's columns differ from the
/// registry entry's <see cref="Streamline.Core.ValueTypes.SchemaDefinition"/>.
/// Computed by <c>SchemaDriftDetector</c> (Application layer, sub-phase 1g)
/// from the file's headers and the registry entry's columns; consumed
/// by the orchestrator to decide which <c>SCHEMA_DRIFT_*</c>
/// observations to emit at which severity (driven by the entry's
/// drift policies).
/// </summary>
/// <param name="NewColumns">
/// Columns present in the source file but not declared in the
/// registry. Severity is governed by
/// <c>RegistryEntry.NewColumnsDriftPolicy</c>.
/// </param>
/// <param name="MissingRequiredColumns">
/// Columns declared <c>required</c> in the registry but absent from
/// the source file. Always rejected at <c>Critical</c> severity per
/// <c>SCHEMA_DRIFT_MISSING_REQUIRED</c> — included here only so the
/// caller can list them in the observation context.
/// </param>
/// <param name="MissingOptionalColumns">
/// Columns declared in the registry as non-required but absent from
/// the source file. Severity is governed by
/// <c>RegistryEntry.MissingOptionalDriftPolicy</c>.
/// </param>
public sealed record class DriftReport(
    ImmutableArray<string> NewColumns,
    ImmutableArray<string> MissingRequiredColumns,
    ImmutableArray<string> MissingOptionalColumns)
{
    /// <summary>
    /// True when the file's headers exactly match the registry entry's
    /// declared columns — no new, no missing.
    /// </summary>
    public bool IsEmpty =>
        NewColumns.IsDefaultOrEmpty
        && MissingRequiredColumns.IsDefaultOrEmpty
        && MissingOptionalColumns.IsDefaultOrEmpty;

    public bool HasNewColumns => !NewColumns.IsDefaultOrEmpty;
    public bool HasMissingRequiredColumns => !MissingRequiredColumns.IsDefaultOrEmpty;
    public bool HasMissingOptionalColumns => !MissingOptionalColumns.IsDefaultOrEmpty;

    public static DriftReport Empty { get; } =
        new([], [], []);

    public bool Equals(DriftReport? other) =>
        other is not null
        && NewColumns.SequenceEqual(other.NewColumns)
        && MissingRequiredColumns.SequenceEqual(other.MissingRequiredColumns)
        && MissingOptionalColumns.SequenceEqual(other.MissingOptionalColumns);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var c in NewColumns)
        {
            hash.Add(c);
        }
        foreach (var c in MissingRequiredColumns)
        {
            hash.Add(c);
        }
        foreach (var c in MissingOptionalColumns)
        {
            hash.Add(c);
        }
        return hash.ToHashCode();
    }
}
