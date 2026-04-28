using AwesomeAssertions;
using Streamline.Core.Observations;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class InMemoryObservationRepositoryTests
{
    private static readonly BatchId BatchA = new("batch-a");
    private static readonly BatchId BatchB = new("batch-b");
    private static readonly DateTimeOffset T0 = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_PersistsSingleObservation()
    {
        var repo = new InMemoryObservationRepository();
        var obs = MakeObs(BatchA, T0);

        await repo.RecordAsync(obs);

        repo.ForTestingOnly_AllObservations().Should().ContainSingle();
    }

    [Fact]
    public async Task RecordBatchAsync_PersistsAll()
    {
        var repo = new InMemoryObservationRepository();
        await repo.RecordBatchAsync(
        [
            MakeObs(BatchA, T0),
            MakeObs(BatchA, T0.AddSeconds(1)),
        ]);

        repo.ForTestingOnly_AllObservations().Should().HaveCount(2);
    }

    [Fact]
    public async Task GetForBatchAsync_FiltersByBatch()
    {
        var repo = new InMemoryObservationRepository();
        await repo.RecordAsync(MakeObs(BatchA, T0));
        await repo.RecordAsync(MakeObs(BatchB, T0));
        await repo.RecordAsync(MakeObs(BatchA, T0.AddSeconds(1)));

        var observations = new List<Observation>();
        await foreach (var obs in repo.GetForBatchAsync(BatchA))
        {
            observations.Add(obs);
        }

        observations.Should().HaveCount(2);
        observations.Should().AllSatisfy(o => o.BatchId.Should().Be(BatchA.Value));
    }

    [Fact]
    public async Task GetForBatchAsync_OrdersByRaisedAt()
    {
        var repo = new InMemoryObservationRepository();
        // Insert out of timestamp order; read back in time order.
        await repo.RecordAsync(MakeObs(BatchA, T0.AddSeconds(10), code: "C"));
        await repo.RecordAsync(MakeObs(BatchA, T0.AddSeconds(5), code: "B"));
        await repo.RecordAsync(MakeObs(BatchA, T0, code: "A"));

        var codes = new List<string>();
        await foreach (var obs in repo.GetForBatchAsync(BatchA))
        {
            codes.Add(obs.Code);
        }

        codes.Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task GetForBatchAsync_EqualTimestamps_PreserveInsertionOrder()
    {
        // Q13 pin: equal RaisedAt values must read back in insertion
        // order. Without a deterministic tiebreaker, tests asserting
        // observation order would be flaky.
        var repo = new InMemoryObservationRepository();
        await repo.RecordAsync(MakeObs(BatchA, T0, code: "FIRST"));
        await repo.RecordAsync(MakeObs(BatchA, T0, code: "SECOND"));
        await repo.RecordAsync(MakeObs(BatchA, T0, code: "THIRD"));

        var codes = new List<string>();
        await foreach (var obs in repo.GetForBatchAsync(BatchA))
        {
            codes.Add(obs.Code);
        }

        codes.Should().Equal("FIRST", "SECOND", "THIRD");
    }

    [Fact]
    public async Task GetForBatchAsync_MinSeverity_FiltersBelow()
    {
        var repo = new InMemoryObservationRepository();
        await repo.RecordAsync(MakeObs(BatchA, T0, severity: ObservationSeverity.Info));
        await repo.RecordAsync(MakeObs(BatchA, T0, severity: ObservationSeverity.Warning));
        await repo.RecordAsync(MakeObs(BatchA, T0, severity: ObservationSeverity.Error));
        await repo.RecordAsync(MakeObs(BatchA, T0, severity: ObservationSeverity.Critical));

        var observations = new List<Observation>();
        await foreach (var obs in repo.GetForBatchAsync(BatchA, ObservationSeverity.Warning))
        {
            observations.Add(obs);
        }

        observations.Select(o => o.Severity).Should().Equal(
            ObservationSeverity.Warning,
            ObservationSeverity.Error,
            ObservationSeverity.Critical);
    }

    [Fact]
    public async Task GetForBatchAsync_UnknownBatch_ReturnsEmpty()
    {
        var repo = new InMemoryObservationRepository();
        await repo.RecordAsync(MakeObs(BatchA, T0));

        var observations = new List<Observation>();
        await foreach (var obs in repo.GetForBatchAsync(new BatchId("missing")))
        {
            observations.Add(obs);
        }

        observations.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_PreCancelledToken_Throws()
    {
        var repo = new InMemoryObservationRepository();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await repo.RecordAsync(MakeObs(BatchA, T0), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecordAsync_ConcurrentBatches_DoNotInterleaveOrLoseObservations()
    {
        var repo = new InMemoryObservationRepository();

        async Task RecordManyFor(BatchId batch, int count)
        {
            for (var i = 0; i < count; i++)
            {
                await repo.RecordAsync(MakeObs(batch, T0.AddTicks(i), code: $"C{i}"));
            }
        }

        await Task.WhenAll(
            Task.Run(() => RecordManyFor(BatchA, 50)),
            Task.Run(() => RecordManyFor(BatchB, 50)));

        var allA = new List<Observation>();
        await foreach (var obs in repo.GetForBatchAsync(BatchA)) { allA.Add(obs); }
        var allB = new List<Observation>();
        await foreach (var obs in repo.GetForBatchAsync(BatchB)) { allB.Add(obs); }

        allA.Should().HaveCount(50);
        allB.Should().HaveCount(50);
        repo.ForTestingOnly_AllObservations().Should().HaveCount(100);
    }

    private static Observation MakeObs(
        BatchId batch,
        DateTimeOffset raisedAt,
        ObservationSeverity severity = ObservationSeverity.Info,
        string code = "FILE_INGESTED") =>
        new(severity, code, $"msg-{code}", batch.Value, raisedAt);
}
