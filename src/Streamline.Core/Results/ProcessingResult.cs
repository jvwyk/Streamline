using System.Collections.Immutable;
using Streamline.Core.Observations;

namespace Streamline.Core.Results;

/// <summary>
/// Batch-level processing outcome returned by <c>ProcessBatchHandler</c>.
/// Aggregates <see cref="TableProcessingOutcome"/> records across every
/// target table the batch processed, plus the <see cref="Observation"/>s
/// raised during processing, plus computed totals.
/// </summary>
public sealed record class ProcessingResult
{
    public ImmutableArray<TableProcessingOutcome> Tables { get; }
    public ImmutableArray<Observation> Observations { get; }

    public ProcessingResult(
        IEnumerable<TableProcessingOutcome> tables,
        IEnumerable<Observation>? observations = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Tables = [.. tables];
        Observations = observations is null ? [] : [.. observations];
    }

    public long TotalRowsCommitted => Tables.Sum(t => t.RowsCommitted);
    public long TotalRowsRolledBack => Tables.Sum(t => t.RowsRolledBack);
    public long TotalRowsQuarantined => Tables.Sum(t => t.RowsQuarantined);
    public TimeSpan TotalDuration => Tables.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);

    public static ProcessingResult Empty { get; } = new(Array.Empty<TableProcessingOutcome>());

    public bool Equals(ProcessingResult? other) =>
        other is not null
        && Tables.SequenceEqual(other.Tables)
        && Observations.SequenceEqual(other.Observations);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var table in Tables)
        {
            hash.Add(table);
        }
        foreach (var observation in Observations)
        {
            hash.Add(observation);
        }
        return hash.ToHashCode();
    }
}
