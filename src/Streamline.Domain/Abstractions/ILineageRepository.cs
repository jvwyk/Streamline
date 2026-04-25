using Streamline.Domain.Lineage;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Engine-side full contract for row-level lineage: the write
/// surface inherited from <see cref="ILineageWriter"/> plus query
/// methods used by <c>--lineage &lt;destination_pk&gt;</c>
/// (Phase 4) and the <c>--inspect &lt;batch&gt;</c> command.
/// </summary>
/// <remarks>
/// Inheriting from <see cref="ILineageWriter"/> rather than
/// duplicating the write method means a single Postgres
/// implementation satisfies both contracts. Transformers that
/// receive the writer via <c>BatchContext</c> see only the write
/// surface — they cannot accidentally query lineage during a
/// transform, which is by design.
/// </remarks>
public interface ILineageRepository : ILineageWriter
{
    /// <summary>
    /// Trace forward: every <see cref="RowLineage"/> entry that
    /// originated from a given staging <c>incoming_id</c>. Returns
    /// an empty sequence (not null) when the row produced no
    /// destination writes — typically because it was quarantined.
    /// </summary>
    IAsyncEnumerable<RowLineage> GetForSourceRowAsync(
        long incomingId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trace backward: every <see cref="RowLineage"/> entry whose
    /// destination matches the given (schema, table, pk-values)
    /// triple. Multi-result when several source rows map to the
    /// same destination row (e.g. transform-driven aggregation).
    /// </summary>
    IAsyncEnumerable<RowLineage> GetForDestinationAsync(
        string schema,
        string table,
        IReadOnlyList<object> pkValues,
        CancellationToken cancellationToken = default);
}
