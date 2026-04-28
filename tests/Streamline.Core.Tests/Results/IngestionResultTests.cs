using AwesomeAssertions;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class IngestionResultTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);


    [Fact]
    public void Totals_SumAcrossFiles()
    {
        var result = new IngestionResult(
        [
            new FileIngestionOutcome("a.csv", 10, 9, 1, TimeSpan.FromMilliseconds(100)),
            new FileIngestionOutcome("b.csv", 20, 20, 0, TimeSpan.FromMilliseconds(200)),
            new FileIngestionOutcome("c.csv", 5, 3, 2, TimeSpan.FromMilliseconds(50)),
        ]);

        result.TotalRowsRead.Should().Be(35);
        result.TotalRowsStaged.Should().Be(32);
        result.TotalRowsQuarantined.Should().Be(3);
        result.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public void Empty_IsSingletonWithNoFiles()
    {
        IngestionResult.Empty.Files.Should().BeEmpty();
        IngestionResult.Empty.TotalRowsRead.Should().Be(0);
        IngestionResult.Empty.TotalDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Construct_WithNullFiles_Throws()
    {
        var act = () => new IngestionResult(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_IsStructuralOverFileSequence()
    {
        var a = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero)]);
        var b = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero)]);
        var c = new IngestionResult(
            [new FileIngestionOutcome("b.csv", 1, 1, 0, TimeSpan.Zero)]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }

    /// <summary>
    /// Back-compat guard: constructing without the observations argument
    /// must continue to work. Pre-1b callers did this and their code
    /// should keep compiling and running.
    /// </summary>
    [Fact]
    public void Construct_WithoutObservations_GivesEmptyObservationsField()
    {
        var result = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 10, 10, 0, TimeSpan.Zero)]);

        result.Observations.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithObservations_PropagatesThem()
    {
        var obs = new[]
        {
            new Observation(ObservationSeverity.Info, "FILE_INGESTED", "ok", "batch-1", FixedTime),
            new Observation(ObservationSeverity.Warning, "FILE_EMPTY", "empty", "batch-1", FixedTime)
            {
                FileLogId = 2,
            },
        };

        var result = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 10, 10, 0, TimeSpan.Zero)],
            obs);

        result.Observations.Should().HaveCount(2);
        result.Observations[0].Code.Should().Be("FILE_INGESTED");
        result.Observations[1].FileLogId.Should().Be(2);
    }

    [Fact]
    public void Equality_IncludesObservationSequence()
    {
        var files = new[] { new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero) };
        var obs1 = new Observation(ObservationSeverity.Info, "FILE_INGESTED", "ok", "b", FixedTime);
        var obs2 = new Observation(ObservationSeverity.Warning, "FILE_EMPTY", "empty", "b", FixedTime);

        var a = new IngestionResult(files, [obs1]);
        var b = new IngestionResult(files, [obs1]);
        var different = new IngestionResult(files, [obs2]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }

    [Fact]
    public void Empty_HasNoObservations()
    {
        IngestionResult.Empty.Observations.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithoutObservabilityDegraded_DefaultsToFalse()
    {
        var result = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 10, 10, 0, TimeSpan.Zero)]);

        result.ObservabilityDegraded.Should().BeFalse();
    }

    [Fact]
    public void Construct_WithObservabilityDegradedTrue_PropagatesThrough()
    {
        var result = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 10, 10, 0, TimeSpan.Zero)],
            observabilityDegraded: true);

        result.ObservabilityDegraded.Should().BeTrue();
    }

    [Fact]
    public void Equality_IncludesObservabilityDegraded()
    {
        var files = new[] { new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero) };
        var clean = new IngestionResult(files, observabilityDegraded: false);
        var degraded = new IngestionResult(files, observabilityDegraded: true);

        clean.Should().NotBe(degraded);
        clean.GetHashCode().Should().NotBe(degraded.GetHashCode());
    }

    [Fact]
    public void Empty_HasObservabilityDegradedFalse()
    {
        IngestionResult.Empty.ObservabilityDegraded.Should().BeFalse();
    }
}
