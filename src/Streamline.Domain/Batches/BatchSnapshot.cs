using Streamline.Core.Enums;

namespace Streamline.Domain.Batches;

/// <summary>
/// Persisted state of a <see cref="Batch"/> at a point in time —
/// what <see cref="Streamline.Domain.Abstractions.IStagingRepository"/>'s
/// <c>GetBatchAsync</c> returns and what
/// <see cref="Batch.FromState"/> consumes to rehydrate an aggregate.
/// Used by <c>RetryBatchHandler</c> (to construct a Batch in the
/// persisted state before driving a retry transition) and by
/// <c>InspectBatchHandler</c> (to populate the inspection result).
/// </summary>
/// <param name="BatchId">Identity. Must be initialised.</param>
/// <param name="Source">
/// The free-form source description recorded when the batch was
/// originally opened. Non-blank.
/// </param>
/// <param name="Status">Current lifecycle state per <see cref="BatchStatus"/>.</param>
/// <param name="StartedAt">When the batch was first opened, UTC.</param>
/// <param name="CompletedAt">
/// When the batch reached a terminal state (Completed or Failed),
/// or null if still in flight. When non-null, must be at or after
/// <paramref name="StartedAt"/>.
/// </param>
public sealed record class BatchSnapshot(
    BatchId BatchId,
    string Source,
    BatchStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt)
{
    public BatchId BatchId { get; } = RequireValid(BatchId);
    public string Source { get; } = RequireNonBlank(Source, nameof(Source));
    public DateTimeOffset? CompletedAt { get; } = RequireCompletedAtNotBeforeStarted(StartedAt, CompletedAt);

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

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    private static DateTimeOffset? RequireCompletedAtNotBeforeStarted(
        DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        if (completedAt.HasValue && completedAt.Value < startedAt)
        {
            throw new ArgumentException(
                $"CompletedAt ({completedAt}) must be on or after StartedAt ({startedAt}).",
                nameof(completedAt));
        }
        return completedAt;
    }
}
