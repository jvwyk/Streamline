using System.Collections.Immutable;
using Streamline.Domain.Abstractions;

namespace Streamline.Domain.Batches;

/// <summary>
/// The context handed to a transformer at invocation time. Carries
/// enough scope information for the transformer to find its source
/// data in staging and to record lineage as it writes to the
/// destination. See plan §15 Appendix C.
/// </summary>
/// <param name="BatchId">The batch the transform is running for.</param>
/// <param name="FileLogIds">
/// The <c>file_log</c> IDs that contributed source rows to this
/// batch. Stored in arrival order. A transformer that wants to
/// reason about per-file provenance reads this rather than scanning
/// staging.
/// </param>
/// <param name="SourceSchema">
/// The PostgreSQL schema where staging tables live (typically
/// <c>"staging"</c>). Phase 2 will let consumers configure this.
/// </param>
/// <param name="Lineage">
/// Lineage writer. Transformers MUST emit a
/// <see cref="Streamline.Domain.Lineage.RowLineage"/> per destination
/// row alongside their writes (per plan §11 decision 10 "row-level
/// lineage is mandatory"). Engine-driven upserts populate lineage
/// automatically; transformers do not get the same automation.
/// </param>
public sealed record class BatchContext(
    BatchId BatchId,
    ImmutableArray<long> FileLogIds,
    string SourceSchema,
    ILineageWriter Lineage)
{
    public BatchId BatchId { get; } = RequireValid(BatchId);
    public string SourceSchema { get; } = RequireNonBlank(SourceSchema, nameof(SourceSchema));
    public ILineageWriter Lineage { get; } = RequireNonNull(Lineage, nameof(Lineage));

    private static BatchId RequireValid(BatchId id)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException(
                "BatchId must be initialised; default(BatchId) is not a valid value.",
                nameof(id));
        }
        return id;
    }

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    private static T RequireNonNull<T>(T value, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(value, name);
        return value;
    }
}
