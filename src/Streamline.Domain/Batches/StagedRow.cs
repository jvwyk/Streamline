using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;

namespace Streamline.Domain.Batches;

/// <summary>
/// One row as it sits in <c>staging.incoming</c>: identified by its
/// auto-generated <see cref="IncomingId"/>, scoped to a
/// <see cref="Batches.BatchId"/> and target table, carrying the
/// validated <see cref="Streamline.Core.ValueTypes.Record"/> and its
/// current <see cref="RowStatus"/>. Returned by
/// <c>IStagingRepository.GetPendingRowsAsync</c>; consumed by the
/// processing orchestrator.
/// </summary>
/// <remarks>
/// The inner record's <c>SourceFileName</c> and <c>SourceRowIndex</c>
/// already carry source provenance; this type adds the staging-side
/// identifiers (<see cref="IncomingId"/>, <see cref="FileLogId"/>) and
/// the per-row state.
/// </remarks>
public sealed record class StagedRow
{
    public long IncomingId { get; }
    public BatchId BatchId { get; }
    public string TargetTable { get; }
    public Record Record { get; }
    public RowStatus Status { get; }
    public long FileLogId { get; }

    public StagedRow(
        long incomingId,
        BatchId batchId,
        string targetTable,
        Record record,
        RowStatus status,
        long fileLogId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(incomingId, nameof(incomingId));
        if (!batchId.IsValid)
        {
            throw new ArgumentException(
                "BatchId must be initialised; default(BatchId) is not a valid value.",
                nameof(batchId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable, nameof(targetTable));
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileLogId, nameof(fileLogId));

        IncomingId = incomingId;
        BatchId = batchId;
        TargetTable = targetTable;
        Record = record;
        Status = status;
        FileLogId = fileLogId;
    }
}
