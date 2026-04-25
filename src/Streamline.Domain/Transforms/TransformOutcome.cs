namespace Streamline.Domain.Transforms;

/// <summary>
/// The result a transformer reports back to the engine. The engine
/// records the outcome to <c>staging.transform_log</c> and emits the
/// corresponding <c>TRANSFORMER_*</c> observations; it does not
/// otherwise interpret the counts.
/// </summary>
/// <param name="RowsInserted">Destination rows newly written.</param>
/// <param name="RowsUpdated">Destination rows updated in place.</param>
/// <param name="RowsSkipped">
/// Rows the transformer chose not to write (no-op, e.g. unchanged
/// upsert). Distinct from <paramref name="RowsFailed"/>.
/// </param>
/// <param name="RowsFailed">
/// Rows the transformer attempted but could not write. Non-zero
/// produces a <c>TRANSFORMER_PARTIAL_FAILURE</c> observation; the
/// batch may still succeed if the consumer's policy allows it.
/// </param>
/// <param name="Duration">Wall-clock time the transformer ran.</param>
/// <param name="ErrorMessage">
/// Optional, populated when <paramref name="RowsFailed"/> is non-zero
/// or the transformer surfaced a problem worth reporting in
/// observation context.
/// </param>
public sealed record class TransformOutcome(
    long RowsInserted,
    long RowsUpdated,
    long RowsSkipped,
    long RowsFailed,
    TimeSpan Duration,
    string? ErrorMessage = null)
{
    public long RowsInserted { get; } = RequireNonNegative(RowsInserted, nameof(RowsInserted));
    public long RowsUpdated { get; } = RequireNonNegative(RowsUpdated, nameof(RowsUpdated));
    public long RowsSkipped { get; } = RequireNonNegative(RowsSkipped, nameof(RowsSkipped));
    public long RowsFailed { get; } = RequireNonNegative(RowsFailed, nameof(RowsFailed));
    public TimeSpan Duration { get; } = RequireNonNegativeDuration(Duration, nameof(Duration));

    public long TotalRowsAttempted =>
        RowsInserted + RowsUpdated + RowsSkipped + RowsFailed;

    private static long RequireNonNegative(long value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, name);
        return value;
    }

    private static TimeSpan RequireNonNegativeDuration(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, "Duration must not be negative.");
        }
        return value;
    }
}
