namespace Streamline.Application.Transactions;

/// <summary>
/// Transactional intent marker used by Application-layer code to
/// express "these operations should commit or roll back together".
/// Phase 1 (in-memory fakes) implements this as a no-op; Phase 2's
/// Postgres implementation wraps the deferred transaction across
/// the staging connection and the destination connection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Application-layer abstraction?</b> Handlers and
/// orchestrators don't know about Postgres transactions, savepoints,
/// or multi-connection coordination — those are infrastructure
/// concerns. They do know "these writes belong together". This
/// interface gives them a way to say so without committing to
/// transaction mechanics that vary between in-memory and real
/// backends.
/// </para>
/// <para>
/// <b>Lifecycle.</b>
/// <list type="bullet">
///   <item><c>BeginAsync</c> opens a unit. The returned task
///     completes once underlying state (transactions, locks) is
///     ready for writes.</item>
///   <item><c>CommitAsync</c> finalises the unit. Subsequent calls
///     on the same instance are invalid (implementations throw
///     <see cref="InvalidOperationException"/>).</item>
///   <item><c>RollbackAsync</c> discards the unit's writes.
///     Same single-use rule.</item>
///   <item><c>DisposeAsync</c> implicit rollback if neither commit
///     nor rollback was called — same idiom as
///     <c>ITransactionScope</c>. Implementations log the implicit
///     rollback at warning level.</item>
/// </list>
/// </para>
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Open the unit. Single-use; subsequent calls on the same
    /// instance throw.
    /// </summary>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit the unit's writes. Single-use; subsequent calls
    /// (including <see cref="RollbackAsync"/>) throw.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back the unit's writes. Single-use; subsequent calls
    /// (including <see cref="CommitAsync"/>) throw.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
