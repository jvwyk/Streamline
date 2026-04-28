namespace Streamline.Core.Enums;

/// <summary>
/// How the engine reacts when a source file's schema differs from the
/// registry. Configured per registry entry, applied independently to
/// each drift dimension (new columns, missing required, missing
/// optional). See docs/OBSERVATIONS.md "Schema Drift".
/// </summary>
public enum DriftPolicy
{
    /// <summary>
    /// Reject the file during ingestion before any row is staged.
    /// Emits <c>SCHEMA_DRIFT_BLOCKED</c> at <c>Critical</c>.
    /// </summary>
    Block,

    /// <summary>
    /// Stage the file; emit a <c>Warning</c> observation
    /// (<c>SCHEMA_DRIFT_NEW_COLUMN</c>, <c>SCHEMA_DRIFT_MISSING_OPTIONAL</c>).
    /// The batch proceeds.
    /// </summary>
    Warn,

    /// <summary>
    /// Stage the file; emit an <c>Info</c> observation recording the
    /// drift but raise no warning.
    /// </summary>
    Ignore,
}
