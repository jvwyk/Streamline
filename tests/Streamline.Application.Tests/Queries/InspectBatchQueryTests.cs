using AwesomeAssertions;
using Streamline.Application.Queries;
using Streamline.Core.Observations;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Application.Tests.Queries;

public class InspectBatchQueryTests
{
    private static readonly BatchId AnyBatch = new("batch-1");

    [Fact]
    public void Construct_DefaultsMinSeverityToNull()
    {
        var query = new InspectBatchQuery(AnyBatch);
        query.MinSeverity.Should().BeNull();
    }

    [Fact]
    public void Construct_WithMinSeverity_PreservesIt()
    {
        var query = new InspectBatchQuery(AnyBatch, ObservationSeverity.Warning);
        query.MinSeverity.Should().Be(ObservationSeverity.Warning);
    }

    [Fact]
    public void Construct_DefaultBatchId_Throws()
    {
        var act = () => new InspectBatchQuery(default);
        act.Should().Throw<ArgumentException>();
    }
}
