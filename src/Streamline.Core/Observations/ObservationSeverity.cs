namespace Streamline.Core.Observations;

/// <summary>
/// The four severity levels an <see cref="Observation"/> can carry. See
/// docs/OBSERVATIONS.md "Severity Levels" for the normative definitions.
/// Severity is an engine judgment, not a configuration choice: operators
/// choose what to do about a severity (block, log, alert), not what a
/// condition's severity actually is.
/// </summary>
public enum ObservationSeverity
{
    /// <summary>
    /// Something happened worth recording but no action is needed.
    /// Examples: a file finished staging, a transformer was invoked,
    /// a retry succeeded on first attempt.
    /// </summary>
    Info,

    /// <summary>
    /// Something non-ideal happened but the batch proceeded.
    /// Examples: schema drift with policy <c>Warn</c>, FK cache was
    /// empty under policy <c>WhenParentPopulated</c>, an optional
    /// column was missing from the source file.
    /// </summary>
    Warning,

    /// <summary>
    /// Something failed but was handled. Row quarantined, savepoint
    /// rolled back, transformer returned partial failure. The batch
    /// may still succeed overall if enough rows / tables processed
    /// cleanly.
    /// </summary>
    Error,

    /// <summary>
    /// Something failed that prevents batch completion. File could
    /// not be read, required registry entry missing, deferred
    /// transaction failed at commit, advisory lock held elsewhere.
    /// Operators must intervene before the batch can resume.
    /// </summary>
    Critical,
}
