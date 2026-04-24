using Streamline.Core.Enums;

namespace Streamline.Core.ValueTypes;

/// <summary>
/// A registry-declared foreign-key reference from a column in this
/// table to a column in a parent table. See docs/streamline-plan.md
/// §5.3 and §6 Phase 4.
/// </summary>
/// <param name="ParentTable">
/// The target table the reference points to (logical table name as
/// registered, not a schema-qualified SQL name).
/// </param>
/// <param name="ParentColumn">
/// The column in the parent table this FK matches against. Typically
/// the parent's primary key.
/// </param>
/// <param name="EnforcementMode">
/// How strictly the engine enforces this reference during validation.
/// Applied per FK, not per registry entry.
/// </param>
public sealed record class FkReference(
    string ParentTable,
    string ParentColumn,
    FkEnforcementMode EnforcementMode)
{
    public string ParentTable { get; } = RequireNonBlank(ParentTable, nameof(ParentTable));
    public string ParentColumn { get; } = RequireNonBlank(ParentColumn, nameof(ParentColumn));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
