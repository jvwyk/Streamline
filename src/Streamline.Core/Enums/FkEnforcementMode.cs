namespace Streamline.Core.Enums;

/// <summary>
/// How strictly the engine enforces a foreign-key reference during
/// validation. Configured per FK in the registry. See
/// docs/streamline-plan.md §6 Phase 4.
/// </summary>
public enum FkEnforcementMode
{
    /// <summary>
    /// Every referenced parent value must exist. Missing parent → row
    /// quarantined with <c>FK_VIOLATION</c>.
    /// </summary>
    Always,

    /// <summary>
    /// Enforce only when the parent cache has at least one value.
    /// An empty parent cache skips enforcement (and emits
    /// <c>FK_CACHE_EMPTY</c> as a warning).
    /// </summary>
    WhenParentPopulated,

    /// <summary>
    /// Never enforce. The FK is declared for lineage/documentation but
    /// not checked during validation.
    /// </summary>
    Never,
}
