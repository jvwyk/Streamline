using Streamline.Core.Enums;

namespace Streamline.Domain.Batches.StateMachine;

/// <summary>
/// Pure-logic gate over <see cref="RowStatus"/> transitions. Mirrors
/// the row state model defined in docs/streamline-plan.md §5.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>The legal set has seven entries</b> on the 5×5 status matrix
/// (18 illegal). Plan §5.3 lists T1–T8 but T1 is <c>(none) → Pending</c>
/// — row creation, not a transition between two existing states. T1
/// is the responsibility of <c>IStagingRepository.BulkInsertIncomingAsync</c>;
/// it does not appear in this matrix. T2–T8 are the seven entries
/// here.
/// </para>
/// <para>
/// <b>No I/O, no observations, no side effects.</b> The state machine
/// is the single source of truth for which <c>(from, to)</c> pairs are
/// valid. The <c>Batch</c> aggregate (commit 3 of sub-phase 1d) calls
/// <see cref="RequireLegal"/> before emitting domain events;
/// <c>IStagingRepository.TransitionAsync</c> implementations call
/// it before persisting.
/// </para>
/// </remarks>
public static class RowStateMachine
{
    private static readonly HashSet<(RowStatus From, RowStatus To)> LegalTransitions =
    [
        (RowStatus.Pending,     RowStatus.Processing),     // T2
        (RowStatus.Pending,     RowStatus.Quarantined),    // T5
        (RowStatus.Processing,  RowStatus.Committed),      // T3
        (RowStatus.Processing,  RowStatus.RolledBack),     // T4
        (RowStatus.Processing,  RowStatus.Quarantined),    // T6
        (RowStatus.RolledBack,  RowStatus.Pending),        // T7
        (RowStatus.Quarantined, RowStatus.Pending),        // T8
    ];

    /// <summary>
    /// Count of legal <c>(from, to)</c> transitions in the matrix.
    /// Tripwire for accidental edits to the legal set.
    /// </summary>
    public static int LegalTransitionCount => LegalTransitions.Count;

    /// <summary>
    /// True when the transition <paramref name="from"/> →
    /// <paramref name="to"/> is permitted by the state model.
    /// </summary>
    public static bool IsLegal(RowStatus from, RowStatus to) =>
        LegalTransitions.Contains((from, to));

    /// <summary>
    /// Asserts the transition is legal; throws
    /// <see cref="IllegalStateTransitionException"/> when it is not.
    /// The <c>reason</c> argument (optional) is included in the
    /// exception message as additional context, e.g. a row id.
    /// </summary>
    public static void RequireLegal(RowStatus from, RowStatus to, string? reason = null)
    {
        if (!IsLegal(from, to))
        {
            throw new IllegalStateTransitionException(
                "row", from.ToString(), to.ToString(), reason);
        }
    }
}
