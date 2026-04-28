using Streamline.Domain.Batches;

namespace Streamline.Application.Commands;

/// <summary>
/// Request to retry a previously-failed batch. The handler resets every
/// <see cref="Streamline.Core.Enums.RowStatus.RolledBack"/> row to
/// <see cref="Streamline.Core.Enums.RowStatus.Pending"/>, transitions
/// the batch from <see cref="Streamline.Core.Enums.BatchStatus.Failed"/>
/// (or already-<see cref="Streamline.Core.Enums.BatchStatus.Processing"/>)
/// back to <see cref="Streamline.Core.Enums.BatchStatus.Processing"/>,
/// then dispatches a <see cref="ProcessBatchCommand"/> internally to
/// re-run processing.
/// </summary>
/// <remarks>
/// Quarantined rows are NOT touched by retry — quarantine resolution
/// is a separate manual operation. See <c>IStagingRepository.ResetForRetryAsync</c>
/// for the row-level contract.
/// </remarks>
/// <param name="BatchId">
/// The batch to retry. Must be initialised; default(BatchId) rejected.
/// </param>
public sealed record class RetryBatchCommand(BatchId BatchId)
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
