using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class FkEnforcementModeTests
{
    [Fact]
    public void Values_are_the_three_documented_enforcement_modes()
    {
        Enum.GetNames<FkEnforcementMode>().Should().BeEquivalentTo(
        [
            nameof(FkEnforcementMode.Always),
            nameof(FkEnforcementMode.WhenParentPopulated),
            nameof(FkEnforcementMode.Never),
        ]);
    }
}
