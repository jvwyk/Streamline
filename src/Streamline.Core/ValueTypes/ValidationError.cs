namespace Streamline.Core.ValueTypes;

/// <summary>
/// One failure produced by <c>RowValidator</c> for a single column on a
/// single row. Carries enough context for the caller to emit a
/// corresponding observation (<see cref="Code"/> is a stable
/// observation code from <c>ObservationCodes</c>, landing in sub-phase
/// 1b) and for the quarantine record to explain what went wrong.
/// </summary>
/// <param name="Column">
/// The column the error applies to. Empty string for row-level
/// errors that don't belong to any specific column (e.g., PK_VIOLATION).
/// </param>
/// <param name="AttemptedValue">
/// The value the validator tried to accept. Typically the raw string
/// from the file reader; may be null if the column was missing.
/// </param>
/// <param name="Code">
/// Stable observation code, e.g. <c>MISSING_REQUIRED</c>,
/// <c>INVALID_TYPE</c>, <c>FK_VIOLATION</c>. Must match a value in
/// <c>Streamline.Core.Observations.ObservationCodes</c> (enforced by
/// <c>ObservationCodes</c> tests once that type lands in 1b).
/// </param>
/// <param name="Message">
/// Human-readable description. Not a replacement for <paramref name="Code"/>;
/// the code is the stable identifier, the message is the operator-
/// facing explanation.
/// </param>
public sealed record class ValidationError(
    string Column,
    object? AttemptedValue,
    string Code,
    string Message)
{
    public string Column { get; } = Column ?? throw new ArgumentNullException(nameof(Column));
    public string Code { get; } = RequireNonBlank(Code, nameof(Code));
    public string Message { get; } = RequireNonBlank(Message, nameof(Message));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
