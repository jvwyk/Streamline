using Streamline.Domain.Lineage;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Write-only contract for recording <see cref="RowLineage"/> entries.
/// Passed to C# transformers via <c>BatchContext</c> so they can
/// emit lineage alongside their destination writes without exposing
/// the read side of the lineage repository (transformers shouldn't
/// query lineage; they produce it).
/// </summary>
/// <remarks>
/// <c>ILineageRepository</c> (the engine-side full contract, lands in
/// commit 6 of sub-phase 1c) extends this interface, so a single
/// concrete implementation satisfies both consumers without code
/// duplication.
/// </remarks>
public interface ILineageWriter
{
    /// <summary>
    /// Record a batch of <see cref="RowLineage"/> entries. Atomic
    /// per-call: implementations either persist all or none. Idempotent
    /// retries are not part of the contract — duplicate calls produce
    /// duplicate rows unless the implementation deduplicates by
    /// (incoming_id, destination_schema, destination_table, pk).
    /// </summary>
    Task RecordAsync(IEnumerable<RowLineage> lineages, CancellationToken cancellationToken = default);
}
