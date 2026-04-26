using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Registry;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryRegistryRepositoryTests
{
    [Fact]
    public async Task UpsertAsync_AndGetActiveAsync_RoundTrip()
    {
        var repo = new InMemoryRegistryRepository();
        var entry = MakeEntry("broker");

        await repo.UpsertAsync(entry);
        var fetched = await repo.GetActiveAsync("broker");

        fetched.Should().Be(entry);
    }

    [Fact]
    public async Task GetActiveAsync_UnknownTable_ReturnsNull()
    {
        var repo = new InMemoryRegistryRepository();
        var fetched = await repo.GetActiveAsync("missing");
        fetched.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_InactiveEntry_ReturnsNull()
    {
        var repo = new InMemoryRegistryRepository();
        var entry = MakeEntry("broker") with { IsActive = false };
        await repo.UpsertAsync(entry);

        var fetched = await repo.GetActiveAsync("broker");

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveAsync_OutsideDateWindow_ReturnsNull()
    {
        var repo = new InMemoryRegistryRepository();
        // ValidTo in the past.
        var future = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddDays(-1);
        var entry = MakeEntry("broker") with
        {
            ValidFrom = future.AddDays(-30),
            ValidTo = future,
        };
        await repo.UpsertAsync(entry);

        var fetched = await repo.GetActiveAsync("broker");

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_ReplacesExistingByTableName()
    {
        var repo = new InMemoryRegistryRepository();
        var first = MakeEntry("broker") with { TargetSchema = "core" };
        var second = MakeEntry("broker") with { TargetSchema = "warehouse" };

        await repo.UpsertAsync(first);
        await repo.UpsertAsync(second);

        var fetched = await repo.GetActiveAsync("broker");
        fetched!.TargetSchema.Should().Be("warehouse");
    }

    [Fact]
    public async Task GetAllActiveAsync_ReturnsOnlyActiveEntries()
    {
        var repo = new InMemoryRegistryRepository();
        await repo.UpsertAsync(MakeEntry("broker"));
        await repo.UpsertAsync(MakeEntry("trust") with { IsActive = false });

        var all = await repo.GetAllActiveAsync();

        all.Select(e => e.TableName).Should().BeEquivalentTo(["broker"]);
    }

    // ---- topological sort -------------------------------------------

    [Fact]
    public async Task GetLoadOrderAsync_RespectsDependsOnEdges()
    {
        // dim_broker is parent of fact_position; both should land,
        // dim_broker first.
        var repo = new InMemoryRegistryRepository();
        await repo.UpsertAsync(MakeEntry("fact_position") with
        {
            DependsOn = ["dim_broker"],
        });
        await repo.UpsertAsync(MakeEntry("dim_broker"));

        var order = await repo.GetLoadOrderAsync();

        order.Should().Equal("dim_broker", "fact_position");
    }

    [Fact]
    public async Task GetLoadOrderAsync_ChainOfThree_OrderedCorrectly()
    {
        var repo = new InMemoryRegistryRepository();
        await repo.UpsertAsync(MakeEntry("c") with { DependsOn = ["b"] });
        await repo.UpsertAsync(MakeEntry("b") with { DependsOn = ["a"] });
        await repo.UpsertAsync(MakeEntry("a"));

        var order = await repo.GetLoadOrderAsync();

        order.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task GetLoadOrderAsync_IndependentEntries_AlphabeticalAsTiebreaker()
    {
        // Two entries with no dependencies between them. Order is
        // alphabetical (deterministic) so tests aren't flaky on
        // dictionary-iteration order.
        var repo = new InMemoryRegistryRepository();
        await repo.UpsertAsync(MakeEntry("zebra"));
        await repo.UpsertAsync(MakeEntry("alpha"));

        var order = await repo.GetLoadOrderAsync();

        order.Should().Equal("alpha", "zebra");
    }

    [Fact]
    public async Task GetLoadOrderAsync_Cycle_Throws()
    {
        var repo = new InMemoryRegistryRepository();
        await repo.UpsertAsync(MakeEntry("a") with { DependsOn = ["b"] });
        await repo.UpsertAsync(MakeEntry("b") with { DependsOn = ["a"] });

        var act = async () => await repo.GetLoadOrderAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task GetLoadOrderAsync_DependencyOutsideActiveSet_Ignored()
    {
        // fact_position depends on dim_broker, but dim_broker isn't
        // in the active set. The fake silently treats the dep as
        // unsatisfiable-but-ignorable; fact_position emits anyway.
        var repo = new InMemoryRegistryRepository();
        await repo.UpsertAsync(MakeEntry("fact_position") with
        {
            DependsOn = ["dim_broker"],
        });

        var order = await repo.GetLoadOrderAsync();

        order.Should().Equal("fact_position");
    }

    [Fact]
    public async Task GetLoadOrderAsync_EmptyRegistry_ReturnsEmpty()
    {
        var repo = new InMemoryRegistryRepository();
        var order = await repo.GetLoadOrderAsync();
        order.Should().BeEmpty();
    }

    // ---- concurrency ------------------------------------------------

    [Fact]
    public async Task UpsertAsync_ConcurrentWrites_AllPreserved()
    {
        var repo = new InMemoryRegistryRepository();
        var tasks = Enumerable.Range(0, 30).Select(i =>
            Task.Run(() => repo.UpsertAsync(MakeEntry($"table-{i}")))).ToArray();
        await Task.WhenAll(tasks);

        var all = await repo.GetAllActiveAsync();
        all.Should().HaveCount(30);
    }

    private static RegistryEntry MakeEntry(string tableName) =>
        new(tableName, BuildSchema())
        {
            TargetSchema = "core",
            ValidFrom = new DateOnly(2020, 1, 1),
        };

    private static SchemaDefinition BuildSchema() =>
        new(
        [
            new ColumnDefinition("id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                IsPrimaryKey = true,
            },
        ]);
}
