namespace Streamline.Core.Results;

/// <summary>
/// Per-table processing outcome aggregated by <see cref="ProcessingResult"/>.
/// One record per target table the batch touched during processing.
/// </summary>
/// <remarks>
/// <para>
/// This type carries only counts and timing. Replication-specific
/// detail (an <see cref="UpsertOutcome"/> per table) and
/// transform-specific detail (a <c>TransformOutcome</c>, which lives
/// in Domain) are composed at the Application layer into richer
/// reports — Core stays narrow so it can be referenced from any
/// layer without pulling in Domain or Application types.
/// </para>
/// <para>
/// Invariant: <c>RowsCommitted + RowsRolledBack + RowsQuarantined</c>
/// matches the number of rows staged for this table. Mismatch is a
/// reconciliation signal.
/// </para>
/// </remarks>
public sealed record class TableProcessingOutcome(
    string TableName,
    long RowsCommitted,
    long RowsRolledBack,
    long RowsQuarantined,
    TimeSpan Duration)
{
    public string TableName { get; } = RequireNonBlank(TableName, nameof(TableName));
    public long RowsCommitted { get; } = RequireNonNegative(RowsCommitted, nameof(RowsCommitted));
    public long RowsRolledBack { get; } = RequireNonNegative(RowsRolledBack, nameof(RowsRolledBack));
    public long RowsQuarantined { get; } = RequireNonNegative(RowsQuarantined, nameof(RowsQuarantined));
    public TimeSpan Duration { get; } = RequireNonNegative(Duration, nameof(Duration));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

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
