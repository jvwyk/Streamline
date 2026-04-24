using System.Collections.Immutable;

namespace Streamline.Core.Results;

/// <summary>
/// Batch-level ingestion outcome returned by <c>IngestBatchHandler</c>.
/// Aggregates <see cref="FileIngestionOutcome"/> records across every
/// file the batch staged, with computed totals.
/// </summary>
/// <remarks>
/// No observations are carried here in sub-phase 1a; the field for
/// <c>ImmutableArray&lt;Observation&gt;</c> lands in sub-phase 1b
/// once the observation model exists.
/// </remarks>
public sealed record class IngestionResult
{
    public ImmutableArray<FileIngestionOutcome> Files { get; }

    public IngestionResult(IEnumerable<FileIngestionOutcome> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Files = [.. files];
    }

    public long TotalRowsRead => Files.Sum(f => f.RowsRead);
    public long TotalRowsStaged => Files.Sum(f => f.RowsStaged);
    public long TotalRowsQuarantined => Files.Sum(f => f.RowsQuarantined);
    public TimeSpan TotalDuration => Files.Aggregate(TimeSpan.Zero, (acc, f) => acc + f.Duration);

    public static IngestionResult Empty { get; } = new(Array.Empty<FileIngestionOutcome>());

    public bool Equals(IngestionResult? other) =>
        other is not null && Files.SequenceEqual(other.Files);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var file in Files)
        {
            hash.Add(file);
        }
        return hash.ToHashCode();
    }
}
