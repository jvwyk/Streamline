using AwesomeAssertions;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class ProcessingResultTests
{
    private static readonly DateTimeOffset FixedTime =
        new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);


    [Fact]
    public void Totals_SumAcrossTables()
    {
        var result = new ProcessingResult(
        [
            new TableProcessingOutcome("broker", 10, 1, 0, TimeSpan.FromMilliseconds(100)),
            new TableProcessingOutcome("trade", 100, 0, 2, TimeSpan.FromMilliseconds(300)),
        ]);

        result.TotalRowsCommitted.Should().Be(110);
        result.TotalRowsRolledBack.Should().Be(1);
        result.TotalRowsQuarantined.Should().Be(2);
        result.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void Empty_IsSingletonWithNoTables()
    {
        ProcessingResult.Empty.Tables.Should().BeEmpty();
        ProcessingResult.Empty.TotalRowsCommitted.Should().Be(0);
    }

    [Fact]
    public void Construct_WithNullTables_Throws()
    {
        var act = () => new ProcessingResult(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_IsStructuralOverTableSequence()
    {
        var a = new ProcessingResult(
            [new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero)]);
        var b = new ProcessingResult(
            [new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero)]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    /// <summary>
    /// Back-compat guard: constructing without the observations argument
    /// must continue to work. See IngestionResultTests for rationale.
    /// </summary>
    [Fact]
    public void Construct_WithoutObservations_GivesEmptyObservationsField()
    {
        var result = new ProcessingResult(
            [new TableProcessingOutcome("broker", 10, 0, 0, TimeSpan.Zero)]);

        result.Observations.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithObservations_PropagatesThem()
    {
        var obs = new[]
        {
            new Observation(ObservationSeverity.Info, "UPSERT_COMPLETED", "ok", "batch-1", FixedTime)
            {
                TableName = "broker",
            },
            new Observation(ObservationSeverity.Error, "SAVEPOINT_ROLLED_BACK", "rolled back", "batch-1", FixedTime)
            {
                TableName = "trade",
            },
        };

        var result = new ProcessingResult(
            [new TableProcessingOutcome("broker", 5, 0, 0, TimeSpan.Zero)],
            obs);

        result.Observations.Should().HaveCount(2);
        result.Observations[0].Code.Should().Be("UPSERT_COMPLETED");
        result.Observations[1].TableName.Should().Be("trade");
    }

    [Fact]
    public void Equality_IncludesObservationSequence()
    {
        var tables = new[] { new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero) };
        var obs1 = new Observation(ObservationSeverity.Info, "UPSERT_COMPLETED", "ok", "b", FixedTime);
        var obs2 = new Observation(ObservationSeverity.Error, "UPSERT_FAILED", "nope", "b", FixedTime);

        var a = new ProcessingResult(tables, [obs1]);
        var b = new ProcessingResult(tables, [obs1]);
        var different = new ProcessingResult(tables, [obs2]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }

    [Fact]
    public void Empty_HasNoObservations()
    {
        ProcessingResult.Empty.Observations.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithoutObservabilityDegraded_DefaultsToFalse()
    {
        var result = new ProcessingResult(
            [new TableProcessingOutcome("broker", 10, 0, 0, TimeSpan.Zero)]);

        result.ObservabilityDegraded.Should().BeFalse();
    }

    [Fact]
    public void Construct_WithObservabilityDegradedTrue_PropagatesThrough()
    {
        var result = new ProcessingResult(
            [new TableProcessingOutcome("broker", 10, 0, 0, TimeSpan.Zero)],
            observabilityDegraded: true);

        result.ObservabilityDegraded.Should().BeTrue();
    }

    [Fact]
    public void Equality_IncludesObservabilityDegraded()
    {
        var tables = new[] { new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero) };
        var clean = new ProcessingResult(tables, observabilityDegraded: false);
        var degraded = new ProcessingResult(tables, observabilityDegraded: true);

        clean.Should().NotBe(degraded);
    }

    [Fact]
    public void Empty_HasObservabilityDegradedFalse()
    {
        ProcessingResult.Empty.ObservabilityDegraded.Should().BeFalse();
    }
}
