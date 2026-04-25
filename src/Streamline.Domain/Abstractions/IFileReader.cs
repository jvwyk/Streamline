using Streamline.Core.ValueTypes;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Reads a leaf source file as a stream of <see cref="Record"/>
/// values. One implementation per format
/// (<c>DelimitedFileReader</c>, <c>XlsxFileReader</c>,
/// <c>JsonFileReader</c>, etc.). Per plan §11 decision 4: one
/// specialised library per format, each wrapped behind this
/// interface — no generic multi-format library.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single-pass.</b> The reader is single-pass per plan §5.3 and
/// AGENTS.md "common mistakes #6": calling
/// <see cref="ReadRowsAsync(FileMapping, string, CancellationToken)"/>
/// twice on the same file is allowed but each call re-opens the
/// file. Implementations must not internally re-open during one
/// enumeration.
/// </para>
/// <para>
/// <b>Stream-overload ownership rules</b>
/// (<see cref="GetHeadersAsync(FileMapping, Stream, string, CancellationToken)"/>,
/// <see cref="ReadRowsAsync(FileMapping, Stream, string, CancellationToken)"/>):
/// </para>
/// <list type="bullet">
///   <item>
///     <b>The caller owns the stream's lifetime.</b> The reader must
///     not call <see cref="Stream.Dispose()"/>,
///     <see cref="Stream.Close"/>, or <see cref="IAsyncDisposable.DisposeAsync"/>.
///     Container dispatchers (zip) re-use streams across multiple
///     reader calls and need them to remain open.
///   </item>
///   <item>
///     <b>The reader must not <see cref="Stream.Seek"/>.</b> Some
///     entry streams (e.g. zip entries) are not seekable; even
///     seekable streams may have positions the dispatcher carefully
///     established, and seeking would let a reader accidentally
///     re-read bytes the dispatcher already paid for.
///   </item>
/// </list>
/// </remarks>
public interface IFileReader
{
    /// <summary>
    /// The format token this reader handles, matching
    /// <see cref="FileMapping.Format"/> values
    /// (e.g. <c>"delimited"</c>, <c>"xlsx"</c>, <c>"json"</c>).
    /// Used by <see cref="IFileReaderRegistry"/> for resolution.
    /// </summary>
    string Format { get; }

    /// <summary>
    /// True when this reader can handle the given mapping + file
    /// path combination. Cheap check — typically a format-token
    /// match plus optional file-extension validation. Should not
    /// open the file.
    /// </summary>
    bool CanRead(FileMapping mapping, string filePath);

    /// <summary>
    /// Read the file's headers — the source-column names, in the
    /// order the file declares them. For headerless formats the
    /// reader synthesises positional names (<c>col_0</c>,
    /// <c>col_1</c>, ...).
    /// </summary>
    Task<IReadOnlyList<string>> GetHeadersAsync(
        FileMapping mapping,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the file's data rows as <see cref="Record"/> instances.
    /// Each record carries its <see cref="Record.SourceFileName"/>
    /// (the file path's leaf name) and
    /// <see cref="Record.SourceRowIndex"/> (zero-based row position).
    /// Values are emitted as raw strings; validation/typing is the
    /// orchestrator's responsibility (sub-phase 1g).
    /// </summary>
    IAsyncEnumerable<Record> ReadRowsAsync(
        FileMapping mapping,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream-based header read. Caller owns the stream; reader does
    /// not dispose or seek. Used by container dispatchers reading
    /// in-memory entries without extracting to disk. The
    /// <c>logicalName</c> takes the place of the file path for
    /// <see cref="Record.SourceFileName"/> on emitted records.
    /// </summary>
    Task<IReadOnlyList<string>> GetHeadersAsync(
        FileMapping mapping,
        Stream content,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream-based row read. Caller owns the stream; reader does
    /// not dispose or seek. See remarks on <see cref="IFileReader"/>
    /// for the full ownership contract.
    /// </summary>
    IAsyncEnumerable<Record> ReadRowsAsync(
        FileMapping mapping,
        Stream content,
        string logicalName,
        CancellationToken cancellationToken = default);
}
