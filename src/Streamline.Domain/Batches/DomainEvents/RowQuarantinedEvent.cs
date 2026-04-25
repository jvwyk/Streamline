namespace Streamline.Domain.Batches.DomainEvents;

/// <summary>
/// Emitted when a row transitions to
/// <see cref="Streamline.Core.Enums.RowStatus.Quarantined"/>. The
/// transition can be from <see cref="Streamline.Core.Enums.RowStatus.Pending"/>
/// (validation failure during ingestion, T5) or from
/// <see cref="Streamline.Core.Enums.RowStatus.Processing"/>
/// (validation or DB error during processing, T6) — both produce
/// this event.
/// </summary>
/// <param name="BatchId">The batch.</param>
/// <param name="OccurredAt">When the quarantine occurred, UTC.</param>
/// <param name="IncomingId">The staging row id.</param>
/// <param name="TargetTable">The target table the row was bound for.</param>
/// <param name="Code">
/// Stable observation code for the failure
/// (<c>MISSING_REQUIRED</c>, <c>INVALID_TYPE</c>, etc.). Must match
/// a value in <c>Streamline.Core.Observations.ObservationCodes</c>;
/// the aggregate does not cross-check (per the catalog's contract).
/// </param>
/// <param name="Message">Human-readable description of the failure.</param>
public sealed record class RowQuarantinedEvent(
    BatchId BatchId,
    DateTimeOffset OccurredAt,
    long IncomingId,
    string TargetTable,
    string Code,
    string Message) : IDomainEvent
{
    public long IncomingId { get; } = RequirePositive(IncomingId);
    public string TargetTable { get; } = RequireNonBlank(TargetTable, nameof(TargetTable));
    public string Code { get; } = RequireNonBlank(Code, nameof(Code));
    public string Message { get; } = RequireNonBlank(Message, nameof(Message));

    private static long RequirePositive(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value;
    }

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
