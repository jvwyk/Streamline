namespace Streamline.Domain.Batches.StateMachine;

/// <summary>
/// Thrown when a caller attempts an illegal state transition. Inherits
/// <see cref="InvalidOperationException"/> rather than
/// <see cref="ArgumentException"/> on purpose: the offending parameters
/// are arguments, but the semantic is "the system is in a state where
/// this operation isn't valid", not "the caller passed bad data". The
/// state-machine review process is the canonical source for what counts
/// as legal.
/// </summary>
public sealed class IllegalStateTransitionException : InvalidOperationException
{
    /// <summary>
    /// Subject of the transition — typically <c>"row"</c> or
    /// <c>"batch"</c>. Lets a single exception type cover both
    /// machines without a generic parameter.
    /// </summary>
    public string Subject { get; }

    /// <summary>The state the transition was attempted from.</summary>
    public string From { get; }

    /// <summary>The state the transition was attempted to.</summary>
    public string To { get; }

    /// <summary>Optional context, e.g. the row id or batch id involved.</summary>
    public string? Reason { get; }

    public IllegalStateTransitionException(string subject, string from, string to, string? reason = null)
        : base(BuildMessage(subject, from, to, reason))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));
        ArgumentException.ThrowIfNullOrWhiteSpace(from, nameof(from));
        ArgumentException.ThrowIfNullOrWhiteSpace(to, nameof(to));

        Subject = subject;
        From = from;
        To = to;
        Reason = reason;
    }

    private static string BuildMessage(string subject, string from, string to, string? reason)
    {
        var core = $"Illegal {subject} state transition: {from} -> {to}.";
        return reason is null ? core : $"{core} {reason}";
    }
}
