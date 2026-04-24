using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class BatchStatusTests
{
    [Fact]
    public void Values_are_the_six_documented_batch_states()
    {
        Enum.GetNames<BatchStatus>().Should().BeEquivalentTo(
        [
            nameof(BatchStatus.Created),
            nameof(BatchStatus.Ingesting),
            nameof(BatchStatus.Ingested),
            nameof(BatchStatus.Processing),
            nameof(BatchStatus.Completed),
            nameof(BatchStatus.Failed),
        ]);
    }
}
