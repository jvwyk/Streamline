using Streamline.Core.Results;
using Streamline.Core.ValueTypes;

namespace Streamline.Domain.Abstractions;

/// <summary>
/// One transactional session against a destination database. Returned
/// by <see cref="IDestinationAdapter.BeginTransactionAsync"/>. The
/// scope owns the connection, the transaction, and the savepoint
/// stack until <see cref="IAsyncDisposable.DisposeAsync"/> runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three terminal states.</b> A scope is finalised in exactly one
/// of three ways:
/// </para>
/// <list type="number">
///   <item>
///     <c>CommitAsync</c> was called — <c>DisposeAsync</c> is a no-op.
///   </item>
///   <item>
///     <c>RollbackAsync</c> was called — <c>DisposeAsync</c> is a no-op.
///   </item>
///   <item>
///     Neither was called — <c>DisposeAsync</c> issues an <em>implicit
///     rollback</em>. This matches the Npgsql and EF Core idioms; it is
///     intentional, not silent. Implementations <b>must</b> log the
///     implicit rollback at warning level so unintended cases (forgotten
///     commit, exception escape) are visible in operator logs.
///   </item>
/// </list>
/// <para>
/// <b>Nested scopes.</b> <see cref="BeginNestedScopeAsync"/> opens a
/// per-table savepoint. The outer scope's commit / rollback acts on
/// every nested scope that hasn't been individually released or
/// rolled back. Nested-scope failure (rolled back via
/// <see cref="INestedScope.RollbackAsync"/>) does not invalidate the
/// outer scope; the orchestrator can continue with other tables.
/// </para>
/// </remarks>
public interface ITransactionScope : IAsyncDisposable
{
    /// <summary>
    /// Open a nested scope (a savepoint) under this transaction.
    /// Used by the orchestrator to isolate per-table writes so a
    /// single table's failure can be rolled back without affecting
    /// other tables in the same batch.
    /// </summary>
    Task<INestedScope> BeginNestedScopeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert a batch of records into a destination table. The
    /// schema describes column definitions for type coercion; rows
    /// must already be validated (typed values per
    /// <see cref="Record.GetTyped{T}"/>). Returns counts of rows
    /// inserted, updated, and unchanged.
    /// </summary>
    Task<UpsertOutcome> UpsertAsync(
        string schema,
        string table,
        SchemaDefinition definition,
        IReadOnlyList<Record> rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read distinct values of a single column from a destination
    /// table — used to populate the FK cache for FK validation.
    /// Returns an unordered set; callers that need ordering must
    /// sort.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetDistinctColumnValuesAsync(
        string schema,
        string table,
        string column,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit the transaction. Subsequent calls (including
    /// <see cref="RollbackAsync"/>) throw; <see cref="IAsyncDisposable.DisposeAsync"/>
    /// becomes a no-op.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back the transaction. Subsequent calls (including
    /// <see cref="CommitAsync"/>) throw; <see cref="IAsyncDisposable.DisposeAsync"/>
    /// becomes a no-op.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
