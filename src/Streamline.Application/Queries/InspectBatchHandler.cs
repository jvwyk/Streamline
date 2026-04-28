using System.Collections.Immutable;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;

namespace Streamline.Application.Queries;

/// <summary>
/// Thin entry point for an <see cref="InspectBatchQuery"/>. Looks up
/// the batch's persisted state, streams observations from
/// <see cref="IObservationRepository"/> (filtered by
/// <see cref="InspectBatchQuery.MinSeverity"/> if set), builds the
/// per-severity histogram, and returns a
/// <see cref="BatchInspectionResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-file and per-table outcomes are empty in v1.</b>
/// Reconstructing <see cref="FileIngestionOutcome"/> and
/// <see cref="TableProcessingOutcome"/> from persisted state requires
/// either parsing them out of <c>FILE_INGESTED</c> /
/// <c>UPSERT_COMPLETED</c> observation context (fragile) or adding
/// dedicated query methods to <see cref="IStagingRepository"/> /
/// destination (clean but out of scope here). Phase 4's
/// <c>--inspect</c> CLI will need them; the work belongs to that
/// sub-phase. v1 returns empty arrays so the type contract holds —
/// callers see structurally-valid <see cref="BatchInspectionResult"/>
/// values either way.
/// </para>
/// <para>
/// <b>Histogram always carries every severity.</b> The
/// <see cref="BatchInspectionResult"/> constructor enforces that —
/// see the <c>EnsureAllSeveritiesPresent</c> helper there. The
/// handler's loop only contributes seen severities; the absent ones
/// land at zero via that constructor.
/// </para>
/// <para>
/// <b>Missing batch.</b> Same agreement as <c>RetryBatchHandler</c>
/// and <c>ProcessBatchHandler</c>: the repository returns null for
/// a missing id, the handler converts to
/// <see cref="KeyNotFoundException"/>.
/// </para>
/// </remarks>
public sealed class InspectBatchHandler
{
    private readonly IStagingRepository _staging;
    private readonly IObservationRepository _observations;

    public InspectBatchHandler(
        IStagingRepository staging,
        IObservationRepository observations)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(observations);

        _staging = staging;
        _observations = observations;
    }

    public async Task<BatchInspectionResult> HandleAsync(
        InspectBatchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var snapshot = await _staging.GetBatchAsync(query.BatchId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            throw new KeyNotFoundException(
                $"No batch found with id '{query.BatchId}'.");
        }

        var observations = ImmutableArray.CreateBuilder<Observation>();
        var counts = ImmutableDictionary.CreateBuilder<ObservationSeverity, long>();

        var stream = query.MinSeverity is { } minSeverity
            ? _observations.GetForBatchAsync(query.BatchId, minSeverity, cancellationToken)
            : _observations.GetForBatchAsync(query.BatchId, cancellationToken);

        await foreach (var observation in stream.ConfigureAwait(false))
        {
            observations.Add(observation);
            counts[observation.Severity] = counts.TryGetValue(observation.Severity, out var existing)
                ? existing + 1
                : 1;
        }

        return new BatchInspectionResult(
            BatchId: snapshot.BatchId,
            Status: snapshot.Status,
            Source: snapshot.Source,
            StartedAt: snapshot.StartedAt,
            CompletedAt: snapshot.CompletedAt,
            Files: [],
            Tables: [],
            Observations: observations.ToImmutable(),
            ObservationCountsBySeverity: counts.ToImmutable(),
            ObservabilityDegraded: false);
    }
}
