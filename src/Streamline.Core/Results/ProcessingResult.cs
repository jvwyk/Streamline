using System.Collections.Immutable;

namespace Streamline.Core.Results;

/// <summary>
/// Batch-level processing outcome returned by <c>ProcessBatchHandler</c>.
/// Aggregates <see cref="TableProcessingOutcome"/> records across every
/// target table the batch processed, with computed totals.
/// </summary>
/// <remarks>
/// No observations are carried here in sub-phase 1a; the field for
/// <c>ImmutableArray&lt;Observation&gt;</c> lands in sub-phase 1b
/// once the observation model exists.
/// </remarks>
public sealed record class ProcessingResult
{
    public ImmutableArray<TableProcessingOutcome> Tables { get; }

    public ProcessingResult(IEnumerable<TableProcessingOutcome> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Tables = [.. tables];
    }

    public long TotalRowsCommitted => Tables.Sum(t => t.RowsCommitted);
    public long TotalRowsRolledBack => Tables.Sum(t => t.RowsRolledBack);
    public long TotalRowsQuarantined => Tables.Sum(t => t.RowsQuarantined);
    public TimeSpan TotalDuration => Tables.Aggregate(TimeSpan.Zero, (acc, t) => acc + t.Duration);

    public static ProcessingResult Empty { get; } = new(Array.Empty<TableProcessingOutcome>());

    public bool Equals(ProcessingResult? other) =>
        other is not null && Tables.SequenceEqual(other.Tables);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var table in Tables)
        {
            hash.Add(table);
        }
        return hash.ToHashCode();
    }
}
