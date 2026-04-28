using AwesomeAssertions;
using Streamline.Core.ValueTypes;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryFileMappingRepositoryTests
{
    [Fact]
    public async Task ResolveAsync_NoMappings_ReturnsNull()
    {
        var repo = new InMemoryFileMappingRepository();
        var result = await repo.ResolveAsync("anything.csv");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNull()
    {
        var repo = new InMemoryFileMappingRepository();
        await repo.UpsertAsync(new FileMapping(@"^brokers\.csv$", "broker", "delimited"));

        var result = await repo.ResolveAsync("trusts.csv");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_FirstMatchWins()
    {
        // Two mappings could each match "broker_2025.csv". Insertion
        // order must determine which one resolves.
        var repo = new InMemoryFileMappingRepository();
        var first = new FileMapping(@"broker", "broker_yearly", "delimited");
        var second = new FileMapping(@"broker_\d+", "broker_yearly_strict", "delimited");

        await repo.UpsertAsync(first);
        await repo.UpsertAsync(second);
        var result = await repo.ResolveAsync("broker_2025.csv");

        result.Should().Be(first);
    }

    [Fact]
    public async Task UpsertAsync_SamePatternAndTable_ReplacesExisting()
    {
        var repo = new InMemoryFileMappingRepository();
        var first = new FileMapping(@"broker\.csv", "broker", "delimited",
            [new KeyValuePair<string, string>("delimiter", ",")]);
        var second = new FileMapping(@"broker\.csv", "broker", "delimited",
            [new KeyValuePair<string, string>("delimiter", ";")]);

        await repo.UpsertAsync(first);
        await repo.UpsertAsync(second);

        var all = await repo.GetAllActiveAsync();
        all.Should().HaveCount(1);
        all[0].ReaderConfig["delimiter"].Should().Be(";");
    }

    [Fact]
    public async Task UpsertAsync_SamePatternDifferentTable_AddsDistinctEntry()
    {
        var repo = new InMemoryFileMappingRepository();
        var first = new FileMapping(@"data\.csv", "broker", "delimited");
        var second = new FileMapping(@"data\.csv", "trust", "delimited");

        await repo.UpsertAsync(first);
        await repo.UpsertAsync(second);

        var all = await repo.GetAllActiveAsync();
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpsertAsync_PreservesInsertionOrder_AfterReplace()
    {
        var repo = new InMemoryFileMappingRepository();
        var a = new FileMapping(@"a\.csv", "a", "delimited");
        var b = new FileMapping(@"b\.csv", "b", "delimited");
        var aReplaced = new FileMapping(@"a\.csv", "a", "delimited",
            [new KeyValuePair<string, string>("delimiter", "\t")]);

        await repo.UpsertAsync(a);
        await repo.UpsertAsync(b);
        await repo.UpsertAsync(aReplaced);

        var all = await repo.GetAllActiveAsync();
        all.Select(m => m.TargetTable).Should().Equal("a", "b");
        all[0].ReaderConfig["delimiter"].Should().Be("\t");
    }

    [Fact]
    public async Task GetAllActiveAsync_EmptyRepository_ReturnsEmpty()
    {
        var repo = new InMemoryFileMappingRepository();
        var all = await repo.GetAllActiveAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentWrites_AllPreserved()
    {
        var repo = new InMemoryFileMappingRepository();
        var tasks = Enumerable.Range(0, 30).Select(i =>
            Task.Run(() => repo.UpsertAsync(
                new FileMapping($"^pattern_{i}$", $"table-{i}", "delimited")))).ToArray();
        await Task.WhenAll(tasks);

        var all = await repo.GetAllActiveAsync();
        all.Should().HaveCount(30);
    }
}
