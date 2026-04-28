using System.Collections.Immutable;
using AwesomeAssertions;
using Streamline.Application.Queries;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Queries;

public class BatchInspectionResultTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    private static BatchInspectionResult MakeBare(
        ImmutableDictionary<ObservationSeverity, long>? counts = null) =>
        new(
            AnyBatch,
            BatchStatus.Created,
            "src",
            AnyTime,
            CompletedAt: null,
            Files: [],
            Tables: [],
            Observations: [],
            ObservationCountsBySeverity: counts ?? ImmutableDictionary<ObservationSeverity, long>.Empty,
            ObservabilityDegraded: false);

    [Fact]
    public void Construct_PreservesEveryField()
    {
        var files = new[] { new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero) }.ToImmutableArray();
        var tables = new[] { new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero) }.ToImmutableArray();
        var obs = new[] { new Observation(ObservationSeverity.Info, "C", "m", "batch-1", AnyTime) }.ToImmutableArray();
        var counts = ImmutableDictionary<ObservationSeverity, long>.Empty.Add(ObservationSeverity.Info, 1);

        var result = new BatchInspectionResult(
            AnyBatch,
            BatchStatus.Completed,
            "src",
            AnyTime,
            CompletedAt: AnyTime.AddMinutes(5),
            Files: files,
            Tables: tables,
            Observations: obs,
            ObservationCountsBySeverity: counts,
            ObservabilityDegraded: true);

        result.BatchId.Should().Be(AnyBatch);
        result.Status.Should().Be(BatchStatus.Completed);
        result.Source.Should().Be("src");
        result.CompletedAt.Should().Be(AnyTime.AddMinutes(5));
        result.Files.Should().HaveCount(1);
        result.Tables.Should().HaveCount(1);
        result.Observations.Should().HaveCount(1);
        result.ObservabilityDegraded.Should().BeTrue();
    }

    [Fact]
    public void Construct_DefaultBatchId_Throws()
    {
        var act = () => new BatchInspectionResult(
            default, BatchStatus.Created, "src", AnyTime, null,
            [], [], [],
            ImmutableDictionary<ObservationSeverity, long>.Empty,
            ObservabilityDegraded: false);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_BlankSource_Throws(string source)
    {
        var act = () => new BatchInspectionResult(
            AnyBatch, BatchStatus.Created, source, AnyTime, null,
            [], [], [],
            ImmutableDictionary<ObservationSeverity, long>.Empty,
            false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ObservationCountsBySeverity_AlwaysHasAllFourSeverities()
    {
        // Even if the caller supplies an empty dictionary, the result
        // exposes all four severities with zero counts. Mirrors the
        // GetRowCountsAsync contract (RowStatus dictionary always
        // complete) so callers don't need defensive lookups.
        var result = MakeBare();

        result.ObservationCountsBySeverity.Should().ContainKeys(
            ObservationSeverity.Info,
            ObservationSeverity.Warning,
            ObservationSeverity.Error,
            ObservationSeverity.Critical);

        foreach (var sev in Enum.GetValues<ObservationSeverity>())
        {
            result.ObservationCountsBySeverity[sev].Should().Be(0);
        }
    }

    [Fact]
    public void ObservationCountsBySeverity_PreservesNonZeroCounts()
    {
        var counts = ImmutableDictionary<ObservationSeverity, long>.Empty
            .Add(ObservationSeverity.Warning, 5)
            .Add(ObservationSeverity.Error, 2);

        var result = MakeBare(counts);

        result.ObservationCountsBySeverity[ObservationSeverity.Warning].Should().Be(5);
        result.ObservationCountsBySeverity[ObservationSeverity.Error].Should().Be(2);
        result.ObservationCountsBySeverity[ObservationSeverity.Info].Should().Be(0);
        result.ObservationCountsBySeverity[ObservationSeverity.Critical].Should().Be(0);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = MakeBare();
        var b = MakeBare();
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
