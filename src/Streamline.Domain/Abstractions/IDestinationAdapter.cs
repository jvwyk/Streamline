namespace Streamline.Domain.Abstractions;

/// <summary>
/// Factory for transactional sessions against a destination database.
/// Each <see cref="BeginTransactionAsync"/> call hands back a fresh
/// <see cref="ITransactionScope"/>; the scope owns the connection,
/// the transaction, and the savepoint stack until disposed.
/// </summary>
/// <remarks>
/// Replication-mode upserts and engine-driven writes go through this
/// adapter. Transformer writes (transform mode) may use it via
/// <c>BatchContext</c> or may use a direct connection — that's a
/// transformer implementation detail.
/// </remarks>
public interface IDestinationAdapter
{
    /// <summary>
    /// Begin a new transaction. The returned scope is the only
    /// way to issue writes; releasing it without
    /// <see cref="ITransactionScope.CommitAsync"/> rolls back per the
    /// scope's documented contract.
    /// </summary>
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
