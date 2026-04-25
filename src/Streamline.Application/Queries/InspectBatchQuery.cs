using Streamline.Core.Observations;
using Streamline.Domain.Batches;

namespace Streamline.Application.Queries;

/// <summary>
/// Read-only request for a structured summary of one batch.
/// Combines per-file ingestion outcomes, per-table processing
/// outcomes, observation counts (grouped by severity), and any
/// reconciliation summary into a single
/// <see cref="BatchInspectionResult"/>.
/// </summary>
/// <param name="BatchId">The batch to inspect. Must be initialised.</param>
/// <param name="MinSeverity">
/// When set, the response includes only observations at or above
/// this severity. When null, every observation is included.
/// Useful for operators who want a focused view (Warning+ or
/// Error+ only).
/// </param>
public sealed record class InspectBatchQuery(BatchId BatchId, ObservationSeverity? MinSeverity = null)
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
