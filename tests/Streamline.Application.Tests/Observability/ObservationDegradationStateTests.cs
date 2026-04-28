using AwesomeAssertions;
using Streamline.Application.Observability;
using Xunit;

namespace Streamline.Application.Tests.Observability;

public class ObservationDegradationStateTests
{
    [Fact]
    public void Fresh_NotDegraded_ZeroFailures()
    {
        var state = new ObservationDegradationState();

        state.IsDegraded.Should().BeFalse();
        state.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RecordFailure_BelowThreshold_NotYetDegraded()
    {
        var state = new ObservationDegradationState();

        state.RecordFailure();
        state.RecordFailure();

        state.ConsecutiveFailures.Should().Be(2);
        state.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public void RecordFailure_AtThreshold_BecomesDegraded()
    {
        var state = new ObservationDegradationState();

        for (var i = 0; i < ObservationDegradationState.FailureThreshold; i++)
        {
            state.RecordFailure();
        }

        state.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public void RecordSuccess_BeforeThreshold_ResetsCounter()
    {
        var state = new ObservationDegradationState();
        state.RecordFailure();
        state.RecordFailure();

        state.RecordSuccess();

        state.ConsecutiveFailures.Should().Be(0);
        state.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public void RecordSuccess_AfterDegrading_DoesNotRecover()
    {
        // Once degraded within a batch, the state stays degraded —
        // recovery doesn't happen mid-batch.
        var state = new ObservationDegradationState();
        for (var i = 0; i < ObservationDegradationState.FailureThreshold; i++)
        {
            state.RecordFailure();
        }
        state.IsDegraded.Should().BeTrue();

        state.RecordSuccess();

        state.IsDegraded.Should().BeTrue("degradation persists for the rest of the batch");
    }

    [Fact]
    public void Threshold_IsDocumentedAsThree()
    {
        // Pinned for the v1 contract. If Phase 4 makes the threshold
        // configurable, this test moves to checking the configured
        // value.
        ObservationDegradationState.FailureThreshold.Should().Be(3);
    }
}
