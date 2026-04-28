using AwesomeAssertions;
using NSubstitute;
using Streamline.Application.Services;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Domain.Batches;
using Streamline.Domain.Batches.DomainEvents;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Application.Tests.Services;

public class DomainEventPublisherTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    // ---- Translate: per-event-type mapping ----------------------------

    [Fact]
    public void Translate_BatchStarted_MapsToBATCH_STARTED_Info()
    {
        var ev = new BatchStartedEvent(AnyBatch, AnyTime, "/data");

        var obs = DomainEventPublisher.Translate(ev);

        obs.Code.Should().Be(ObservationCodes.BATCH_STARTED);
        obs.Severity.Should().Be(ObservationSeverity.Info);
        obs.Context["source"].Should().Be("/data");
        obs.BatchId.Should().Be(AnyBatch.Value);
    }

    [Fact]
    public void Translate_RowQuarantined_UsesEventCode_AtError()
    {
        var ev = new RowQuarantinedEvent(
            AnyBatch, AnyTime,
            IncomingId: 42,
            TargetTable: "broker",
            Code: "MISSING_REQUIRED",
            Message: "amount was null");

        var obs = DomainEventPublisher.Translate(ev);

        obs.Code.Should().Be("MISSING_REQUIRED");
        obs.Severity.Should().Be(ObservationSeverity.Error);
        obs.Message.Should().Be("amount was null");
        obs.IncomingId.Should().Be(42);
        obs.TableName.Should().Be("broker");
        obs.Context["incoming_id"].Should().Be(42L);
        obs.Context["target_table"].Should().Be("broker");
    }

    [Fact]
    public void Translate_TransformInvoked_NoFailures_MapsToTRANSFORMER_COMPLETED_Info()
    {
        var reference = new TransformReference(
            TransformKind.SqlFunction, "domain.fn", "intembeko.dim_broker", TransformInvocation.PerBatch);
        var outcome = new TransformOutcome(10, 2, 1, 0, TimeSpan.FromMilliseconds(50));
        var ev = new TransformInvokedEvent(AnyBatch, AnyTime, reference, outcome);

        var obs = DomainEventPublisher.Translate(ev);

        obs.Code.Should().Be(ObservationCodes.TRANSFORMER_COMPLETED);
        obs.Severity.Should().Be(ObservationSeverity.Info);
        obs.Context["rows_inserted"].Should().Be(10L);
        obs.Context["rows_updated"].Should().Be(2L);
        obs.Context["rows_skipped"].Should().Be(1L);
        obs.Context["rows_failed"].Should().Be(0L);
        obs.Context["transform_reference"].Should().Be("domain.fn");
        obs.Context["transform_kind"].Should().Be("SqlFunction");
        obs.Context["destination_table"].Should().Be("intembeko.dim_broker");
        obs.TableName.Should().Be("intembeko.dim_broker");
    }

    [Fact]
    public void Translate_TransformInvoked_WithFailures_MapsToTRANSFORMER_PARTIAL_FAILURE_Error()
    {
        var reference = new TransformReference(
            TransformKind.CSharp, "T,A", "schema.t", TransformInvocation.PerBatch);
        var outcome = new TransformOutcome(5, 0, 0, 3, TimeSpan.FromMilliseconds(10), "3 rows had bad provider codes");
        var ev = new TransformInvokedEvent(AnyBatch, AnyTime, reference, outcome);

        var obs = DomainEventPublisher.Translate(ev);

        obs.Code.Should().Be(ObservationCodes.TRANSFORMER_PARTIAL_FAILURE);
        obs.Severity.Should().Be(ObservationSeverity.Error);
        obs.Context["rows_failed"].Should().Be(3L);
        obs.Context["error_message"].Should().Be("3 rows had bad provider codes");
    }

    [Fact]
    public void Translate_ReconciliationCompleted_Passed_MapsToRECONCILIATION_PASSED_Info()
    {
        var ev = new ReconciliationCompletedEvent(AnyBatch, AnyTime, Passed: true);
        var obs = DomainEventPublisher.Translate(ev);

        obs.Code.Should().Be(ObservationCodes.RECONCILIATION_PASSED);
        obs.Severity.Should().Be(ObservationSeverity.Info);
        obs.Context["passed"].Should().Be(true);
    }

    [Fact]
    public void Translate_ReconciliationCompleted_Failed_MapsToRECONCILIATION_MISMATCH_Warning()
    {
        var ev = new ReconciliationCompletedEvent(AnyBatch, AnyTime, Passed: false, Summary: "10 rows missing");
        var obs = DomainEventPublisher.Translate(ev);

        obs.Code.Should().Be(ObservationCodes.RECONCILIATION_MISMATCH);
        obs.Severity.Should().Be(ObservationSeverity.Warning);
        obs.Context["summary"].Should().Be("10 rows missing");
    }

    [Fact]
    public void Translate_BatchCompleted_MapsToBATCH_COMPLETED_Info()
    {
        var obs = DomainEventPublisher.Translate(new BatchCompletedEvent(AnyBatch, AnyTime));

        obs.Code.Should().Be(ObservationCodes.BATCH_COMPLETED);
        obs.Severity.Should().Be(ObservationSeverity.Info);
    }

    [Fact]
    public void Translate_BatchFailed_MapsToBATCH_FAILED_Critical()
    {
        var obs = DomainEventPublisher.Translate(
            new BatchFailedEvent(AnyBatch, AnyTime, "advisory lock contention"));

        obs.Code.Should().Be(ObservationCodes.BATCH_FAILED);
        obs.Severity.Should().Be(ObservationSeverity.Critical);
        obs.Context["reason"].Should().Be("advisory lock contention");
    }

    // ---- PublishAsync: forwards through the sink ----------------------

    [Fact]
    public async Task PublishAsync_CallsSinkWithTranslatedObservation()
    {
        var sink = Substitute.For<IObservationSink>();
        var publisher = new DomainEventPublisher(sink);
        var ev = new BatchStartedEvent(AnyBatch, AnyTime, "src");

        await publisher.PublishAsync(ev);

        await sink.Received(1).RecordAsync(
            Arg.Is<Observation>(o => o.Code == ObservationCodes.BATCH_STARTED),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_PropagatesSinkException()
    {
        var sink = Substitute.For<IObservationSink>();
        sink.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var publisher = new DomainEventPublisher(sink);
        var ev = new BatchCompletedEvent(AnyBatch, AnyTime);

        var act = async () => await publisher.PublishAsync(ev);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Construct_NullSink_Throws()
    {
        var act = () => new DomainEventPublisher(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Translate_NullEvent_Throws()
    {
        var act = () => DomainEventPublisher.Translate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Translate_UnknownEventType_Throws()
    {
        // Future-proof: if a new event type is added without a
        // mapping, the throw guides the contributor to the right
        // place to fix.
        var unknown = new UnknownEvent(AnyBatch, AnyTime);

        var act = () => DomainEventPublisher.Translate(unknown);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not have a translation*");
    }

    private sealed record class UnknownEvent(BatchId BatchId, DateTimeOffset OccurredAt) : IDomainEvent;
}
