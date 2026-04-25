namespace Streamline.Domain.Abstractions;

/// <summary>
/// Resolves <see cref="IFileReader"/> and <see cref="IFileDispatcher"/>
/// instances by format token. Lookup is synchronous — registration
/// happens once at DI setup and the registry holds references to
/// already-constructed instances.
/// </summary>
/// <remarks>
/// Returns null for unknown formats rather than throwing — the
/// orchestrator decides what to do with an unmapped format
/// (typically: emit <c>FILE_READ_FAILED</c> at <c>Critical</c>).
/// Throwing would force callers to wrap every lookup in try/catch.
/// </remarks>
public interface IFileReaderRegistry
{
    /// <summary>
    /// Resolve the leaf-file reader for <paramref name="format"/>,
    /// or null if no reader is registered.
    /// </summary>
    IFileReader? ResolveReader(string format);

    /// <summary>
    /// Resolve the container dispatcher for <paramref name="format"/>,
    /// or null if no dispatcher is registered.
    /// </summary>
    IFileDispatcher? ResolveDispatcher(string format);

    /// <summary>
    /// True when <paramref name="format"/> is a registered container
    /// format (i.e. a dispatcher exists). The orchestrator uses
    /// this to decide whether to read the file directly or dispatch
    /// it.
    /// </summary>
    bool IsContainer(string format);
}
