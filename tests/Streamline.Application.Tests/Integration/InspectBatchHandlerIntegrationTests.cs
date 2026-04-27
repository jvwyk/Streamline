using AwesomeAssertions;
using Streamline.Application.Queries;
using Streamline.Application.Tests.Fakes;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="InspectBatchHandler"/>. The
/// handler is the read side of the observation flow: tests verify
/// it surfaces every observation that landed in the repository and
/// applies the optional severity filter correctly.
/// </summary>
public class InspectBatchHandlerIntegrationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Inspect_ReturnsSnapshotPlusObservationsAndHistogram()
    {
        var fixture = new Fixture();
        var batch = await fixture.Staging.StartBatchAsync("/data");

        // Seed a varied set of observations directly through the
        // repository — the handler reads them back.
        await fixture.Observations.RecordAsync(MakeObs(batch, T0,
            ObservationSeverity.Info, ObservationCodes.BATCH_STARTED));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(1),
            ObservationSeverity.Warning, ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(2),
            ObservationSeverity.Error, ObservationCodes.MISSING_REQUIRED));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(3),
            ObservationSeverity.Critical, ObservationCodes.BATCH_FAILED));

        var handler = new InspectBatchHandler(fixture.Staging, fixture.Observations);
        var result = await handler.HandleAsync(new InspectBatchQuery(batch));

        result.BatchId.Should().Be(batch);
        result.Source.Should().Be("/data");
        result.Status.Should().Be(BatchStatus.Created);
        result.Observations.Should().HaveCount(4);

        // Histogram contract: every severity present (zero where
        // absent), per BatchInspectionResult's
        // EnsureAllSeveritiesPresent invariant. The four severities
        // each have exactly one observation here.
        result.ObservationCountsBySeverity[ObservationSeverity.Info].Should().Be(1);
        result.ObservationCountsBySeverity[ObservationSeverity.Warning].Should().Be(1);
        result.ObservationCountsBySeverity[ObservationSeverity.Error].Should().Be(1);
        result.ObservationCountsBySeverity[ObservationSeverity.Critical].Should().Be(1);
    }

    [Fact]
    public async Task Inspect_WithMinSeverity_FiltersToWarningAndAbove()
    {
        var fixture = new Fixture();
        var batch = await fixture.Staging.StartBatchAsync("/data");

        await fixture.Observations.RecordAsync(MakeObs(batch, T0,
            ObservationSeverity.Info, ObservationCodes.BATCH_STARTED));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(1),
            ObservationSeverity.Warning, ObservationCodes.SCHEMA_DRIFT_NEW_COLUMN));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(2),
            ObservationSeverity.Critical, ObservationCodes.BATCH_FAILED));

        var handler = new InspectBatchHandler(fixture.Staging, fixture.Observations);
        var result = await handler.HandleAsync(
            new InspectBatchQuery(batch, MinSeverity: ObservationSeverity.Warning));

        // Filter contract: only observations at or above MinSeverity.
        result.Observations.Should().HaveCount(2);
        result.Observations.Should().NotContain(o => o.Severity == ObservationSeverity.Info);
    }

    [Fact]
    public async Task Inspect_ObservationsOrderedByRaisedAt()
    {
        var fixture = new Fixture();
        var batch = await fixture.Staging.StartBatchAsync("/data");

        // Insert out of timestamp order; assert read-back order.
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(10), code: "C"));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0.AddSeconds(5), code: "B"));
        await fixture.Observations.RecordAsync(MakeObs(batch, T0, code: "A"));

        var handler = new InspectBatchHandler(fixture.Staging, fixture.Observations);
        var result = await handler.HandleAsync(new InspectBatchQuery(batch));

        result.Observations.Select(o => o.Code).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task Inspect_ReflectsBatchStatusFromStaging()
    {
        // Verifies the handler reads the persisted status, not a
        // cached or default value. After the staging fake's
        // ForTestingOnly_SetBatchStatus drives the batch to
        // Completed, InspectBatchHandler should report Completed.
        var fixture = new Fixture();
        var batch = await fixture.Staging.StartBatchAsync("/data");
        var completedAt = DateTimeOffset.UtcNow;
        fixture.Staging.ForTestingOnly_SetBatchStatus(batch, BatchStatus.Completed, completedAt);

        var handler = new InspectBatchHandler(fixture.Staging, fixture.Observations);
        var result = await handler.HandleAsync(new InspectBatchQuery(batch));

        result.Status.Should().Be(BatchStatus.Completed);
        result.CompletedAt.Should().Be(completedAt);
    }

    // ---- fixtures ----------------------------------------------------

    private sealed class Fixture
    {
        public InMemoryStagingRepository Staging { get; } = new();
        public InMemoryObservationRepository Observations { get; } = new();
    }

    private static Observation MakeObs(
        BatchId batch, DateTimeOffset raisedAt,
        ObservationSeverity severity = ObservationSeverity.Info,
        string code = "FILE_INGESTED") =>
        new(severity, code, $"msg-{code}", batch.Value, raisedAt);
}
