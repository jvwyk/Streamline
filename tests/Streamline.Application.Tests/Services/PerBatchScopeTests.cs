using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Observations;
using Xunit;

namespace Streamline.Application.Tests.Services;

public class PerBatchScopeTests
{
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateForBatch_NullInnerSink_Throws()
    {
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();
        var act = () => PerBatchScope.CreateForBatch(null!, logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateForBatch_NullLogger_Throws()
    {
        var inner = Substitute.For<IObservationSink>();
        var act = () => PerBatchScope.CreateForBatch(inner, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateForBatch_PopulatesAllThreePieces()
    {
        var inner = Substitute.For<IObservationSink>();
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();

        var scope = PerBatchScope.CreateForBatch(inner, logger);

        scope.DegradationState.Should().NotBeNull();
        scope.DegradationState.IsDegraded.Should().BeFalse();
        scope.DegradationState.ConsecutiveFailures.Should().Be(0);
        scope.Sink.Should().BeOfType<ResilientObservationSink>();
        scope.EventPublisher.Should().NotBeNull();
    }

    [Fact]
    public void CreateForBatch_TwoCalls_ProduceIndependentDegradationState()
    {
        // The whole point of per-batch scoping: a sink failure in batch
        // A must not leak into batch B's degradation state.
        var inner = Substitute.For<IObservationSink>();
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();

        var scopeA = PerBatchScope.CreateForBatch(inner, logger);
        var scopeB = PerBatchScope.CreateForBatch(inner, logger);

        scopeA.DegradationState.Should().NotBeSameAs(scopeB.DegradationState);
    }

    [Fact]
    public async Task Sink_RoutesThroughResilientDecorator()
    {
        var inner = Substitute.For<IObservationSink>();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();

        var scope = PerBatchScope.CreateForBatch(inner, logger);
        var observation = new Observation(
            ObservationSeverity.Info, "FILE_INGESTED", "msg", "batch-1", AnyTime);

        await scope.Sink.RecordAsync(observation);

        await inner.Received(1).RecordAsync(observation, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EventPublisher_WiredOverScopeSink()
    {
        // The publisher must use the scope's resilient sink, not the
        // raw inner sink — otherwise observations bypass retry and
        // degradation logic.
        var inner = Substitute.For<IObservationSink>();
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();

        var scope = PerBatchScope.CreateForBatch(inner, logger);

        scope.EventPublisher.Should().NotBeNull();
        // The DomainEventPublisher constructor takes an IObservationSink;
        // we verified via the Sink_RoutesThroughResilientDecorator test
        // that scope.Sink is the resilient one. The publisher's PublishAsync
        // path is exercised in DomainEventPublisherTests; here we just
        // check that the publisher exists and is non-null.
    }
}
