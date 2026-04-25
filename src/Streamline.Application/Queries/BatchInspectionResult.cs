using System.Collections.Immutable;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Domain.Batches;

namespace Streamline.Application.Queries;

/// <summary>
/// Structured snapshot of a batch's full state for the
/// <c>--inspect &lt;batch&gt;</c> command (Phase 4) and any other
/// caller that wants a one-shot read of "what happened in this
/// batch". The handler aggregates from <c>IStagingRepository</c>,
/// <c>IObservationRepository</c>, and the registry; this type
/// carries pure data and does no formatting (the CLI's job).
/// </summary>
/// <param name="BatchId">Identity.</param>
/// <param name="Status">Current batch lifecycle state.</param>
/// <param name="Source">The source description recorded at batch start.</param>
/// <param name="StartedAt">When the batch entered <see cref="BatchStatus.Created"/>.</param>
/// <param name="CompletedAt">
/// When the batch reached a terminal state, or null if still in flight.
/// </param>
/// <param name="Files">
/// One <see cref="FileIngestionOutcome"/> per file the batch staged.
/// Empty for a batch that hasn't started ingesting.
/// </param>
/// <param name="Tables">
/// One <see cref="TableProcessingOutcome"/> per target table the
/// batch processed. Empty before processing begins.
/// </param>
/// <param name="Observations">
/// Observations recorded against the batch, in
/// <see cref="Observation.RaisedAt"/> order. May be filtered by
/// <see cref="InspectBatchQuery.MinSeverity"/>.
/// </param>
/// <param name="ObservationCountsBySeverity">
/// Pre-computed histogram. Always carries an entry for each of the
/// four severities (zero counts where absent), so callers don't need
/// defensive lookups.
/// </param>
/// <param name="ObservabilityDegraded">
/// True if the resilient sink hit its degradation threshold during
/// the batch — observations after that point may be missing from
/// the recorded set.
/// </param>
public sealed record class BatchInspectionResult(
    BatchId BatchId,
    BatchStatus Status,
    string Source,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    ImmutableArray<FileIngestionOutcome> Files,
    ImmutableArray<TableProcessingOutcome> Tables,
    ImmutableArray<Observation> Observations,
    ImmutableDictionary<ObservationSeverity, long> ObservationCountsBySeverity,
    bool ObservabilityDegraded)
{
    public BatchId BatchId { get; } = RequireValid(BatchId);
    public string Source { get; } = RequireNonBlank(Source, nameof(Source));
    public ImmutableDictionary<ObservationSeverity, long> ObservationCountsBySeverity { get; } =
        EnsureAllSeveritiesPresent(ObservationCountsBySeverity);

    public bool Equals(BatchInspectionResult? other) =>
        other is not null
        && BatchId.Equals(other.BatchId)
        && Status == other.Status
        && Source == other.Source
        && StartedAt == other.StartedAt
        && CompletedAt == other.CompletedAt
        && Files.SequenceEqual(other.Files)
        && Tables.SequenceEqual(other.Tables)
        && Observations.SequenceEqual(other.Observations)
        && CountsEqual(ObservationCountsBySeverity, other.ObservationCountsBySeverity)
        && ObservabilityDegraded == other.ObservabilityDegraded;

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(BatchId);
        hash.Add(Status);
        hash.Add(Source);
        hash.Add(StartedAt);
        hash.Add(CompletedAt);
        foreach (var f in Files) { hash.Add(f); }
        foreach (var t in Tables) { hash.Add(t); }
        foreach (var o in Observations) { hash.Add(o); }
        foreach (var (sev, count) in ObservationCountsBySeverity.OrderBy(kv => kv.Key))
        {
            hash.Add(sev);
            hash.Add(count);
        }
        hash.Add(ObservabilityDegraded);
        return hash.ToHashCode();
    }

    private static BatchId RequireValid(BatchId batchId)
    {
        if (!batchId.IsValid)
        {
            throw new ArgumentException(
                "BatchId must be initialised; default(BatchId) is not a valid value.",
                nameof(batchId));
        }
        return batchId;
    }

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    /// <summary>
    /// Fills in zero counts for any severity not present in the
    /// supplied dictionary. The handler builds the dictionary by
    /// counting observed severities; this guarantees the result
    /// always exposes all four keys, matching the analogous contract
    /// on <c>IStagingRepository.GetRowCountsAsync</c>.
    /// </summary>
    private static ImmutableDictionary<ObservationSeverity, long> EnsureAllSeveritiesPresent(
        ImmutableDictionary<ObservationSeverity, long> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var builder = source.ToBuilder();
        foreach (var sev in Enum.GetValues<ObservationSeverity>())
        {
            if (!builder.ContainsKey(sev))
            {
                builder.Add(sev, 0);
            }
        }
        return builder.ToImmutable();
    }

    private static bool CountsEqual(
        ImmutableDictionary<ObservationSeverity, long> a,
        ImmutableDictionary<ObservationSeverity, long> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        foreach (var (key, valueA) in a)
        {
            if (!b.TryGetValue(key, out var valueB) || valueA != valueB)
            {
                return false;
            }
        }
        return true;
    }
}
