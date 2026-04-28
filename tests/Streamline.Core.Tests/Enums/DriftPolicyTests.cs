using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class DriftPolicyTests
{
    [Fact]
    public void Values_are_the_three_documented_drift_policies()
    {
        Enum.GetNames<DriftPolicy>().Should().BeEquivalentTo(
        [
            nameof(DriftPolicy.Block),
            nameof(DriftPolicy.Warn),
            nameof(DriftPolicy.Ignore),
        ]);
    }
}
