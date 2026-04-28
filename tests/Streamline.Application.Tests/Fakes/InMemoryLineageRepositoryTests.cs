using System.Collections.Immutable;
using AwesomeAssertions;
using Streamline.Domain.Lineage;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryLineageRepositoryTests
{
    [Fact]
    public async Task RecordAsync_PersistsEntries()
    {
        var repo = new InMemoryLineageRepository();
        await repo.RecordAsync([Lineage(1, "core", "broker", [42])]);

        repo.ForTestingOnly_AllEntries().Should().HaveCount(1);
    }

    [Fact]
    public async Task RecordAsync_DoesNotDeduplicate()
    {
        var repo = new InMemoryLineageRepository();
        var entry = Lineage(1, "core", "broker", [42]);
        await repo.RecordAsync([entry]);
        await repo.RecordAsync([entry]);

        // The contract is explicit: duplicates land as duplicates.
        // Callers must not re-record on retry.
        repo.ForTestingOnly_AllEntries().Should().HaveCount(2);
    }

    [Fact]
    public async Task GetForSourceRowAsync_FiltersByIncomingId()
    {
        var repo = new InMemoryLineageRepository();
        await repo.RecordAsync(
        [
            Lineage(1, "core", "broker", [10]),
            Lineage(2, "core", "broker", [20]),
            Lineage(1, "core", "trust", [30]),
        ]);

        var results = new List<RowLineage>();
        await foreach (var entry in repo.GetForSourceRowAsync(1))
        {
            results.Add(entry);
        }

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e => e.IncomingId.Should().Be(1));
    }

    [Fact]
    public async Task GetForSourceRowAsync_NoMatches_ReturnsEmpty()
    {
        var repo = new InMemoryLineageRepository();
        await repo.RecordAsync([Lineage(1, "core", "broker", [10])]);

        var results = new List<RowLineage>();
        await foreach (var entry in repo.GetForSourceRowAsync(999))
        {
            results.Add(entry);
        }

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetForDestinationAsync_FiltersBySchemaTablePk()
    {
        var repo = new InMemoryLineageRepository();
        await repo.RecordAsync(
        [
            Lineage(1, "core", "broker", [10]),
            Lineage(2, "core", "broker", [20]),
            Lineage(3, "core", "broker", [10]),  // multi-source aggregation
            Lineage(4, "warehouse", "broker", [10]),
        ]);

        var results = new List<RowLineage>();
        await foreach (var entry in repo.GetForDestinationAsync("core", "broker", [10]))
        {
            results.Add(entry);
        }

        results.Should().HaveCount(2);
        results.Select(e => e.IncomingId).Should().BeEquivalentTo([1L, 3L]);
    }

    [Fact]
    public async Task GetForDestinationAsync_CompositePk_MatchesExactSequence()
    {
        var repo = new InMemoryLineageRepository();
        await repo.RecordAsync(
        [
            Lineage(1, "core", "yearly", [42, 2025]),
            Lineage(2, "core", "yearly", [42, 2026]),
        ]);

        var results = new List<RowLineage>();
        await foreach (var entry in repo.GetForDestinationAsync("core", "yearly", [42, 2025]))
        {
            results.Add(entry);
        }

        results.Should().HaveCount(1);
        results[0].IncomingId.Should().Be(1);
    }

    [Fact]
    public async Task RecordAsync_PreCancelledToken_Throws()
    {
        var repo = new InMemoryLineageRepository();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await repo.RecordAsync([Lineage(1, "core", "broker", [10])], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecordAsync_ConcurrentWrites_AllPreserved()
    {
        var repo = new InMemoryLineageRepository();
        var tasks = Enumerable.Range(1, 30).Select(i =>
            Task.Run(() => repo.RecordAsync([Lineage(i, "core", "broker", [i])]))).ToArray();
        await Task.WhenAll(tasks);

        repo.ForTestingOnly_AllEntries().Should().HaveCount(30);
    }

    private static RowLineage Lineage(long incomingId, string schema, string table, params object[] pk) =>
        new(incomingId, schema, table, [.. pk]);
}
