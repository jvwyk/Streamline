using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Fakes;

/// <summary>
/// In-memory fake of <see cref="IDestinationAdapter"/>. Backs every
/// <see cref="ITransactionScope"/> it returns with a shared
/// committed store; pending writes live on the scope until commit.
/// Supports nested scopes (savepoints), upsert semantics
/// (insert / update / unchanged), and read-your-writes within a
/// scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Faithfulness boundary (per Q2 of 1g).</b> Observably
/// equivalent to a real Postgres adapter for the operations the
/// orchestrator performs:
/// <list type="bullet">
///   <item><see cref="ITransactionScope.UpsertAsync"/> classifies each
///     row as inserted / updated / unchanged using the read-your-
///     writes view (committed store ∪ scope's pending writes).</item>
///   <item><see cref="ITransactionScope.GetDistinctColumnValuesAsync"/>
///     reads from the same view.</item>
///   <item><see cref="INestedScope"/> writes go directly into the
///     outer scope's pending-writes structure during the nested
///     scope's lifetime; rollback restores the outer scope to a
///     snapshot taken at <c>BeginNestedScopeAsync</c> time;
///     release is a no-op (writes are already in the outer scope).</item>
///   <item>FK enforcement timing (deferred vs immediate) is NOT
///     modeled — Phase 2's contract-parity tests cover that
///     against real Postgres.</item>
/// </list>
/// </para>
/// <para>
/// <b>Thread safety.</b> The committed store is guarded by a single
/// lock; per-scope pending state is scope-local and assumed
/// single-threaded per scope (the orchestrator drives one scope on
/// one thread at a time).
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "Test fake. Implicit-rollback warnings fire at most once per scope; the " +
                    "LoggerMessage source-generator pattern's perf benefit only matters in tight " +
                    "hot loops.")]
public sealed class InMemoryDestinationAdapter : IDestinationAdapter
{
    private readonly object _committedLock = new();
    private readonly Dictionary<TableKey, Dictionary<RowKey, Record>> _committed = new();
    private readonly ILogger<InMemoryDestinationAdapter> _logger;

    public InMemoryDestinationAdapter(ILogger<InMemoryDestinationAdapter>? logger = null)
    {
        _logger = logger ?? NullLogger<InMemoryDestinationAdapter>.Instance;
    }

    public Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITransactionScope>(new InMemoryTransactionScope(this, _logger));
    }

    /// <summary>
    /// Test-only: snapshot of the committed store for a target
    /// schema and table, keyed by primary key tuple. Tests use this
    /// to assert "after batch commit, these rows are in the
    /// destination."
    /// </summary>
    public ImmutableDictionary<ImmutableArray<object?>, Record> ForTestingOnly_GetCommitted(
        string schema, string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        lock (_committedLock)
        {
            if (!_committed.TryGetValue(new TableKey(schema, table), out var rows))
            {
                return ImmutableDictionary<ImmutableArray<object?>, Record>.Empty;
            }
            return rows.ToImmutableDictionary(
                kvp => kvp.Key.Values,
                kvp => kvp.Value,
                ImmutableArrayEqualityComparer.Instance);
        }
    }

    /// <summary>
    /// Test-only: seeds the committed store with rows under a target
    /// schema and table. Used by tests that want to set up a
    /// pre-existing destination state (e.g., to verify upsert's
    /// update vs. insert classification).
    /// </summary>
    public void ForTestingOnly_SeedCommitted(
        string schema, string table, SchemaDefinition definition, IEnumerable<Record> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(rows);

        var pk = definition.PrimaryKey;
        if (pk.Count == 0)
        {
            throw new InvalidOperationException(
                "ForTestingOnly_SeedCommitted requires a schema with at least one primary-key column.");
        }

        lock (_committedLock)
        {
            var key = new TableKey(schema, table);
            if (!_committed.TryGetValue(key, out var store))
            {
                store = new Dictionary<RowKey, Record>();
                _committed[key] = store;
            }
            foreach (var record in rows)
            {
                store[ExtractKey(record, pk)] = record;
            }
        }
    }

    private void CommitPending(IReadOnlyDictionary<TableKey, Dictionary<RowKey, Record>> pending)
    {
        lock (_committedLock)
        {
            foreach (var (table, rows) in pending)
            {
                if (!_committed.TryGetValue(table, out var store))
                {
                    store = new Dictionary<RowKey, Record>();
                    _committed[table] = store;
                }
                foreach (var (key, record) in rows)
                {
                    store[key] = record;
                }
            }
        }
    }

    private Dictionary<TableKey, Dictionary<RowKey, Record>> SnapshotCommitted()
    {
        lock (_committedLock)
        {
            return _committed.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<RowKey, Record>(kvp.Value));
        }
    }

    internal static RowKey ExtractKey(Record record, IReadOnlyList<ColumnDefinition> primaryKey)
    {
        var values = ImmutableArray.CreateBuilder<object?>(primaryKey.Count);
        foreach (var column in primaryKey)
        {
            record.Values.TryGetValue(column.Name, out var value);
            values.Add(value);
        }
        return new RowKey(values.ToImmutable());
    }

    internal static bool ValuesEqual(Record a, Record b)
    {
        if (a.Values.Count != b.Values.Count)
        {
            return false;
        }
        foreach (var (key, valueA) in a.Values)
        {
            if (!b.Values.TryGetValue(key, out var valueB) || !Equals(valueA, valueB))
            {
                return false;
            }
        }
        return true;
    }

    internal readonly record struct TableKey(string Schema, string Table);

    internal sealed record class RowKey(ImmutableArray<object?> Values)
    {
        public bool Equals(RowKey? other) =>
            other is not null && Values.SequenceEqual(other.Values);

        public override int GetHashCode()
        {
            var hash = default(HashCode);
            foreach (var v in Values)
            {
                hash.Add(v);
            }
            return hash.ToHashCode();
        }
    }

    private sealed class ImmutableArrayEqualityComparer : IEqualityComparer<ImmutableArray<object?>>
    {
        public static ImmutableArrayEqualityComparer Instance { get; } = new();

        public bool Equals(ImmutableArray<object?> x, ImmutableArray<object?> y) => x.SequenceEqual(y);

        public int GetHashCode(ImmutableArray<object?> obj)
        {
            var hash = default(HashCode);
            foreach (var v in obj)
            {
                hash.Add(v);
            }
            return hash.ToHashCode();
        }
    }

    private sealed class InMemoryTransactionScope : ITransactionScope
    {
        private readonly InMemoryDestinationAdapter _adapter;
        private readonly ILogger _logger;
        private readonly Dictionary<TableKey, Dictionary<RowKey, Record>> _pending = new();
        private TerminalState _terminal = TerminalState.None;

        public InMemoryTransactionScope(InMemoryDestinationAdapter adapter, ILogger logger)
        {
            _adapter = adapter;
            _logger = logger;
        }

        public Task<INestedScope> BeginNestedScopeAsync(string name, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            EnsureActive();
            // Snapshot of the outer scope's pending writes so a
            // rollback can identify exactly what to undo. Writes
            // made under the nested scope go directly into _pending.
            var snapshot = ClonePending();
            return Task.FromResult<INestedScope>(new InMemoryNestedScope(this, snapshot, name, _logger));
        }

        public Task<UpsertOutcome> UpsertAsync(
            string schema,
            string table,
            SchemaDefinition definition,
            IReadOnlyList<Record> rows,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);
            ArgumentException.ThrowIfNullOrWhiteSpace(table);
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(rows);
            EnsureActive();

            var pk = definition.PrimaryKey;
            if (pk.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Schema for {schema}.{table} has no primary key; cannot determine row identity.");
            }

            var key = new TableKey(schema, table);
            long inserted = 0, updated = 0, unchanged = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Read-your-writes: existing-row lookup pulls from the
            // committed store first, then is overlaid by this scope's
            // pending writes. Same scope's prior upserts of the same
            // PK reflect Update / Unchanged, not a second Insert.
            var existing = SnapshotReadView(key);

            if (!_pending.TryGetValue(key, out var pendingForTable))
            {
                pendingForTable = new Dictionary<RowKey, Record>();
                _pending[key] = pendingForTable;
            }

            foreach (var record in rows)
            {
                ArgumentNullException.ThrowIfNull(record);
                var rowKey = ExtractKey(record, pk);
                if (existing.TryGetValue(rowKey, out var prior))
                {
                    if (ValuesEqual(prior, record))
                    {
                        unchanged++;
                    }
                    else
                    {
                        updated++;
                        pendingForTable[rowKey] = record;
                        existing[rowKey] = record;
                    }
                }
                else
                {
                    inserted++;
                    pendingForTable[rowKey] = record;
                    existing[rowKey] = record;
                }
            }

            sw.Stop();
            return Task.FromResult(new UpsertOutcome(inserted, updated, unchanged, sw.Elapsed));
        }

        public Task<IReadOnlyCollection<string>> GetDistinctColumnValuesAsync(
            string schema, string table, string column, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);
            ArgumentException.ThrowIfNullOrWhiteSpace(table);
            ArgumentException.ThrowIfNullOrWhiteSpace(column);
            EnsureActive();

            var view = SnapshotReadView(new TableKey(schema, table));
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in view.Values)
            {
                if (record.Values.TryGetValue(column, out var v) && v is not null)
                {
                    values.Add(v.ToString()!);
                }
            }
            return Task.FromResult<IReadOnlyCollection<string>>(values);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureActive();
            _adapter.CommitPending(_pending);
            _terminal = TerminalState.Committed;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureActive();
            _pending.Clear();
            _terminal = TerminalState.RolledBack;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_terminal == TerminalState.None)
            {
                _logger.LogWarning(
                    "InMemoryTransactionScope disposed without explicit Commit or Rollback; performing implicit rollback.");
                _pending.Clear();
                _terminal = TerminalState.RolledBack;
            }
            return ValueTask.CompletedTask;
        }

        internal void RestorePendingFromSnapshot(
            Dictionary<TableKey, Dictionary<RowKey, Record>> snapshot)
        {
            _pending.Clear();
            foreach (var (table, rows) in snapshot)
            {
                _pending[table] = new Dictionary<RowKey, Record>(rows);
            }
        }

        private Dictionary<TableKey, Dictionary<RowKey, Record>> ClonePending() =>
            _pending.ToDictionary(
                kvp => kvp.Key,
                kvp => new Dictionary<RowKey, Record>(kvp.Value));

        private Dictionary<RowKey, Record> SnapshotReadView(TableKey key)
        {
            var committed = _adapter.SnapshotCommitted();
            var view = committed.TryGetValue(key, out var c)
                ? new Dictionary<RowKey, Record>(c)
                : new Dictionary<RowKey, Record>();
            if (_pending.TryGetValue(key, out var p))
            {
                foreach (var (k, r) in p)
                {
                    view[k] = r;
                }
            }
            return view;
        }

        private void EnsureActive()
        {
            if (_terminal != TerminalState.None)
            {
                throw new InvalidOperationException(
                    $"Transaction scope is already {_terminal}; subsequent calls are invalid.");
            }
        }

        private enum TerminalState { None, Committed, RolledBack }
    }

    private sealed class InMemoryNestedScope : INestedScope
    {
        private readonly InMemoryTransactionScope _outer;
        private readonly Dictionary<TableKey, Dictionary<RowKey, Record>> _snapshot;
        private readonly string _name;
        private readonly ILogger _logger;
        private TerminalState _terminal = TerminalState.None;

        public InMemoryNestedScope(
            InMemoryTransactionScope outer,
            Dictionary<TableKey, Dictionary<RowKey, Record>> snapshot,
            string name,
            ILogger logger)
        {
            _outer = outer;
            _snapshot = snapshot;
            _name = name;
            _logger = logger;
        }

        public Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureActive();
            // No-op: writes already live in the outer scope's pending
            // structure. Releasing just commits to "we're done with
            // this savepoint, the writes stay."
            _terminal = TerminalState.Released;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureActive();
            _outer.RestorePendingFromSnapshot(_snapshot);
            _terminal = TerminalState.RolledBack;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_terminal == TerminalState.None)
            {
                _logger.LogWarning(
                    "InMemoryNestedScope '{Savepoint}' disposed without explicit Release or Rollback; performing implicit rollback.",
                    _name);
                _outer.RestorePendingFromSnapshot(_snapshot);
                _terminal = TerminalState.RolledBack;
            }
            return ValueTask.CompletedTask;
        }

        private void EnsureActive()
        {
            if (_terminal != TerminalState.None)
            {
                throw new InvalidOperationException(
                    $"Nested scope '{_name}' is already {_terminal}; subsequent calls are invalid.");
            }
        }

        private enum TerminalState { None, Released, RolledBack }
    }
}
