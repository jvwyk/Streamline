using System.Collections.Immutable;
using Streamline.Core.Observations;

namespace Streamline.Core.Results;

/// <summary>
/// Batch-level ingestion outcome returned by <c>IngestBatchHandler</c>.
/// Aggregates <see cref="FileIngestionOutcome"/> records across every
/// file the batch staged, plus the <see cref="Observation"/>s raised
/// during ingestion, plus computed totals.
/// </summary>
public sealed record class IngestionResult
{
    public ImmutableArray<FileIngestionOutcome> Files { get; }
    public ImmutableArray<Observation> Observations { get; }

    public IngestionResult(
        IEnumerable<FileIngestionOutcome> files,
        IEnumerable<Observation>? observations = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = [.. files];
        Observations = observations is null ? [] : [.. observations];
    }

    public long TotalRowsRead => Files.Sum(f => f.RowsRead);
    public long TotalRowsStaged => Files.Sum(f => f.RowsStaged);
    public long TotalRowsQuarantined => Files.Sum(f => f.RowsQuarantined);
    public TimeSpan TotalDuration => Files.Aggregate(TimeSpan.Zero, (acc, f) => acc + f.Duration);

    public static IngestionResult Empty { get; } = new(Array.Empty<FileIngestionOutcome>());

    public bool Equals(IngestionResult? other) =>
        other is not null
        && Files.SequenceEqual(other.Files)
        && Observations.SequenceEqual(other.Observations);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var file in Files)
        {
            hash.Add(file);
        }
        foreach (var observation in Observations)
        {
            hash.Add(observation);
        }
        return hash.ToHashCode();
    }
}
