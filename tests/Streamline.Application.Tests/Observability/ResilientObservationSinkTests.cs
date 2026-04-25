using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Streamline.Application.Observability;
using Streamline.Core.Observations;
using Xunit;

namespace Streamline.Application.Tests.Observability;

public class ResilientObservationSinkTests
{
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Zero-delay backoff so tests don't sleep. The number of retries
    /// matches the production default (3); only the timing is
    /// accelerated.
    /// </summary>
    private static readonly IReadOnlyList<TimeSpan> ZeroDelays =
    [
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
    ];

    private static Observation MakeObs(
        ObservationSeverity severity = ObservationSeverity.Info,
        string code = "FILE_INGESTED") =>
        new(severity, code, "msg", "batch-1", AnyTime);

    private static (ResilientObservationSink Sink, IObservationSink Inner, ObservationDegradationState State, ILogger<ResilientObservationSink> Logger)
        Build()
    {
        var inner = Substitute.For<IObservationSink>();
        var state = new ObservationDegradationState();
        var logger = Substitute.For<ILogger<ResilientObservationSink>>();
        var sink = new ResilientObservationSink(inner, logger, state, ZeroDelays);
        return (sink, inner, state, logger);
    }

    // ---- happy path ----------------------------------------------------

    [Fact]
    public async Task RecordAsync_FirstAttemptSucceeds_ResetsStreak_DoesNotDegrade()
    {
        var (sink, inner, state, _) = Build();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await sink.RecordAsync(MakeObs());

        state.IsDegraded.Should().BeFalse();
        state.ConsecutiveFailures.Should().Be(0);
        await inner.Received(1).RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    // ---- retry path ---------------------------------------------------

    [Fact]
    public async Task RecordAsync_FailsThenSucceeds_RetriesAndResets()
    {
        var (sink, inner, state, _) = Build();
        var calls = 0;
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                return calls < 2
                    ? Task.FromException(new InvalidOperationException("transient"))
                    : Task.CompletedTask;
            });

        await sink.RecordAsync(MakeObs());

        calls.Should().Be(2);
        state.ConsecutiveFailures.Should().Be(0);
        state.IsDegraded.Should().BeFalse();
    }

    [Fact]
    public async Task RecordAsync_AllFourAttemptsFail_IncrementsCounter_DoesNotRethrow()
    {
        var (sink, inner, state, _) = Build();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dead")));

        await sink.RecordAsync(MakeObs());  // does NOT throw — graceful degradation

        await inner.Received(4).RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>());
        state.ConsecutiveFailures.Should().Be(1);
        state.IsDegraded.Should().BeFalse();  // 1 < 3
    }

    [Fact]
    public async Task RecordAsync_ThreeConsecutiveFullyFailedCalls_MarksDegraded()
    {
        var (sink, inner, state, _) = Build();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dead")));

        for (var i = 0; i < 3; i++)
        {
            await sink.RecordAsync(MakeObs());
        }

        state.IsDegraded.Should().BeTrue();
    }

    // ---- degraded mode ------------------------------------------------

    [Fact]
    public async Task RecordAsync_AfterDegraded_NonCritical_DropsSilently_NoSinkCall()
    {
        var (sink, inner, _, _) = Build();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dead")));

        for (var i = 0; i < 3; i++)
        {
            await sink.RecordAsync(MakeObs());
        }
        inner.ClearReceivedCalls();

        await sink.RecordAsync(MakeObs(ObservationSeverity.Warning));

        await inner.DidNotReceive().RecordAsync(
            Arg.Any<Observation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_AfterDegraded_Critical_LogsViaILoggerInsteadOfSink()
    {
        var (sink, inner, _, logger) = Build();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dead")));

        for (var i = 0; i < 3; i++)
        {
            await sink.RecordAsync(MakeObs());
        }
        inner.ClearReceivedCalls();
        logger.ClearReceivedCalls();

        await sink.RecordAsync(MakeObs(ObservationSeverity.Critical, "BATCH_FAILED"));

        await inner.DidNotReceive().RecordAsync(
            Arg.Any<Observation>(), Arg.Any<CancellationToken>());
        logger.ReceivedCalls()
            .Should().Contain(call =>
                call.GetMethodInfo().Name == "Log"
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Critical);
    }

    // ---- cancellation -------------------------------------------------

    [Fact]
    public async Task RecordAsync_CancellationDuringInner_Propagates()
    {
        var (sink, inner, _, _) = Build();
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException()));

        var act = async () => await sink.RecordAsync(MakeObs());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- streak reset on success after partial failures ---------------

    [Fact]
    public async Task RecordAsync_SuccessResetsCounterEvenAfterPartialFailures()
    {
        var (sink, inner, state, _) = Build();

        // First call: 4 failures → counter = 1
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("a")));
        await sink.RecordAsync(MakeObs());
        state.ConsecutiveFailures.Should().Be(1);

        // Second call succeeds first try → reset
        inner.RecordAsync(Arg.Any<Observation>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        await sink.RecordAsync(MakeObs());
        state.ConsecutiveFailures.Should().Be(0);
    }

    // ---- WithDefaults factory -----------------------------------------

    [Fact]
    public void WithDefaults_AppliesProductionBackoffDelays()
    {
        var inner = Substitute.For<IObservationSink>();
        var sink = ResilientObservationSink.WithDefaults(
            inner, NullLogger<ResilientObservationSink>.Instance, new ObservationDegradationState());

        sink.MaxRetries.Should().Be(ResilientObservationSink.DefaultBackoffDelays.Count);
        ResilientObservationSink.DefaultBackoffDelays.Should().Equal(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(800));
    }

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Construct_NullInner_Throws()
    {
        var act = () => new ResilientObservationSink(
            null!, NullLogger<ResilientObservationSink>.Instance, new ObservationDegradationState(), ZeroDelays);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullLogger_Throws()
    {
        var inner = Substitute.For<IObservationSink>();
        var act = () => new ResilientObservationSink(
            inner, null!, new ObservationDegradationState(), ZeroDelays);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullState_Throws()
    {
        var inner = Substitute.For<IObservationSink>();
        var act = () => new ResilientObservationSink(
            inner, NullLogger<ResilientObservationSink>.Instance, null!, ZeroDelays);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NegativeDelay_Throws()
    {
        var inner = Substitute.For<IObservationSink>();
        var bad = new[] { TimeSpan.FromSeconds(-1) };
        var act = () => new ResilientObservationSink(
            inner, NullLogger<ResilientObservationSink>.Instance, new ObservationDegradationState(), bad);
        act.Should().Throw<ArgumentException>().WithMessage("*non-negative*");
    }

    [Fact]
    public async Task RecordAsync_NullObservation_Throws()
    {
        var (sink, _, _, _) = Build();
        var act = async () => await sink.RecordAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
