namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Emitted when the batch first transitions out of
/// <see cref="Streamline.Core.Enums.BatchStatus.Created"/> into
/// <see cref="Streamline.Core.Enums.BatchStatus.Ingesting"/> — the
/// moment ingestion actually begins.
/// </summary>
/// <param name="BatchId">The batch.</param>
/// <param name="OccurredAt">When the transition happened, UTC.</param>
/// <param name="Source">
/// The source description originally passed to
/// <c>IStagingRepository.StartBatchAsync</c> — typically a
/// directory path or invocation-argument summary.
/// </param>
public sealed record class BatchStartedEvent(
    BatchId BatchId,
    DateTimeOffset OccurredAt,
    string Source) : IDomainEvent
{
    public string Source { get; } = RequireNonBlank(Source, nameof(Source));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
