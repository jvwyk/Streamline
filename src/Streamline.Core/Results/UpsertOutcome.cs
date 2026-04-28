namespace Streamline.Core.Results;

/// <summary>
/// The result of one upsert call against the destination adapter in
/// replication mode. A successful upsert produces this; a failed
/// upsert is surfaced via an exception and a <c>UPSERT_FAILED</c>
/// observation (1b), not via this type.
/// </summary>
/// <param name="RowsInserted">New rows written to the destination.</param>
/// <param name="RowsUpdated">Existing rows whose payload changed.</param>
/// <param name="RowsUnchanged">
/// Existing rows whose checksum matched and therefore skipped the
/// write — part of the deduplication contract in the replication
/// upsert.
/// </param>
/// <param name="Duration">Wall-clock time the adapter spent on the upsert.</param>
public sealed record class UpsertOutcome(
    long RowsInserted,
    long RowsUpdated,
    long RowsUnchanged,
    TimeSpan Duration)
{
    public long RowsInserted { get; } = RequireNonNegative(RowsInserted, nameof(RowsInserted));
    public long RowsUpdated { get; } = RequireNonNegative(RowsUpdated, nameof(RowsUpdated));
    public long RowsUnchanged { get; } = RequireNonNegative(RowsUnchanged, nameof(RowsUnchanged));
    public TimeSpan Duration { get; } = RequireNonNegative(Duration, nameof(Duration));

    /// <summary>
    /// Total rows the adapter reported on — inserted + updated +
    /// unchanged. Rows quarantined before the upsert was attempted
    /// are not included here.
    /// </summary>
    public long TotalRows => RowsInserted + RowsUpdated + RowsUnchanged;

    public static UpsertOutcome Empty { get; } = new(0, 0, 0, TimeSpan.Zero);

    private static long RequireNonNegative(long value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        return value;
    }

    private static TimeSpan RequireNonNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, "Duration must not be negative.");
        }
        return value;
    }
}
