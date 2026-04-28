using System.Text.RegularExpressions;
using Streamline.Core.Enums;

namespace Streamline.Core.ValueTypes;

/// <summary>
/// The per-column definition inside a <see cref="SchemaDefinition"/>.
/// Describes name, type, structural-validation rules, FK reference,
/// primary-key membership, and — for replication mode — the target
/// column in the destination table.
/// </summary>
/// <remarks>
/// <para>
/// In replication mode the registry entry declares a <see cref="TargetColumn"/>
/// on every column; in transform mode every <see cref="TargetColumn"/> is
/// null. The engine decides the pipeline mode by inspecting whether the
/// entry declares a transform, not by reading this property — but the
/// replication-mode upsert uses the target name here to write.
/// </para>
/// <para>
/// <see cref="MinValue"/> and <see cref="MaxValue"/> are stored as strings
/// and parsed per <see cref="TypeCode"/> by <c>RowValidator</c>. Keeping
/// them as strings avoids a Min/Max property explosion (one each per
/// numeric and temporal type) and matches how registry YAML expresses
/// them.
/// </para>
/// </remarks>
public sealed record class ColumnDefinition
{
    public string Name { get; }
    public ColumnTypeCode TypeCode { get; }
    public bool IsRequired { get; init; }
    public bool IsPrimaryKey { get; init; }
    public int? MaxLength { get; init; }
    public string? MinValue { get; init; }
    public string? MaxValue { get; init; }
    public string? Pattern { get; init; }
    public FkReference? FkReference { get; init; }
    public string? TargetColumn { get; init; }

    public ColumnDefinition(string name, ColumnTypeCode typeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        Name = name;
        TypeCode = typeCode;
    }

    /// <summary>
    /// Validates semantic invariants that depend on combinations of
    /// properties. Called explicitly by <c>RegistryValidator</c>;
    /// individual property setters stay permissive so callers building
    /// up a definition incrementally don't trip partial states.
    /// </summary>
    public IEnumerable<string> DescribeInvariantViolations()
    {
        if (MaxLength is < 0)
        {
            yield return $"{Name}: MaxLength must be non-negative (got {MaxLength}).";
        }

        if (Pattern is not null)
        {
            RegexError? error = TryCompileRegex(Pattern);
            if (error is not null)
            {
                yield return $"{Name}: Pattern '{Pattern}' is not a valid regex ({error.Message}).";
            }
        }

        if (MaxLength.HasValue && TypeCode != ColumnTypeCode.String)
        {
            yield return $"{Name}: MaxLength is only valid for String columns (TypeCode={TypeCode}).";
        }
    }

    private static RegexError? TryCompileRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant);
            return null;
        }
        catch (ArgumentException ex)
        {
            return new RegexError(ex.Message);
        }
    }

    private sealed record class RegexError(string Message);
}
