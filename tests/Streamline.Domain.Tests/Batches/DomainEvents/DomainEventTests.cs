using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Batches;
using Streamline.Domain.Batches.DomainEvents;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Domain.Tests.Batches.DomainEvents;

/// <summary>
/// Single test class for all six domain events — each event is a
/// trivial record with construction invariants. One file is more
/// readable than six separate test classes for content this small.
/// </summary>
public class DomainEventTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BatchStartedEvent_PreservesFields()
    {
        var ev = new BatchStartedEvent(AnyBatch, AnyTime, "/data/incoming");

        ev.BatchId.Should().Be(AnyBatch);
        ev.OccurredAt.Should().Be(AnyTime);
        ev.Source.Should().Be("/data/incoming");
        ev.Should().BeAssignableTo<IDomainEvent>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BatchStartedEvent_BlankSource_Throws(string source)
    {
        var act = () => new BatchStartedEvent(AnyBatch, AnyTime, source);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RowQuarantinedEvent_PreservesFields()
    {
        var ev = new RowQuarantinedEvent(
            AnyBatch, AnyTime,
            IncomingId: 42,
            TargetTable: "broker",
            Code: "MISSING_REQUIRED",
            Message: "amount was null");

        ev.IncomingId.Should().Be(42);
        ev.TargetTable.Should().Be("broker");
        ev.Code.Should().Be("MISSING_REQUIRED");
        ev.Message.Should().Be("amount was null");
        ev.Should().BeAssignableTo<IDomainEvent>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RowQuarantinedEvent_NonPositiveIncomingId_Throws(long incomingId)
    {
        var act = () => new RowQuarantinedEvent(AnyBatch, AnyTime, incomingId, "t", "X", "m");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", "X", "m")]
    [InlineData("t", "", "m")]
    [InlineData("t", "X", "")]
    public void RowQuarantinedEvent_BlankRequiredField_Throws(string table, string code, string message)
    {
        var act = () => new RowQuarantinedEvent(AnyBatch, AnyTime, 1, table, code, message);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TransformInvokedEvent_PreservesFields()
    {
        var reference = new TransformReference(
            TransformKind.SqlFunction, "domain.fn", "intembeko.dim_broker", TransformInvocation.PerBatch);
        var outcome = new TransformOutcome(10, 0, 0, 0, TimeSpan.FromMilliseconds(50));

        var ev = new TransformInvokedEvent(AnyBatch, AnyTime, reference, outcome);

        ev.Reference.Should().BeSameAs(reference);
        ev.Outcome.Should().BeSameAs(outcome);
        ev.Should().BeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void TransformInvokedEvent_NullReference_Throws()
    {
        var outcome = new TransformOutcome(0, 0, 0, 0, TimeSpan.Zero);
        var act = () => new TransformInvokedEvent(AnyBatch, AnyTime, null!, outcome);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TransformInvokedEvent_NullOutcome_Throws()
    {
        var reference = new TransformReference(
            TransformKind.CSharp, "T,A", "schema.table", TransformInvocation.PerBatch);
        var act = () => new TransformInvokedEvent(AnyBatch, AnyTime, reference, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReconciliationCompletedEvent_PreservesFields()
    {
        var ev = new ReconciliationCompletedEvent(AnyBatch, AnyTime, Passed: true, Summary: null);

        ev.Passed.Should().BeTrue();
        ev.Summary.Should().BeNull();
        ev.Should().BeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void ReconciliationCompletedEvent_AllowsSummary()
    {
        var ev = new ReconciliationCompletedEvent(AnyBatch, AnyTime, Passed: false, Summary: "10 rows missing");
        ev.Summary.Should().Be("10 rows missing");
    }

    [Fact]
    public void BatchCompletedEvent_PreservesFields()
    {
        var ev = new BatchCompletedEvent(AnyBatch, AnyTime);

        ev.BatchId.Should().Be(AnyBatch);
        ev.OccurredAt.Should().Be(AnyTime);
        ev.Should().BeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void BatchFailedEvent_PreservesFields()
    {
        var ev = new BatchFailedEvent(AnyBatch, AnyTime, "advisory lock contention");

        ev.Reason.Should().Be("advisory lock contention");
        ev.Should().BeAssignableTo<IDomainEvent>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BatchFailedEvent_BlankReason_Throws(string reason)
    {
        var act = () => new BatchFailedEvent(AnyBatch, AnyTime, reason);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_IsStructural_ForEachEventType()
    {
        // Quick smoke check that record-class equality wires up
        // correctly for every event type. Picking one scenario per
        // type rather than exhaustive coverage — the records have
        // simple shape, so default equality semantics suffice.
        new BatchStartedEvent(AnyBatch, AnyTime, "s")
            .Should().Be(new BatchStartedEvent(AnyBatch, AnyTime, "s"));

        new RowQuarantinedEvent(AnyBatch, AnyTime, 1, "t", "C", "m")
            .Should().Be(new RowQuarantinedEvent(AnyBatch, AnyTime, 1, "t", "C", "m"));

        new ReconciliationCompletedEvent(AnyBatch, AnyTime, true)
            .Should().Be(new ReconciliationCompletedEvent(AnyBatch, AnyTime, true));

        new BatchCompletedEvent(AnyBatch, AnyTime)
            .Should().Be(new BatchCompletedEvent(AnyBatch, AnyTime));

        new BatchFailedEvent(AnyBatch, AnyTime, "r")
            .Should().Be(new BatchFailedEvent(AnyBatch, AnyTime, "r"));
    }
}
