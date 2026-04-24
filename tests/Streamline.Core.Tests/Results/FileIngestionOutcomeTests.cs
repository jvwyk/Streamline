using AwesomeAssertions;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class FileIngestionOutcomeTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var outcome = new FileIngestionOutcome(
            FileName: "f.csv",
            RowsRead: 100,
            RowsStaged: 97,
            RowsQuarantined: 3,
            Duration: TimeSpan.FromMilliseconds(500));

        outcome.FileName.Should().Be("f.csv");
        outcome.RowsRead.Should().Be(100);
        outcome.RowsStaged.Should().Be(97);
        outcome.RowsQuarantined.Should().Be(3);
        outcome.Duration.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankFileName_Throws(string name)
    {
        var act = () => new FileIngestionOutcome(name, 0, 0, 0, TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Construct_WithNegativeRowCount_Throws(long read, long staged, long quarantined)
    {
        var act = () => new FileIngestionOutcome("f.csv", read, staged, quarantined, TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_WithNegativeDuration_Throws()
    {
        var act = () => new FileIngestionOutcome("f.csv", 0, 0, 0, TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new FileIngestionOutcome("f.csv", 1, 1, 0, TimeSpan.Zero);
        var b = new FileIngestionOutcome("f.csv", 1, 1, 0, TimeSpan.Zero);
        a.Should().Be(b);
    }
}
