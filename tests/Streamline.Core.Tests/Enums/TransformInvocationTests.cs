using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class TransformInvocationTests
{
    [Fact]
    public void Values_are_the_v1_set()
    {
        // Single value today (PerBatch); PerRow lands when a real job needs it.
        Enum.GetNames<TransformInvocation>().Should().BeEquivalentTo(
        [
            nameof(TransformInvocation.PerBatch),
        ]);
    }
}
