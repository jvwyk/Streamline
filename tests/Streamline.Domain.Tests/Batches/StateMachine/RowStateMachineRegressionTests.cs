using System.Reflection;
using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches;
using Streamline.Domain.Batches.StateMachine;
using Xunit;

// Avoid the Xunit.Record collision (production type lives in Core.ValueTypes).
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Domain.Tests.Batches.StateMachine;

/// <summary>
/// Regression tests for predecessor-project bugs whose state-machine
/// shape Streamline must prevent from re-emerging. Each test carries a
/// <see cref="PreventsPredecessorBugAttribute"/> naming the bug it
/// guards against. The meta-test
/// <see cref="All_regression_tests_carry_a_predecessor_bug_attribute"/>
/// asserts this discipline so context never gets lost.
///
/// Bugs not covered here are orchestrator-level and surface in 1g
/// (sink-failure semantics, observation threading) or 1j / Phase 4
/// (bounded retry). See docs/PARKED.md for the deferred list.
/// </summary>
public class RowStateMachineRegressionTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    private static StagedRow MakeRow(
        long incomingId = 1,
        RowStatus status = RowStatus.Pending,
        string targetTable = "broker") =>
        new(
            incomingId,
            AnyBatch,
            targetTable,
            new Record("file.csv", incomingId - 1,
                new Dictionary<string, object?> { ["id"] = incomingId }),
            status,
            fileLogId: 1);

    private static Batch AdvanceToProcessing()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();
        batch.MarkIngested();
        batch.BeginProcessing();
        batch.DrainEvents();
        return batch;
    }

    /// <summary>
    /// Predecessor bug SM-1a: the original orchestrator updated
    /// staging.incoming.status='processed' for rows whose upserts had
    /// just been rolled back. The state machine cannot prevent the
    /// orchestrator from lying about persistence outcomes, but it can
    /// close the door on re-marking a rolled-back row as committed.
    /// Orchestrator discipline (1g) is the second line of defense for
    /// the broader pattern.
    /// </summary>
    [Fact]
    [PreventsPredecessorBug("SM-1a",
        "RolledBack rows were marked Committed without going through Pending first.")]
    public void TransitionRow_FromRolledBackToCommitted_Throws()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.RolledBack);

        var act = () => batch.TransitionRow(row, RowStatus.Committed);

        act.Should().Throw<IllegalStateTransitionException>();
    }

    /// <summary>
    /// Predecessor bug SM-1b (belt-and-braces companion to SM-1a): the
    /// aggregate's event accumulator must be transactional with
    /// respect to a failing transition. A throwing TransitionRow call
    /// must not leave events in the drain queue.
    /// </summary>
    [Fact]
    [PreventsPredecessorBug("SM-1b",
        "Aggregate event buffer leaked partial events when a transition threw mid-method.")]
    public void Aggregate_DoesNotEmitEvents_WhenTransitionFails()
    {
        var batch = AdvanceToProcessing();
        var rolledBackRow = MakeRow(status: RowStatus.RolledBack);

        var act = () => batch.TransitionRow(rolledBackRow, RowStatus.Committed);
        act.Should().Throw<IllegalStateTransitionException>();

        batch.DrainEvents().Should().BeEmpty(
            "a transition that throws must not leave events in the buffer");
    }

    public static IEnumerable<object[]> IllegalQuarantineEscapes() =>
    [
        [RowStatus.Committed],
        [RowStatus.Processing],
        [RowStatus.RolledBack],
    ];

    /// <summary>
    /// Predecessor bug SM-2: a row quarantined during validation got
    /// "uncoarantined" by a later rollback or upsert path because the
    /// engine treated quarantine as transient. T8
    /// (Quarantined → Pending via manual resolution) is the only
    /// legal escape. Every other target from Quarantined throws.
    /// </summary>
    [Theory]
    [MemberData(nameof(IllegalQuarantineEscapes))]
    [PreventsPredecessorBug("SM-2",
        "Quarantined rows escaped to Committed/Processing/RolledBack via non-T8 paths.")]
    public void TransitionRow_FromQuarantinedToIllegalTarget_Throws(RowStatus illegalTarget)
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Quarantined);

        var act = () => batch.TransitionRow(row, illegalTarget);

        act.Should().Throw<IllegalStateTransitionException>();
    }

    /// <summary>
    /// Predecessor bug SM-3: the orchestrator passed a stale row
    /// snapshot whose Status reflected an earlier point in time. The
    /// aggregate uses <see cref="StagedRow.Status"/> as the
    /// authoritative <c>from</c> state — there is no separate
    /// <c>from</c> parameter that could disagree with the row. This
    /// test pins that property: a snapshot whose Status equals the
    /// requested target throws because the matrix forbids
    /// self-transitions, which is exactly the surface a stale-
    /// snapshot re-affirmation would hit.
    ///
    /// Detection of "snapshot disagrees with persisted reality" is
    /// outside the aggregate's reach (the aggregate has no
    /// repository) — it lives in
    /// <c>IStagingRepository.TransitionAsync</c>, which throws
    /// InvalidOperationException when the persisted status doesn't
    /// match <c>fromStatus</c>. Together the two layers prevent the
    /// SM-3 pattern.
    /// </summary>
    [Fact]
    [PreventsPredecessorBug("SM-3",
        "Stale StagedRow.Status used as the from-state without verification against persisted reality.")]
    public void TransitionRow_StaleSnapshotRequestingSameStateAsTarget_Throws()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Pending);

        // A stale orchestrator snapshot might end up asking the
        // aggregate to "confirm" a state — Pending → Pending. The
        // matrix forbids self-transitions, so this throws.
        var act = () => batch.TransitionRow(row, RowStatus.Pending);

        act.Should().Throw<IllegalStateTransitionException>();
    }

    /// <summary>
    /// Predecessor bug SM-4: two <c>--process</c> invocations both
    /// claimed the same Pending row and both attempted Pending →
    /// Processing. SELECT ... FOR UPDATE SKIP LOCKED at the
    /// repository level is the primary defense in Phase 2; this test
    /// is the second line of defense at the aggregate. A row that
    /// already records Status=Processing must throw on a request to
    /// transition INTO Processing (a self-transition, which the
    /// matrix forbids).
    /// </summary>
    [Fact]
    [PreventsPredecessorBug("SM-4",
        "Two workers race-claimed a Pending row and both succeeded at Pending → Processing.")]
    public void TransitionRow_ToProcessingWhenAlreadyProcessing_Throws()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Processing);

        var act = () => batch.TransitionRow(row, RowStatus.Processing);

        act.Should().Throw<IllegalStateTransitionException>();
    }

    /// <summary>
    /// Predecessor bug SM-5: a row quarantined during validation got
    /// quarantined a second time during processing because the
    /// orchestrator didn't notice it was already Quarantined. The
    /// matrix forbids self-transitions; this test pins the specific
    /// path the predecessor bug hit.
    /// </summary>
    [Fact]
    [PreventsPredecessorBug("SM-5",
        "Quarantined → Quarantined produced duplicate quarantine records for the same row.")]
    public void TransitionRow_QuarantinedToQuarantined_Throws()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Quarantined);

        var act = () => batch.TransitionRow(row, RowStatus.Quarantined);

        act.Should().Throw<IllegalStateTransitionException>();
    }

    // ---- meta-test ------------------------------------------------------

    /// <summary>
    /// Self-policing discipline: every <c>[Fact]</c> and
    /// <c>[Theory]</c> method in this class must carry a
    /// <see cref="PreventsPredecessorBugAttribute"/> naming the bug it
    /// guards against. Stops regression tests from accumulating
    /// without context over time — a future contributor adding a
    /// test here without the attribute fails this meta-test
    /// immediately.
    /// </summary>
    [Fact]
    public void All_regression_tests_carry_a_predecessor_bug_attribute()
    {
        var regressionTests = typeof(RowStateMachineRegressionTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m =>
                m.GetCustomAttributes<FactAttribute>(true).Any() ||
                m.GetCustomAttributes<TheoryAttribute>(true).Any())
            .Where(m => m.Name != nameof(All_regression_tests_carry_a_predecessor_bug_attribute))
            .ToArray();

        regressionTests.Should().NotBeEmpty(
            "the class must contain at least one regression test (sanity check)");

        var missing = regressionTests
            .Where(m => m.GetCustomAttribute<PreventsPredecessorBugAttribute>() is null)
            .Select(m => m.Name)
            .ToArray();

        missing.Should().BeEmpty(
            "every regression test must carry a [PreventsPredecessorBug] attribute " +
            "naming the bug it prevents — see docs/AGENTS.md for the discipline");
    }
}
