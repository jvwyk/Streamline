using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IFileMappingRepository"/>. Stores
/// mappings in insertion order; <see cref="ResolveAsync"/> returns
/// the first match by <see cref="FileMapping.Matches"/>, matching
/// the contract's "first registered match wins" rule.
/// </summary>
/// <remarks>
/// <b>Identity for upsert.</b> The contract identifies mappings by
/// (FilenamePattern, TargetTable). The fake mirrors that —
/// upserting a mapping with the same pattern + table replaces the
/// existing entry; same pattern + different table is a distinct
/// entry. Order of insertion is preserved; replacing an entry
/// keeps its original position.
/// </remarks>
public sealed class InMemoryFileMappingRepository : IFileMappingRepository
{
    private readonly object _lock = new();
    private readonly List<FileMapping> _mappings = [];

    public Task<FileMapping?> ResolveAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(fileName);

        lock (_lock)
        {
            foreach (var mapping in _mappings)
            {
                if (mapping.Matches(fileName))
                {
                    return Task.FromResult<FileMapping?>(mapping);
                }
            }
            return Task.FromResult<FileMapping?>(null);
        }
    }

    public Task<IReadOnlyList<FileMapping>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<FileMapping>>(_mappings.ToArray());
        }
    }

    public Task UpsertAsync(FileMapping mapping, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(mapping);

        lock (_lock)
        {
            for (var i = 0; i < _mappings.Count; i++)
            {
                if (_mappings[i].FilenamePattern == mapping.FilenamePattern
                    && _mappings[i].TargetTable == mapping.TargetTable)
                {
                    _mappings[i] = mapping;
                    return Task.CompletedTask;
                }
            }
            _mappings.Add(mapping);
        }
        return Task.CompletedTask;
    }
}
