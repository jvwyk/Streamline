using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches.StateMachine;
using Xunit;

namespace Streamline.Domain.Tests.Batches.StateMachine;

/// <summary>
/// Exhaustive coverage of the row state matrix. Two theories cover
/// every cell of the 5×5 grid; meta-tests assert structural properties
/// (terminal states, reachability, no self-transitions) so accidental
/// matrix edits surface immediately.
/// </summary>
public class RowStateMachineTests
{
    /// <summary>
    /// Independent mirror of the legal set, maintained here so the
    /// tests don't validate the source against itself. Drift between
    /// this list and <see cref="RowStateMachine.IsLegal"/> fails the
    /// matrix theories immediately, which is the tripwire's whole
    /// point. Order mirrors plan §5.3 T2–T8.
    /// </summary>
    private static readonly HashSet<(RowStatus From, RowStatus To)> ExpectedLegal =
    [
        (RowStatus.Pending,     RowStatus.Processing),     // T2
        (RowStatus.Pending,     RowStatus.Quarantined),    // T5
        (RowStatus.Processing,  RowStatus.Committed),      // T3
        (RowStatus.Processing,  RowStatus.RolledBack),     // T4
        (RowStatus.Processing,  RowStatus.Quarantined),    // T6
        (RowStatus.RolledBack,  RowStatus.Pending),        // T7
        (RowStatus.Quarantined, RowStatus.Pending),        // T8
    ];

    public static IEnumerable<object[]> AllPairs() =>
        from f in Enum.GetValues<RowStatus>()
        from t in Enum.GetValues<RowStatus>()
        select new object[] { f, t };

    public static IEnumerable<object[]> LegalPairs() =>
        ExpectedLegal.Select(p => new object[] { p.From, p.To });

    public static IEnumerable<object[]> IllegalPairs() =>
        AllPairs()
            .Select(p => (From: (RowStatus)p[0], To: (RowStatus)p[1]))
            .Where(p => !ExpectedLegal.Contains((p.From, p.To)))
            .Select(p => new object[] { p.From, p.To });

    [Theory, MemberData(nameof(AllPairs))]
    public void IsLegal_MatchesIndependentCanonicalSet(RowStatus from, RowStatus to)
    {
        var expected = ExpectedLegal.Contains((from, to));
        RowStateMachine.IsLegal(from, to).Should().Be(expected);
    }

    [Theory, MemberData(nameof(LegalPairs))]
    public void RequireLegal_LegalCells_DoNotThrow(RowStatus from, RowStatus to)
    {
        var act = () => RowStateMachine.RequireLegal(from, to);
        act.Should().NotThrow();
    }

    [Theory, MemberData(nameof(IllegalPairs))]
    public void RequireLegal_IllegalCells_ThrowIllegalStateTransition(RowStatus from, RowStatus to)
    {
        var act = () => RowStateMachine.RequireLegal(from, to);

        act.Should().Throw<IllegalStateTransitionException>()
            .Which.Should().Match<IllegalStateTransitionException>(ex =>
                ex.Subject == "row"
                && ex.From == from.ToString()
                && ex.To == to.ToString());
    }

    [Fact]
    public void RequireLegal_IllegalCellWithReason_IncludesReasonInMessage()
    {
        var act = () => RowStateMachine.RequireLegal(
            RowStatus.Committed, RowStatus.Pending, reason: "incoming_id=42");

        act.Should().Throw<IllegalStateTransitionException>()
            .WithMessage("*incoming_id=42*");
    }

    [Fact]
    public void IllegalStateTransitionException_InheritsInvalidOperationException()
    {
        // Semantic check: callers up the stack catch InvalidOperationException
        // for "system is in the wrong state" — not ArgumentException,
        // even though the offending parameters are arguments.
        var ex = new IllegalStateTransitionException("row", "Committed", "Pending");
        ex.Should().BeAssignableTo<InvalidOperationException>();
        ex.Should().NotBeAssignableTo<ArgumentException>();
    }

    // ----- meta-tests on the matrix ---------------------------------------

    [Fact]
    public void LegalTransitionCount_IsExactlySeven()
    {
        // Tripwire: changes to the legal set surface here. T1
        // (row creation) is not in the matrix because it has no
        // `from` state — staging insert handles it. T2–T8 = 7.
        RowStateMachine.LegalTransitionCount.Should().Be(7);
        ExpectedLegal.Should().HaveCount(7);
    }

    [Fact]
    public void Self_transitions_are_all_illegal()
    {
        foreach (var status in Enum.GetValues<RowStatus>())
        {
            RowStateMachine.IsLegal(status, status).Should().BeFalse(
                $"self-transition {status} -> {status} must be illegal (no-ops are bugs)");
        }
    }

    [Fact]
    public void Committed_is_terminal()
    {
        foreach (var to in Enum.GetValues<RowStatus>())
        {
            RowStateMachine.IsLegal(RowStatus.Committed, to).Should().BeFalse(
                $"Committed is terminal; {RowStatus.Committed} -> {to} must be illegal");
        }
    }

    [Fact]
    public void Every_non_terminal_state_has_at_least_one_outbound_legal_transition()
    {
        // If a future contributor accidentally makes a non-terminal
        // state have zero outbound transitions, this catches it.
        var nonTerminal = new[]
        {
            RowStatus.Pending,
            RowStatus.Processing,
            RowStatus.RolledBack,
            RowStatus.Quarantined,
        };

        foreach (var from in nonTerminal)
        {
            var outboundCount = Enum.GetValues<RowStatus>()
                .Count(to => RowStateMachine.IsLegal(from, to));

            outboundCount.Should().BeGreaterThanOrEqualTo(1,
                $"{from} is non-terminal and must have ≥1 outbound legal transition");
        }
    }

    [Fact]
    public void Every_state_is_reachable_from_some_other_state_or_is_initial()
    {
        // Pending is the initial state — staging insert (T1) creates
        // rows in Pending directly, with no `from`. Every other state
        // must be reachable via at least one (from, to) pair.
        var allStates = Enum.GetValues<RowStatus>();

        var unreachable = allStates
            .Where(target => target != RowStatus.Pending)
            .Where(target => !allStates.Any(source =>
                source != target && RowStateMachine.IsLegal(source, target)))
            .ToArray();

        unreachable.Should().BeEmpty(
            "every state except the initial state Pending must be reachable from some other state");
    }
}
