using System.Runtime.CompilerServices;
using AwesomeAssertions;
using NSubstitute;
using Streamline.Application.Queries;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Queries;

public class InspectBatchHandlerTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construct_NullStaging_Throws()
    {
        var act = () => new InspectBatchHandler(null!, Substitute.For<IObservationRepository>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Construct_NullObservations_Throws()
    {
        var act = () => new InspectBatchHandler(Substitute.For<IStagingRepository>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_NullQuery_Throws()
    {
        var handler = BuildHandler(out _, out _);
        var act = async () => await handler.HandleAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_BatchNotFound_ThrowsKeyNotFound()
    {
        var handler = BuildHandler(out var staging, out _);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns((BatchSnapshot?)null);

        var act = async () => await handler.HandleAsync(new InspectBatchQuery(AnyBatch));

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*batch-1*");
    }

    [Fact]
    public async Task Handle_PopulatesSnapshotFields()
    {
        var handler = BuildHandler(out var staging, out var observations);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(new BatchSnapshot(
                AnyBatch, "/data", BatchStatus.Completed, AnyTime, CompletedAt: AnyTime.AddMinutes(5)));
        observations.GetForBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync([]));

        var result = await handler.HandleAsync(new InspectBatchQuery(AnyBatch));

        result.BatchId.Should().Be(AnyBatch);
        result.Source.Should().Be("/data");
        result.Status.Should().Be(BatchStatus.Completed);
        result.StartedAt.Should().Be(AnyTime);
        result.CompletedAt.Should().Be(AnyTime.AddMinutes(5));
        result.Files.Should().BeEmpty();
        result.Tables.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_BuildsHistogramAcrossSeverities()
    {
        var handler = BuildHandler(out var staging, out var observations);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(new BatchSnapshot(
                AnyBatch, "/data", BatchStatus.Completed, AnyTime, AnyTime));
        observations.GetForBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync(
            [
                MakeObs(ObservationSeverity.Info, ObservationCodes.BATCH_STARTED),
                MakeObs(ObservationSeverity.Info, ObservationCodes.FILE_INGESTED),
                MakeObs(ObservationSeverity.Warning, ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN),
                MakeObs(ObservationSeverity.Error, ObservationCodes.MISSING_REQUIRED),
            ]));

        var result = await handler.HandleAsync(new InspectBatchQuery(AnyBatch));

        result.Observations.Should().HaveCount(4);
        result.ObservationCountsBySeverity[ObservationSeverity.Info].Should().Be(2);
        result.ObservationCountsBySeverity[ObservationSeverity.Warning].Should().Be(1);
        result.ObservationCountsBySeverity[ObservationSeverity.Error].Should().Be(1);
        // Histogram always carries every severity (zero where absent).
        result.ObservationCountsBySeverity[ObservationSeverity.Critical].Should().Be(0);
    }

    [Fact]
    public async Task Handle_MinSeverityFilters_RoutesToFilteredOverload()
    {
        var handler = BuildHandler(out var staging, out var observations);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(new BatchSnapshot(
                AnyBatch, "/data", BatchStatus.Completed, AnyTime, AnyTime));
        observations.GetForBatchAsync(AnyBatch, ObservationSeverity.Warning, Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync(
            [
                MakeObs(ObservationSeverity.Warning, ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN),
                MakeObs(ObservationSeverity.Critical, ObservationCodes.BATCH_FAILED),
            ]));

        var result = await handler.HandleAsync(
            new InspectBatchQuery(AnyBatch, MinSeverity: ObservationSeverity.Warning));

        result.Observations.Should().HaveCount(2);
        result.Observations.Should().NotContain(o => o.Severity == ObservationSeverity.Info);
        // The filtered overload was used (not the unfiltered one).
        observations.Received(1).GetForBatchAsync(
            AnyBatch, ObservationSeverity.Warning, Arg.Any<CancellationToken>());
        observations.DidNotReceive().GetForBatchAsync(AnyBatch, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoMinSeverity_RoutesToUnfilteredOverload()
    {
        var handler = BuildHandler(out var staging, out var observations);
        staging.GetBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(new BatchSnapshot(
                AnyBatch, "/data", BatchStatus.Completed, AnyTime, AnyTime));
        observations.GetForBatchAsync(AnyBatch, Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync([]));

        await handler.HandleAsync(new InspectBatchQuery(AnyBatch));

        observations.Received(1).GetForBatchAsync(AnyBatch, Arg.Any<CancellationToken>());
        observations.DidNotReceive().GetForBatchAsync(
            AnyBatch, Arg.Any<ObservationSeverity>(), Arg.Any<CancellationToken>());
    }

    // ---- helpers ------------------------------------------------------

    private static InspectBatchHandler BuildHandler(
        out IStagingRepository staging,
        out IObservationRepository observations)
    {
        staging = Substitute.For<IStagingRepository>();
        observations = Substitute.For<IObservationRepository>();
        return new InspectBatchHandler(staging, observations);
    }

    private static Observation MakeObs(ObservationSeverity severity, string code) =>
        new(severity, code, $"msg-{code}", AnyBatch.Value, AnyTime);

    private static async IAsyncEnumerable<Observation> ToAsync(
        IReadOnlyList<Observation> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var obs in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return obs;
            await Task.Yield();
        }
    }
}
