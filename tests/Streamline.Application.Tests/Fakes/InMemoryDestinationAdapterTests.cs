using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Xunit;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryDestinationAdapterTests
{
    // ---- BeginTransactionAsync ----------------------------------------

    [Fact]
    public async Task BeginTransactionAsync_ReturnsScope()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();
        scope.Should().NotBeNull();
    }

    // ---- UpsertAsync classification ----------------------------------

    [Fact]
    public async Task UpsertAsync_NewRow_ClassifiesAsInserted()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();

        var outcome = await scope.UpsertAsync(
            "core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);

        outcome.RowsInserted.Should().Be(1);
        outcome.RowsUpdated.Should().Be(0);
        outcome.RowsUnchanged.Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_ExistingRowSameValues_ClassifiesAsUnchanged()
    {
        var adapter = new InMemoryDestinationAdapter();
        adapter.ForTestingOnly_SeedCommitted("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);
        await using var scope = await adapter.BeginTransactionAsync();

        var outcome = await scope.UpsertAsync(
            "core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);

        outcome.RowsInserted.Should().Be(0);
        outcome.RowsUpdated.Should().Be(0);
        outcome.RowsUnchanged.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_ExistingRowDifferentValues_ClassifiesAsUpdated()
    {
        var adapter = new InMemoryDestinationAdapter();
        adapter.ForTestingOnly_SeedCommitted("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);
        await using var scope = await adapter.BeginTransactionAsync();

        var outcome = await scope.UpsertAsync(
            "core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme Inc")]);

        outcome.RowsInserted.Should().Be(0);
        outcome.RowsUpdated.Should().Be(1);
        outcome.RowsUnchanged.Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_ReadYourWrites_ClassifiesSecondUpsertAsUpdate()
    {
        // Pinning the Q10 correctness detail: an upsert within the
        // same scope must see prior pending writes, so a second
        // upsert of the same PK is Update or Unchanged, not a second
        // Insert. Without read-your-writes the fake would report two
        // inserts and silently disagree with Postgres.
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();

        await scope.UpsertAsync("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);
        var second = await scope.UpsertAsync("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme Inc")]);

        second.RowsInserted.Should().Be(0);
        second.RowsUpdated.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_NoPrimaryKey_Throws()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();
        var schemaWithoutPk = new SchemaDefinition(
            [new ColumnDefinition("name", ColumnTypeCode.String) { IsRequired = true }]);

        var act = async () => await scope.UpsertAsync(
            "core", "broker", schemaWithoutPk,
            [BuildRecord(broker_id: 1, name: "Acme")]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*primary key*");
    }

    // ---- Commit / Rollback semantics ----------------------------------

    [Fact]
    public async Task Commit_AppliesPendingToCommittedStore()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using (var scope = await adapter.BeginTransactionAsync())
        {
            await scope.UpsertAsync("core", "broker", BuildSchema(),
                [BuildRecord(broker_id: 1, name: "Acme")]);
            await scope.CommitAsync();
        }

        var committed = adapter.ForTestingOnly_GetCommitted("core", "broker");
        committed.Should().HaveCount(1);
    }

    [Fact]
    public async Task Rollback_DiscardsPendingWrites()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using (var scope = await adapter.BeginTransactionAsync())
        {
            await scope.UpsertAsync("core", "broker", BuildSchema(),
                [BuildRecord(broker_id: 1, name: "Acme")]);
            await scope.RollbackAsync();
        }

        adapter.ForTestingOnly_GetCommitted("core", "broker").Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_WithoutCommitOrRollback_LogsWarningAndRollsBack()
    {
        var logger = Substitute.For<ILogger<InMemoryDestinationAdapter>>();
        var adapter = new InMemoryDestinationAdapter(logger);

        await using (var scope = await adapter.BeginTransactionAsync())
        {
            await scope.UpsertAsync("core", "broker", BuildSchema(),
                [BuildRecord(broker_id: 1, name: "Acme")]);
            // Neither Commit nor Rollback called.
        }

        adapter.ForTestingOnly_GetCommitted("core", "broker").Should().BeEmpty();
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Commit_AfterCommit_Throws()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();
        await scope.CommitAsync();

        var act = async () => await scope.CommitAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Rollback_AfterCommit_Throws()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();
        await scope.CommitAsync();

        var act = async () => await scope.RollbackAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- GetDistinctColumnValuesAsync --------------------------------

    [Fact]
    public async Task GetDistinctColumnValuesAsync_ReturnsCommittedValues()
    {
        var adapter = new InMemoryDestinationAdapter();
        adapter.ForTestingOnly_SeedCommitted("core", "broker", BuildSchema(),
        [
            BuildRecord(broker_id: 1, name: "Acme"),
            BuildRecord(broker_id: 2, name: "Globex"),
        ]);
        await using var scope = await adapter.BeginTransactionAsync();

        var values = await scope.GetDistinctColumnValuesAsync("core", "broker", "broker_id");

        values.Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public async Task GetDistinctColumnValuesAsync_ReadsYourWrites()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();
        await scope.UpsertAsync("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 42, name: "Acme")]);

        var values = await scope.GetDistinctColumnValuesAsync("core", "broker", "broker_id");

        values.Should().Contain("42");
    }

    // ---- Nested scope (savepoint) ------------------------------------

    [Fact]
    public async Task NestedScope_Release_KeepsWritesInOuterScope()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();

        await using (var nested = await scope.BeginNestedScopeAsync("sp_broker"))
        {
            await scope.UpsertAsync("core", "broker", BuildSchema(),
                [BuildRecord(broker_id: 1, name: "Acme")]);
            await nested.ReleaseAsync();
        }
        await scope.CommitAsync();

        adapter.ForTestingOnly_GetCommitted("core", "broker").Should().HaveCount(1);
    }

    [Fact]
    public async Task NestedScope_Rollback_DiscardsNestedWrites_OuterUnaffected()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();

        // Outer-scope write before opening the savepoint — should
        // survive the nested rollback.
        await scope.UpsertAsync("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);

        await using (var nested = await scope.BeginNestedScopeAsync("sp_trust"))
        {
            await scope.UpsertAsync("core", "trust", BuildSchema(),
                [BuildRecord(broker_id: 99, name: "BadTrust")]);
            await nested.RollbackAsync();
        }
        await scope.CommitAsync();

        adapter.ForTestingOnly_GetCommitted("core", "broker").Should().HaveCount(1);
        adapter.ForTestingOnly_GetCommitted("core", "trust").Should().BeEmpty();
    }

    [Fact]
    public async Task NestedScope_DisposeWithoutTerminal_RollsBackWithWarning()
    {
        var logger = Substitute.For<ILogger<InMemoryDestinationAdapter>>();
        var adapter = new InMemoryDestinationAdapter(logger);
        await using var scope = await adapter.BeginTransactionAsync();
        await scope.UpsertAsync("core", "broker", BuildSchema(),
            [BuildRecord(broker_id: 1, name: "Acme")]);

        await using (var nested = await scope.BeginNestedScopeAsync("sp_trust"))
        {
            await scope.UpsertAsync("core", "trust", BuildSchema(),
                [BuildRecord(broker_id: 99, name: "BadTrust")]);
            // Neither Release nor Rollback called.
        }
        await scope.CommitAsync();

        adapter.ForTestingOnly_GetCommitted("core", "broker").Should().HaveCount(1);
        adapter.ForTestingOnly_GetCommitted("core", "trust").Should().BeEmpty();
        logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task NestedScope_AfterRelease_AnotherCallThrows()
    {
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();
        var nested = await scope.BeginNestedScopeAsync("sp");
        await nested.ReleaseAsync();

        var act = async () => await nested.RollbackAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- composite PK ------------------------------------------------

    [Fact]
    public async Task UpsertAsync_CompositePrimaryKey_DistinguishesRows()
    {
        var schema = new SchemaDefinition(
        [
            new ColumnDefinition("broker_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
            new ColumnDefinition("year", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
            new ColumnDefinition("revenue", ColumnTypeCode.Decimal),
        ]);
        var adapter = new InMemoryDestinationAdapter();
        await using var scope = await adapter.BeginTransactionAsync();

        var outcome = await scope.UpsertAsync("core", "yearly", schema,
        [
            BuildRecordWith(("broker_id", 1), ("year", 2025), ("revenue", 100m)),
            BuildRecordWith(("broker_id", 1), ("year", 2026), ("revenue", 200m)),
            BuildRecordWith(("broker_id", 2), ("year", 2026), ("revenue", 50m)),
        ]);

        outcome.RowsInserted.Should().Be(3);
    }

    // ---- helpers -----------------------------------------------------

    private static SchemaDefinition BuildSchema() =>
        new(
        [
            new ColumnDefinition("broker_id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
            new ColumnDefinition("name", ColumnTypeCode.String)
            {
                IsRequired = true,
                MaxLength = 200,
            },
        ]);

    private static Record BuildRecord(int broker_id, string name) =>
        new("a.csv", broker_id,
            ImmutableDictionary<string, object?>.Empty
                .Add("broker_id", broker_id)
                .Add("name", name));

    private static Record BuildRecordWith(params (string Key, object? Value)[] kvps)
    {
        var dict = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in kvps)
        {
            dict.Add(k, v);
        }
        return new Record("a.csv", 0, dict.ToImmutable());
    }
}
