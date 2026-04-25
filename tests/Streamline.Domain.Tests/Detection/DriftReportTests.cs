using AwesomeAssertions;
using Streamline.Domain.Detection;
using Xunit;

namespace Streamline.Domain.Tests.Detection;

public class DriftReportTests
{
    [Fact]
    public void Empty_HasAllListsEmpty_AndIsEmptyTrue()
    {
        DriftReport.Empty.IsEmpty.Should().BeTrue();
        DriftReport.Empty.NewColumns.Should().BeEmpty();
        DriftReport.Empty.MissingRequiredColumns.Should().BeEmpty();
        DriftReport.Empty.MissingOptionalColumns.Should().BeEmpty();
        DriftReport.Empty.HasNewColumns.Should().BeFalse();
        DriftReport.Empty.HasMissingRequiredColumns.Should().BeFalse();
        DriftReport.Empty.HasMissingOptionalColumns.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_FalseWhenAnyDriftDimensionPopulated()
    {
        new DriftReport(["X"], [], []).IsEmpty.Should().BeFalse();
        new DriftReport([], ["amount"], []).IsEmpty.Should().BeFalse();
        new DriftReport([], [], ["optional"]).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Has_FlagsReportPresence()
    {
        var report = new DriftReport(["X"], ["amount"], ["optional"]);

        report.HasNewColumns.Should().BeTrue();
        report.HasMissingRequiredColumns.Should().BeTrue();
        report.HasMissingOptionalColumns.Should().BeTrue();
    }

    [Fact]
    public void Equality_IsStructuralOverEachDimension()
    {
        var a = new DriftReport(["X", "Y"], ["amount"], []);
        var b = new DriftReport(["X", "Y"], ["amount"], []);
        var c = new DriftReport(["Y", "X"], ["amount"], []);  // order matters

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }
}
