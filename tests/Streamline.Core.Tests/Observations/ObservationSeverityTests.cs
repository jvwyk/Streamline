using AwesomeAssertions;
using Streamline.Core.Observations;
using Xunit;

namespace Streamline.Core.Tests.Observations;

public class ObservationSeverityTests
{
    [Fact]
    public void Values_are_the_four_documented_severities()
    {
        Enum.GetNames<ObservationSeverity>().Should().BeEquivalentTo(
        [
            nameof(ObservationSeverity.Info),
            nameof(ObservationSeverity.Warning),
            nameof(ObservationSeverity.Error),
            nameof(ObservationSeverity.Critical),
        ]);
    }
}
