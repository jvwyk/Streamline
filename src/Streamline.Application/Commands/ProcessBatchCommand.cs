using Streamline.Domain.Batches;

namespace Streamline.Application.Commands;

/// <summary>
/// Request to process every <see cref="Streamline.Core.Enums.RowStatus.Pending"/>
/// row in <see cref="BatchId"/>. The handler walks target tables in
/// dependency order, validates each row, performs FK resolution,
/// and either upserts (replication mode) or invokes a transformer
/// (transform mode) per registry entry.
/// </summary>
/// <param name="BatchId">
/// The batch produced by a prior <c>IngestBatchCommand</c>. Must be
/// initialised; default(BatchId) is rejected.
/// </param>
public sealed record class ProcessBatchCommand(BatchId BatchId)
{
    public BatchId BatchId { get; } = RequireValid(BatchId);

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
}
