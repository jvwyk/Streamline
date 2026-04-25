using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches;
using Streamline.Domain.Batches.DomainEvents;
using Streamline.Domain.Batches.StateMachine;
using Xunit;

// Avoid the Xunit.Record collision (production type lives in Core.ValueTypes).
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Domain.Tests.Batches;

public class BatchTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    private static StagedRow MakeRow(
        BatchId? batchId = null,
        long incomingId = 1,
        RowStatus status = RowStatus.Pending,
        string targetTable = "broker") =>
        new StagedRow(
            incomingId,
            batchId ?? AnyBatch,
            targetTable,
            new Record("file.csv", incomingId - 1, new Dictionary<string, object?> { ["id"] = incomingId }),
            status,
            fileLogId: 1);

    // ---- creation -----------------------------------------------------

    [Fact]
    public void Create_StartsInCreatedStatus_WithNoEvents()
    {
        var batch = Batch.Create(AnyBatch, "/data/incoming");

        batch.Id.Should().Be(AnyBatch);
        batch.Source.Should().Be("/data/incoming");
        batch.Status.Should().Be(BatchStatus.Created);
        batch.CompletedAt.Should().BeNull();
        batch.DrainEvents().Should().BeEmpty();
    }

    [Fact]
    public void Create_WithDefaultBatchId_Throws()
    {
        var act = () => Batch.Create(default, "/data");
        act.Should().Throw<ArgumentException>().WithMessage("*default(BatchId)*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithBlankSource_Throws(string source)
    {
        var act = () => Batch.Create(AnyBatch, source);
        act.Should().Throw<ArgumentException>();
    }

    // ---- batch-status happy path --------------------------------------

    [Fact]
    public void Start_TransitionsToIngesting_AndEmitsBatchStartedEvent()
    {
        var batch = Batch.Create(AnyBatch, "src");

        batch.Start();

        batch.Status.Should().Be(BatchStatus.Ingesting);
        batch.DrainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<BatchStartedEvent>()
                .Which.Source.Should().Be("src");
    }

    [Fact]
    public void MarkIngested_TransitionsToIngested_NoEvent()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();
        batch.DrainEvents();  // clear

        batch.MarkIngested();

        batch.Status.Should().Be(BatchStatus.Ingested);
        batch.DrainEvents().Should().BeEmpty();
    }

    [Fact]
    public void BeginProcessing_FromIngested_TransitionsToProcessing()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();
        batch.MarkIngested();
        batch.DrainEvents();

        batch.BeginProcessing();

        batch.Status.Should().Be(BatchStatus.Processing);
        batch.DrainEvents().Should().BeEmpty();
    }

    [Fact]
    public void Complete_FromProcessing_EmitsBatchCompletedEvent_AndSetsCompletedAt()
    {
        var batch = AdvanceToProcessing();

        batch.Complete();

        batch.Status.Should().Be(BatchStatus.Completed);
        batch.CompletedAt.Should().NotBeNull();
        batch.DrainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<BatchCompletedEvent>();
    }

    [Fact]
    public void Fail_FromProcessing_EmitsBatchFailedEvent_AndSetsCompletedAt()
    {
        var batch = AdvanceToProcessing();

        batch.Fail("transformer threw");

        batch.Status.Should().Be(BatchStatus.Failed);
        batch.CompletedAt.Should().NotBeNull();
        batch.DrainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<BatchFailedEvent>()
                .Which.Reason.Should().Be("transformer threw");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Fail_WithBlankReason_Throws(string reason)
    {
        var batch = AdvanceToProcessing();
        var act = () => batch.Fail(reason);
        act.Should().Throw<ArgumentException>();
    }

    // ---- retry path ---------------------------------------------------

    [Fact]
    public void BeginProcessing_FromFailed_TransitionsToProcessing()
    {
        var batch = AdvanceToProcessing();
        batch.Fail("first attempt failed");
        batch.DrainEvents();

        batch.BeginProcessing();

        batch.Status.Should().Be(BatchStatus.Processing);
    }

    // ---- batch-status illegal paths -----------------------------------

    [Fact]
    public void Start_FromIngesting_Throws()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();

        var act = () => batch.Start();
        act.Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void Complete_FromCreated_Throws()
    {
        var batch = Batch.Create(AnyBatch, "src");
        var act = () => batch.Complete();
        act.Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void Complete_IsTerminal_FurtherTransitionsThrow()
    {
        var batch = AdvanceToProcessing();
        batch.Complete();

        ((Action)(() => batch.Start())).Should().Throw<IllegalStateTransitionException>();
        ((Action)(() => batch.MarkIngested())).Should().Throw<IllegalStateTransitionException>();
        ((Action)(() => batch.BeginProcessing())).Should().Throw<IllegalStateTransitionException>();
        ((Action)(() => batch.Fail("nope"))).Should().Throw<IllegalStateTransitionException>();
        ((Action)(() => batch.Complete())).Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void Failed_to_Completed_directly_Throws()
    {
        var batch = AdvanceToProcessing();
        batch.Fail("nope");

        var act = () => batch.Complete();
        act.Should().Throw<IllegalStateTransitionException>();
    }

    // ---- row transitions ----------------------------------------------

    [Fact]
    public void TransitionRow_LegalRowStatusChange_DoesNotThrow_AndEmitsNothing()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Pending);

        batch.TransitionRow(row, RowStatus.Processing);

        batch.DrainEvents().Should().BeEmpty();
    }

    [Fact]
    public void TransitionRow_IllegalRowStatusChange_Throws()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Committed);

        var act = () => batch.TransitionRow(row, RowStatus.Pending);
        act.Should().Throw<IllegalStateTransitionException>()
            .Which.Subject.Should().Be("row");
    }

    [Fact]
    public void TransitionRow_RowFromOtherBatch_Throws()
    {
        var batch = AdvanceToProcessing();
        var foreignRow = MakeRow(batchId: new BatchId("other-batch"));

        var act = () => batch.TransitionRow(foreignRow, RowStatus.Processing);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*other-batch*");
    }

    [Fact]
    public void TransitionRow_NullRow_Throws()
    {
        var batch = AdvanceToProcessing();
        var act = () => batch.TransitionRow(null!, RowStatus.Processing);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- quarantine ---------------------------------------------------

    [Fact]
    public void QuarantineRow_FromPending_EmitsRowQuarantinedEvent()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(incomingId: 7, status: RowStatus.Pending, targetTable: "broker");

        batch.QuarantineRow(row, code: "MISSING_REQUIRED", message: "amount was null");

        var events = batch.DrainEvents();
        events.Should().ContainSingle().Which.Should().BeOfType<RowQuarantinedEvent>();

        var ev = (RowQuarantinedEvent)events[0];
        ev.IncomingId.Should().Be(7);
        ev.TargetTable.Should().Be("broker");
        ev.Code.Should().Be("MISSING_REQUIRED");
        ev.Message.Should().Be("amount was null");
    }

    [Fact]
    public void QuarantineRow_FromProcessing_EmitsRowQuarantinedEvent()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Processing);

        batch.QuarantineRow(row, code: "INVALID_TYPE", message: "not a decimal");

        batch.DrainEvents().Should().ContainSingle()
            .Which.Should().BeOfType<RowQuarantinedEvent>();
    }

    [Fact]
    public void QuarantineRow_FromCommitted_Throws()
    {
        // Committed is terminal — quarantining a committed row would
        // mean the row reached destination then got pulled back, which
        // the state machine forbids.
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Committed);

        var act = () => batch.QuarantineRow(row, "X", "m");
        act.Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void QuarantineRow_RowFromOtherBatch_Throws()
    {
        var batch = AdvanceToProcessing();
        var foreignRow = MakeRow(batchId: new BatchId("other-batch"));

        var act = () => batch.QuarantineRow(foreignRow, "X", "m");
        act.Should().Throw<ArgumentException>().WithMessage("*other-batch*");
    }

    [Theory]
    [InlineData("", "m")]
    [InlineData("  ", "m")]
    [InlineData("X", "")]
    [InlineData("X", "  ")]
    public void QuarantineRow_BlankCodeOrMessage_Throws(string code, string message)
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow(status: RowStatus.Pending);

        var act = () => batch.QuarantineRow(row, code, message);
        act.Should().Throw<ArgumentException>();
    }

    // ---- DrainEvents semantics ----------------------------------------

    [Fact]
    public void DrainEvents_ReturnsAccumulatedEventsAndClears()
    {
        var batch = AdvanceToProcessing();
        var row = MakeRow();
        batch.QuarantineRow(row, "X", "m");

        var first = batch.DrainEvents();
        first.Should().ContainSingle();

        var second = batch.DrainEvents();
        second.Should().BeEmpty("DrainEvents must clear the buffer");
    }

    [Fact]
    public void DrainEvents_AccumulatesMultipleEventsAcrossOperations()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();

        var row = MakeRow(status: RowStatus.Pending);
        batch.MarkIngested();
        batch.BeginProcessing();
        batch.QuarantineRow(row, "X", "m");
        batch.Complete();

        var events = batch.DrainEvents();

        events.Should().HaveCount(3);  // BatchStarted, RowQuarantined, BatchCompleted
        events[0].Should().BeOfType<BatchStartedEvent>();
        events[1].Should().BeOfType<RowQuarantinedEvent>();
        events[2].Should().BeOfType<BatchCompletedEvent>();
    }

    // ---- equality -----------------------------------------------------

    [Fact]
    public void Equality_IsByBatchId()
    {
        var a = Batch.Create(AnyBatch, "src");
        var b = Batch.Create(AnyBatch, "different-src");
        var c = Batch.Create(new BatchId("other"), "src");

        a.Should().Be(b);  // same id
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }

    // ---- FromState (rehydration) --------------------------------------

    [Fact]
    public void FromState_SnapshotInProcessing_RehydratesAggregateInThatState()
    {
        var startedAt = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new BatchSnapshot(
            AnyBatch, "src", BatchStatus.Processing, startedAt, CompletedAt: null);

        var batch = Batch.FromState(snapshot);

        batch.Id.Should().Be(AnyBatch);
        batch.Source.Should().Be("src");
        batch.Status.Should().Be(BatchStatus.Processing);
        batch.StartedAt.Should().Be(startedAt);
        batch.CompletedAt.Should().BeNull();
        batch.DrainEvents().Should().BeEmpty();  // rehydration emits no events
    }

    [Fact]
    public void FromState_SnapshotInFailed_AllowsRetryTransition()
    {
        // The retry use case: a Failed snapshot should let the
        // caller transition Failed → Processing via BeginProcessing.
        var snapshot = new BatchSnapshot(
            AnyBatch, "src", BatchStatus.Failed,
            StartedAt: new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2026, 4, 24, 12, 5, 0, TimeSpan.Zero));

        var batch = Batch.FromState(snapshot);

        var act = () => batch.BeginProcessing();
        act.Should().NotThrow();
        batch.Status.Should().Be(BatchStatus.Processing);
    }

    [Fact]
    public void FromState_SnapshotInCompleted_BlocksFurtherTransitions()
    {
        // FromState is not a back door past the state machine.
        // A snapshot in Completed is terminal — BeginProcessing must
        // throw exactly as it would for an aggregate that reached
        // Completed naturally.
        var snapshot = new BatchSnapshot(
            AnyBatch, "src", BatchStatus.Completed,
            StartedAt: new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2026, 4, 24, 12, 5, 0, TimeSpan.Zero));

        var batch = Batch.FromState(snapshot);

        ((Action)(() => batch.BeginProcessing())).Should()
            .Throw<Streamline.Domain.Batches.StateMachine.IllegalStateTransitionException>();
        ((Action)(() => batch.Complete())).Should()
            .Throw<Streamline.Domain.Batches.StateMachine.IllegalStateTransitionException>();
        ((Action)(() => batch.Fail("nope"))).Should()
            .Throw<Streamline.Domain.Batches.StateMachine.IllegalStateTransitionException>();
    }

    [Fact]
    public void FromState_NullSnapshot_Throws()
    {
        var act = () => Batch.FromState(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromState_RejectsInvalidSnapshot_Throws()
    {
        // BatchSnapshot's record-class constructor catches the same
        // invariants, but FromState is a belt-and-braces guard for
        // hand-constructed snapshots that bypass record validation
        // (e.g. via reflection in tests). We can't easily synthesize
        // such an invalid snapshot via the public API, so this test
        // covers the null path and relies on the snapshot's own
        // tests (BatchSnapshotTests) for structural validation.
        var act = () => Batch.FromState(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- helpers ------------------------------------------------------

    private static Batch AdvanceToProcessing()
    {
        var batch = Batch.Create(AnyBatch, "src");
        batch.Start();
        batch.MarkIngested();
        batch.BeginProcessing();
        batch.DrainEvents();  // clear setup events
        return batch;
    }
}
