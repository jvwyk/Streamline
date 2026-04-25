using Streamline.Core.ValueTypes;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Read/write access to file-mapping entries — which filename
/// patterns route to which target tables and reader formats.
/// Separate from <see cref="IRegistryRepository"/> so registry and
/// file-mapping storage can be chosen independently at DI
/// registration (e.g. registry from YAML, mappings from Postgres).
/// </summary>
public interface IFileMappingRepository
{
    /// <summary>
    /// Resolve a filename to its first matching <see cref="FileMapping"/>.
    /// Returns null if no mapping matches; the orchestrator decides
    /// what to do (typically: skip with an observation, or fail per
    /// the source directory's policy). When multiple mappings match,
    /// implementations return the first registered match (load
    /// order); ambiguity is the registry author's responsibility to
    /// resolve.
    /// </summary>
    Task<FileMapping?> ResolveAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch every active mapping. Used by the orchestrator at batch
    /// start to decide which files in a source directory are
    /// recognised.
    /// </summary>
    Task<IReadOnlyList<FileMapping>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or update a mapping. Implementations match by
    /// (<see cref="FileMapping.FilenamePattern"/>,
    /// <see cref="FileMapping.TargetTable"/>) — the same pattern
    /// targeting different tables produces distinct mappings.
    /// </summary>
    Task UpsertAsync(FileMapping mapping, CancellationToken cancellationToken = default);
}
