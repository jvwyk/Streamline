using Streamline.Domain.Abstractions;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IFileReaderRegistry"/>. Resolves
/// <see cref="IFileReader"/> and <see cref="IFileDispatcher"/>
/// instances by format token. Tests register readers / dispatchers
/// up-front; the registry returns null for unknown formats per the
/// production contract (orchestrators turn null into
/// <c>FILE_READ_FAILED</c>, never throw at the registry boundary).
/// </summary>
/// <remarks>
/// Filling a gap from 1g — the orchestrator depends on
/// <see cref="IFileReaderRegistry"/> but no in-memory fake landed
/// in 1g's commits. Surfacing here, in 1h's plumbing commit, since
/// integration tests need it. Same shape as the other registries:
/// dictionary-keyed by format, public, simple <c>lock</c>.
/// </remarks>
public sealed class InMemoryFileReaderRegistry : IFileReaderRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, IFileReader> _readers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IFileDispatcher> _dispatchers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Register a reader for its <see cref="IFileReader.Format"/>.
    /// Replaces any prior binding for the same format.
    /// </summary>
    public void Register(IFileReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (_lock)
        {
            _readers[reader.Format] = reader;
        }
    }

    /// <summary>
    /// Register a dispatcher for its <see cref="IFileDispatcher.Format"/>.
    /// Replaces any prior binding for the same format.
    /// </summary>
    public void Register(IFileDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        lock (_lock)
        {
            _dispatchers[dispatcher.Format] = dispatcher;
        }
    }

    public IFileReader? ResolveReader(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        lock (_lock)
        {
            return _readers.TryGetValue(format, out var reader) ? reader : null;
        }
    }

    public IFileDispatcher? ResolveDispatcher(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        lock (_lock)
        {
            return _dispatchers.TryGetValue(format, out var dispatcher) ? dispatcher : null;
        }
    }

    public bool IsContainer(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        lock (_lock)
        {
            return _dispatchers.ContainsKey(format);
        }
    }
}
