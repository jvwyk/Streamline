using System.Diagnostics;
using System.Runtime.CompilerServices;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// Test <see cref="IFileReader"/>. Yields a configured header set
/// and record sequence; supports common test scenarios via factory
/// methods (<see cref="WithRecords"/>, <see cref="ThatThrowsOn"/>,
/// <see cref="HangsAfter"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Header / record consistency.</b> The constructor takes both
/// headers and records. <see cref="Debug.Assert"/> validates that
/// every record's keys are a subset of the declared headers; tests
/// that intentionally produce header/record mismatches go through
/// <see cref="WithMismatchedRecords"/> which skips the assert.
/// </para>
/// <para>
/// <b>Format token.</b> Defaults to <c>"delimited"</c> for
/// convenience; tests targeting a specific format pass it
/// explicitly via the constructor parameter.
/// </para>
/// <para>
/// <b>Single-pass per the contract.</b> Each enumeration starts
/// from the beginning of the configured record list. The reader
/// doesn't open files; the file path argument is recorded for
/// observation context (via <see cref="Record.SourceFileName"/>)
/// but not otherwise interpreted.
/// </para>
/// </remarks>
public sealed class FakeFileReader : IFileReader
{
    private readonly IReadOnlyList<string> _headers;
    private readonly IReadOnlyList<Record> _rows;
    private readonly int? _throwAtRowIndex;
    private readonly int? _hangAfterRowIndex;

    public string Format { get; }

    private FakeFileReader(
        string format,
        IReadOnlyList<string> headers,
        IReadOnlyList<Record> rows,
        int? throwAtRowIndex = null,
        int? hangAfterRowIndex = null,
        bool validate = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        Format = format;
        _headers = headers;
        _rows = rows;
        _throwAtRowIndex = throwAtRowIndex;
        _hangAfterRowIndex = hangAfterRowIndex;

        if (validate)
        {
            ValidateRecordKeys(headers, rows);
        }
    }

    /// <summary>
    /// Standard happy-path reader: yields every row in order. Validates
    /// that every record's keys are a subset of the declared headers.
    /// </summary>
    public static FakeFileReader WithRecords(
        IReadOnlyList<string> headers,
        IReadOnlyList<Record> rows,
        string format = "delimited") =>
        new(format, headers, rows);

    /// <summary>
    /// Yields rows up to (but not including) <paramref name="throwAtRowIndex"/>,
    /// then throws an <see cref="IOException"/> simulating a mid-read
    /// failure. Used to verify the orchestrator handles partial-read
    /// failure correctly.
    /// </summary>
    public static FakeFileReader ThatThrowsOn(
        IReadOnlyList<string> headers,
        IReadOnlyList<Record> rows,
        int throwAtRowIndex,
        string format = "delimited") =>
        new(format, headers, rows, throwAtRowIndex: throwAtRowIndex);

    /// <summary>
    /// Yields rows up to and including <paramref name="hangAfterRowIndex"/>,
    /// then awaits indefinitely (honoring the cancellation token).
    /// Used to verify cancellation propagates from a hung file
    /// read.
    /// </summary>
    public static FakeFileReader HangsAfter(
        IReadOnlyList<string> headers,
        IReadOnlyList<Record> rows,
        int hangAfterRowIndex,
        string format = "delimited") =>
        new(format, headers, rows, hangAfterRowIndex: hangAfterRowIndex);

    /// <summary>
    /// Negative-test reader: skips the headers/records consistency
    /// validation. Used by tests that exercise the orchestrator's
    /// drift detection or validation paths with intentionally
    /// inconsistent input.
    /// </summary>
    public static FakeFileReader WithMismatchedRecords(
        IReadOnlyList<string> headers,
        IReadOnlyList<Record> rows,
        string format = "delimited") =>
        new(format, headers, rows, validate: false);

    public bool CanRead(FileMapping mapping, string filePath) =>
        string.Equals(mapping?.Format, Format, StringComparison.Ordinal);

    public Task<IReadOnlyList<string>> GetHeadersAsync(
        FileMapping mapping, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_headers);
    }

    public IAsyncEnumerable<Record> ReadRowsAsync(
        FileMapping mapping, string filePath, CancellationToken cancellationToken = default) =>
        EnumerateRowsAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetHeadersAsync(
        FileMapping mapping, Stream content, string logicalName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_headers);
    }

    public IAsyncEnumerable<Record> ReadRowsAsync(
        FileMapping mapping, Stream content, string logicalName, CancellationToken cancellationToken = default) =>
        EnumerateRowsAsync(cancellationToken);

    private async IAsyncEnumerable<Record> EnumerateRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_throwAtRowIndex == i)
            {
                throw new IOException(
                    $"FakeFileReader simulated read failure at row index {i}.");
            }
            yield return _rows[i];
            if (_hangAfterRowIndex == i)
            {
                // Honor cancellation; otherwise wait indefinitely.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            await Task.Yield();
        }
    }

    private static void ValidateRecordKeys(
        IReadOnlyList<string> headers, IReadOnlyList<Record> rows)
    {
        var headerSet = new HashSet<string>(headers, StringComparer.Ordinal);
        foreach (var record in rows)
        {
            foreach (var key in record.Values.Keys)
            {
                Debug.Assert(
                    headerSet.Contains(key),
                    $"FakeFileReader record key '{key}' is not in declared headers; " +
                    "use WithMismatchedRecords for negative-test scenarios.");
            }
        }
    }
}
