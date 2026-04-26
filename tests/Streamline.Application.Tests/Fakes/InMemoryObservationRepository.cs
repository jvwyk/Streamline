using System.Runtime.CompilerServices;
using Streamline.Core.Observations;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IObservationRepository"/>. Stores
/// observations in insertion order per batch; reads return them
/// sorted by <see cref="Observation.RaisedAt"/> with insertion-
/// order as the deterministic tiebreaker for equal timestamps
/// (Q13 of the 1g plan).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why insertion-order tiebreaker.</b> Two observations emitted
/// in the same batch operation can legitimately share a
/// <see cref="Observation.RaisedAt"/> timestamp (clock granularity
/// or explicit equal timestamps in tests). Without a deterministic
/// secondary sort, tests asserting observation order are flaky.
/// Insertion-order matches "the order they were emitted," which
/// is what callers usually want.
/// </para>
/// <para>
/// <b>Atomicity.</b> RecordBatchAsync is all-or-none — observations
/// are buffered first, then added atomically under the lock.
/// </para>
/// <para>
/// <b>Thread safety.</b> Per Q20: multiple batches running
/// concurrently will write observations concurrently. Single
/// object lock guards the per-batch lists.
/// </para>
/// </remarks>
public sealed class InMemoryObservationRepository : IObservationRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<TimedObservation>> _byBatch =
        new(StringComparer.Ordinal);
    private long _sequence;

    public Task RecordAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(observation);

        lock (_lock)
        {
            Append(observation);
        }
        return Task.CompletedTask;
    }

    public Task RecordBatchAsync(IReadOnlyList<Observation> observations, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(observations);

        // Materialise null check before taking the lock so a partial
        // batch isn't recorded on a null entry.
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
        }

        lock (_lock)
        {
            foreach (var observation in observations)
            {
                Append(observation);
            }
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Observation> GetForBatchAsync(
        BatchId batch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = SnapshotForBatch(batch, minSeverity: null);
        foreach (var entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<Observation> GetForBatchAsync(
        BatchId batch,
        ObservationSeverity minSeverity,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = SnapshotForBatch(batch, minSeverity);
        foreach (var entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Test-only: snapshot of every recorded observation across all
    /// batches, in insertion order.
    /// </summary>
    public IReadOnlyList<Observation> ForTestingOnly_AllObservations()
    {
        lock (_lock)
        {
            return _byBatch.Values
                .SelectMany(list => list)
                .OrderBy(t => t.Sequence)
                .Select(t => t.Observation)
                .ToArray();
        }
    }

    private void Append(Observation observation)
    {
        if (!_byBatch.TryGetValue(observation.BatchId, out var list))
        {
            list = [];
            _byBatch[observation.BatchId] = list;
        }
        list.Add(new TimedObservation(observation, ++_sequence));
    }

    private List<Observation> SnapshotForBatch(BatchId batch, ObservationSeverity? minSeverity)
    {
        lock (_lock)
        {
            if (!_byBatch.TryGetValue(batch.Value, out var list))
            {
                return [];
            }
            return list
                .Where(t => minSeverity is null || t.Observation.Severity >= minSeverity.Value)
                // Stable sort on RaisedAt with insertion sequence as
                // the deterministic tiebreaker. .NET's OrderBy is
                // stable, so a single OrderBy on RaisedAt would
                // preserve insertion order for ties — but being
                // explicit about the tiebreaker via ThenBy makes the
                // intent obvious to a future reader.
                .OrderBy(t => t.Observation.RaisedAt)
                .ThenBy(t => t.Sequence)
                .Select(t => t.Observation)
                .ToList();
        }
    }

    private sealed record class TimedObservation(Observation Observation, long Sequence);
}
