using AwesomeAssertions;
using Streamline.Core.Observations;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Fakes;

public class ForwardingObservationSinkTests
{
    private static readonly BatchId AnyBatch = new("batch-1");
    private static readonly DateTimeOffset AnyTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construct_NullRepository_Throws()
    {
        var act = () => new ForwardingObservationSink(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RecordAsync_NullObservation_Throws()
    {
        var sink = new ForwardingObservationSink(new InMemoryObservationRepository());
        var act = async () => await sink.RecordAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RecordAsync_ForwardsToRepository()
    {
        var repository = new InMemoryObservationRepository();
        var sink = new ForwardingObservationSink(repository);
        var observation = new Observation(
            ObservationSeverity.Info, "FILE_INGESTED", "msg", AnyBatch.Value, AnyTime);

        await sink.RecordAsync(observation);

        var stored = repository.ForTestingOnly_AllObservations();
        stored.Should().ContainSingle().Which.Should().BeSameAs(observation);
    }
}
