using Streamline.Domain.Abstractions;
using Streamline.Domain.Registry;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IRegistryRepository"/>. Stores
/// <see cref="RegistryEntry"/> values keyed by table name, supports
/// upsert, "active right now" lookup (date-windowed), and
/// dependency-ordered enumeration via topological sort.
/// </summary>
/// <remarks>
/// <para>
/// <b>GetLoadOrderAsync uses Kahn's algorithm</b> on the active
/// entries' <see cref="RegistryEntry.DependsOn"/> edges. Cycles
/// throw <see cref="InvalidOperationException"/> per the contract.
/// Faithful sorting matters: 1h tests against this fake must catch
/// dependency-order bugs that would also fail against Postgres.
/// </para>
/// <para>
/// <b>"Active" lookup matches the contract.</b> An entry is
/// considered active when <see cref="RegistryEntry.IsActive"/> is
/// true AND today's date (UTC) falls within
/// <see cref="RegistryEntry.ValidFrom"/> /
/// <see cref="RegistryEntry.ValidTo"/>. The fake reads
/// <see cref="DateTimeOffset.UtcNow"/>; tests that need controlled
/// time can construct entries with explicit ValidFrom/ValidTo
/// windows that don't overlap "now."
/// </para>
/// </remarks>
public sealed class InMemoryRegistryRepository : IRegistryRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RegistryEntry> _entries =
        new(StringComparer.Ordinal);

    public Task<RegistryEntry?> GetActiveAsync(string tableName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        lock (_lock)
        {
            if (!_entries.TryGetValue(tableName, out var entry) || !IsActiveNow(entry))
            {
                return Task.FromResult<RegistryEntry?>(null);
            }
            return Task.FromResult<RegistryEntry?>(entry);
        }
    }

    public Task<IReadOnlyList<RegistryEntry>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var active = _entries.Values.Where(IsActiveNow).ToList();
            return Task.FromResult<IReadOnlyList<RegistryEntry>>(active);
        }
    }

    public Task UpsertAsync(RegistryEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            _entries[entry.TableName] = entry;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetLoadOrderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            var active = _entries.Values.Where(IsActiveNow).ToList();
            var ordered = TopologicalSort(active);
            return Task.FromResult<IReadOnlyList<string>>(ordered);
        }
    }

    private static bool IsActiveNow(RegistryEntry entry)
    {
        if (!entry.IsActive)
        {
            return false;
        }
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        if (today < entry.ValidFrom)
        {
            return false;
        }
        if (entry.ValidTo.HasValue && today > entry.ValidTo.Value)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Kahn's algorithm. Repeatedly emit the entry with no
    /// remaining dependencies on entries still in the working set;
    /// remove its outgoing edges; repeat until the working set is
    /// empty (success) or no edge-free node exists (cycle).
    /// </summary>
    private static List<string> TopologicalSort(List<RegistryEntry> active)
    {
        var byName = active.ToDictionary(e => e.TableName, StringComparer.Ordinal);
        // For each entry, count how many of its DependsOn references
        // point to entries that exist in the active set. Dependencies
        // outside the active set are silently ignored — they can't
        // contribute to the topo order anyway.
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in active)
        {
            inDegree[entry.TableName] = entry.DependsOn.Count(d => byName.ContainsKey(d));
        }

        // Stable initial order: alphabetical by table name so two
        // independent entries always emit in the same order
        // regardless of insertion sequence.
        var ready = new SortedSet<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key),
            StringComparer.Ordinal);
        var output = new List<string>(active.Count);

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            output.Add(next);

            // Remove next's outgoing edges. An entry E depends on
            // next when next ∈ E.DependsOn; decrement E's in-degree
            // and add it to ready if it just hit zero.
            foreach (var entry in active)
            {
                if (entry.DependsOn.Contains(next))
                {
                    inDegree[entry.TableName]--;
                    if (inDegree[entry.TableName] == 0)
                    {
                        ready.Add(entry.TableName);
                    }
                }
            }
        }

        if (output.Count != active.Count)
        {
            var unresolved = inDegree
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .OrderBy(n => n, StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Registry contains a dependency cycle involving: {string.Join(", ", unresolved)}.");
        }

        return output;
    }
}
