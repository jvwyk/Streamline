using System.Runtime.CompilerServices;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Lineage;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="ILineageRepository"/> (which extends
/// <see cref="ILineageWriter"/>). Stores lineage entries in a
/// thread-safe list keyed by insertion order; query methods filter
/// by source <c>incoming_id</c> or destination
/// (schema, table, pk-values).
/// </summary>
/// <remarks>
/// <para>
/// <b>RecordAsync is not deduplicating.</b> Per the
/// <see cref="ILineageWriter.RecordAsync"/> contract: "Idempotent
/// retries are not part of the contract — duplicate calls produce
/// duplicate rows unless the implementation deduplicates by
/// (incoming_id, destination_schema, destination_table, pk)." The
/// fake matches that — duplicates are recorded as duplicates;
/// callers must not re-record on retry. (The Postgres
/// implementation may dedup; this fake doesn't try to predict that.)
/// </para>
/// <para>
/// <b>Per-call atomicity.</b> RecordAsync persists all-or-none —
/// the fake collects the input enumerable into a buffer, then
/// adds the buffer atomically under the lock. A
/// <see cref="OperationCanceledException"/> mid-enumeration leaves
/// nothing recorded.
/// </para>
/// </remarks>
public sealed class InMemoryLineageRepository : ILineageRepository
{
    private readonly object _lock = new();
    private readonly List<RowLineage> _entries = [];

    public Task RecordAsync(IEnumerable<RowLineage> lineages, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(lineages);

        // Materialise first so cancellation mid-enumeration leaves
        // nothing partially recorded.
        var buffered = lineages.ToList();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            foreach (var lineage in buffered)
            {
                ArgumentNullException.ThrowIfNull(lineage);
                _entries.Add(lineage);
            }
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<RowLineage> GetForSourceRowAsync(
        long incomingId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Snapshot under the lock; yield without holding it.
        List<RowLineage> snapshot;
        lock (_lock)
        {
            snapshot = _entries.Where(l => l.IncomingId == incomingId).ToList();
        }
        foreach (var entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    public async IAsyncEnumerable<RowLineage> GetForDestinationAsync(
        string schema,
        string table,
        IReadOnlyList<object> pkValues,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(pkValues);

        List<RowLineage> snapshot;
        lock (_lock)
        {
            snapshot = _entries
                .Where(l => l.DestinationSchema == schema
                         && l.DestinationTable == table
                         && l.DestinationPkValues.SequenceEqual(pkValues))
                .ToList();
        }
        foreach (var entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.Yield();
        }
    }

    /// <summary>
    /// Test-only: snapshot of every recorded lineage entry, in
    /// insertion order. 1h tests use the production query API
    /// preferentially; this is for "verify the full audit trail"
    /// assertions where a query method doesn't expose the right
    /// view.
    /// </summary>
    public IReadOnlyList<RowLineage> ForTestingOnly_AllEntries()
    {
        lock (_lock)
        {
            return _entries.ToArray();
        }
    }
}
