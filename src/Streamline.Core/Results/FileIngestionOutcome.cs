namespace Streamline.Core.Results;

/// <summary>
/// Per-file ingestion outcome aggregated by <see cref="IngestionResult"/>.
/// One record per source file read into staging during an IngestBatch
/// call, regardless of whether the file was leaf-read or dispatched
/// from a container.
/// </summary>
/// <param name="FileName">Name of the file as presented to the reader.</param>
/// <param name="RowsRead">Rows produced by the reader, including those that later failed validation.</param>
/// <param name="RowsStaged">Rows successfully written to <c>staging.incoming</c>.</param>
/// <param name="RowsQuarantined">Rows that failed validation and landed in <c>staging.quarantine</c>.</param>
/// <param name="Duration">Wall-clock time to ingest this file.</param>
/// <remarks>
/// Invariant: <c>RowsRead == RowsStaged + RowsQuarantined</c>. Any mismatch is a
/// reconciliation signal (<c>RECONCILIATION_MISMATCH</c> in 1b) and
/// is enforced by the orchestrator, not by this value type — the
/// record is a passive report.
/// </remarks>
public sealed record class FileIngestionOutcome(
    string FileName,
    long RowsRead,
    long RowsStaged,
    long RowsQuarantined,
    TimeSpan Duration)
{
    public string FileName { get; } = RequireNonBlank(FileName, nameof(FileName));
    public long RowsRead { get; } = RequireNonNegative(RowsRead, nameof(RowsRead));
    public long RowsStaged { get; } = RequireNonNegative(RowsStaged, nameof(RowsStaged));
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
