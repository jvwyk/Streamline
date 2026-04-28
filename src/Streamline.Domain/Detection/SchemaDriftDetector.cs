using System.Collections.Immutable;
using Streamline.Core.ValueTypes;

namespace Streamline.Domain.Detection;

/// <summary>
/// Pure-function diff between a source file's headers and a
/// registry entry's <see cref="SchemaDefinition"/>. Produces a
/// <see cref="DriftReport"/> classifying every column as new
/// (in file, not in registry), missing-required (required by
/// registry, absent from file), or missing-optional (optional in
/// registry, absent from file).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure detection, no policy.</b> The detector reports what
/// differs; the Application-layer
/// <c>SchemaDriftPolicyApplier</c> consults the registry's
/// <c>DriftPolicy</c> fields and decides block / warn / ignore.
/// Splitting detection from policy keeps Domain pure and makes
/// the diff trivially testable.
/// </para>
/// <para>
/// <b>Case sensitivity.</b> Matching is ordinal case-sensitive by
/// default — Postgres column names and most file readers preserve
/// case. If a Phase 3 reader-config flag wants
/// case-insensitive matching for a particular file format, the
/// detector takes a comparer parameter (defaulting to
/// <see cref="StringComparer.Ordinal"/>) so callers can opt in.
/// </para>
/// </remarks>
public static class SchemaDriftDetector
{
    /// <summary>
    /// Compute the drift between <paramref name="fileHeaders"/>
    /// (the column names the reader emitted for the source file)
    /// and <paramref name="schema"/> (the registry-declared
    /// columns).
    /// </summary>
    public static DriftReport Detect(
        IReadOnlyList<string> fileHeaders,
        SchemaDefinition schema,
        StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(fileHeaders);
        ArgumentNullException.ThrowIfNull(schema);

        comparer ??= StringComparer.Ordinal;

        var fileSet = new HashSet<string>(fileHeaders, comparer);
        var registrySet = new HashSet<string>(
            schema.Columns.Select(c => c.Name), comparer);

        var newColumns = fileHeaders
            .Where(h => !registrySet.Contains(h))
            .Distinct(comparer)
            .ToImmutableArray();

        var missingRequired = schema.Columns
            .Where(c => c.IsRequired && !fileSet.Contains(c.Name))
            .Select(c => c.Name)
            .ToImmutableArray();

        var missingOptional = schema.Columns
            .Where(c => !c.IsRequired && !fileSet.Contains(c.Name))
            .Select(c => c.Name)
            .ToImmutableArray();

        return new DriftReport(newColumns, missingRequired, missingOptional);
    }
}
