using Streamline.Domain.Registry;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// Read/write access to the schema registry — the catalog of
/// <see cref="RegistryEntry"/> rows that drive the engine. First-party
/// implementations: Postgres-backed, YAML-backed, and a YAML+Postgres
/// hybrid (Phase 2 + 2b). Consumer-implemented backends are permitted
/// but unsupported per plan §11 decision 1.
/// </summary>
public interface IRegistryRepository
{
    /// <summary>
    /// Fetch the active <see cref="RegistryEntry"/> for a logical
    /// table name. "Active" means <see cref="RegistryEntry.IsActive"/>
    /// is true and the current date falls within
    /// <see cref="RegistryEntry.ValidFrom"/> /
    /// <see cref="RegistryEntry.ValidTo"/>. Returns null if no
    /// active entry exists for the name.
    /// </summary>
    Task<RegistryEntry?> GetActiveAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch every active <see cref="RegistryEntry"/>. Order is not
    /// part of the contract — callers that need topological order
    /// should use <see cref="GetLoadOrderAsync"/>.
    /// </summary>
    Task<IReadOnlyList<RegistryEntry>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or update an entry. Implementations match by
    /// <see cref="RegistryEntry.TableName"/> +
    /// <see cref="RegistryEntry.ValidFrom"/>; updates affecting an
    /// active entry should propagate to the cached lookup
    /// (Phase 2b's hybrid backend handles this explicitly).
    /// </summary>
    Task UpsertAsync(RegistryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return active table names in dependency order — every parent
    /// before any child that <c>DependsOn</c> it. Implementations may
    /// compute this in their backend (Postgres can use a recursive
    /// CTE) or by sorting <see cref="GetAllActiveAsync"/> results in
    /// memory; the contract specifies the order, not the algorithm.
    /// Cycles surface as <c>InvalidOperationException</c>.
    /// </summary>
    Task<IReadOnlyList<string>> GetLoadOrderAsync(CancellationToken cancellationToken = default);
}
