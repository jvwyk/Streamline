namespace Streamline.Domain.Abstractions;

/// <summary>
/// A savepoint within an <see cref="ITransactionScope"/>. Used to
/// isolate per-table writes so a single table's failure can be
/// rolled back without affecting other tables in the same batch.
/// </summary>
/// <remarks>
/// <b>Three terminal states</b>, mirroring <see cref="ITransactionScope"/>:
/// <list type="number">
///   <item>
///     <see cref="ReleaseAsync"/> was called — savepoint persists
///     into the outer transaction; <see cref="IAsyncDisposable.DisposeAsync"/>
///     is a no-op.
///   </item>
///   <item>
///     <see cref="RollbackAsync"/> was called — savepoint and its
///     writes are discarded; the outer transaction continues;
///     <see cref="IAsyncDisposable.DisposeAsync"/> is a no-op.
///   </item>
///   <item>
///     Neither was called — <see cref="IAsyncDisposable.DisposeAsync"/>
///     issues an implicit rollback of the savepoint, with a
///     warning-level log.
///   </item>
/// </list>
/// </remarks>
public interface INestedScope : IAsyncDisposable
{
    /// <summary>
    /// Release the savepoint. Writes made under this scope persist
    /// into the outer transaction (subject to that transaction's
    /// commit/rollback).
    /// </summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back to the savepoint. Writes made under this scope are
    /// discarded; the outer transaction is unaffected and may
    /// continue with other scopes.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
