using Streamline.Application.Tests.Fakes;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;

namespace Streamline.Application.Tests.Regression;

/// <summary>
/// Test helper for PB-4. Wraps an
/// <see cref="InMemoryDestinationAdapter"/> with a
/// <see cref="ITransactionScope"/> whose
/// <see cref="ITransactionScope.UpsertAsync"/> throws — every
/// other method delegates to the underlying scope. Used to drive
/// "destination upsert fails after quarantine writes have already
/// landed in staging" scenarios.
/// </summary>
/// <remarks>
/// Single-purpose per Q7 of the 1i plan. If a future test needs
/// other failure modes (commit fails, GetDistinctColumnValues
/// fails, savepoint fails to open), generalize to a configurable
/// FaultyDestinationAdapter at that point.
/// </remarks>
internal sealed class FailingOnUpsertDestinationAdapter : IDestinationAdapter
{
    private readonly InMemoryDestinationAdapter _inner;

    public FailingOnUpsertDestinationAdapter(InMemoryDestinationAdapter inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var scope = await _inner.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return new FailingScope(scope);
    }

    private sealed class FailingScope : ITransactionScope
    {
        private readonly ITransactionScope _inner;

        public FailingScope(ITransactionScope inner)
        {
            _inner = inner;
        }

        public Task<INestedScope> BeginNestedScopeAsync(string name, CancellationToken cancellationToken = default) =>
            _inner.BeginNestedScopeAsync(name, cancellationToken);

        public Task<UpsertOutcome> UpsertAsync(
            string schema,
            string table,
            SchemaDefinition definition,
            IReadOnlyList<Record> rows,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"FailingOnUpsertDestinationAdapter: simulated upsert failure for {schema}.{table}.");

        public Task<IReadOnlyCollection<string>> GetDistinctColumnValuesAsync(
            string schema, string table, string column, CancellationToken cancellationToken = default) =>
            _inner.GetDistinctColumnValuesAsync(schema, table, column, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            _inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            _inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
