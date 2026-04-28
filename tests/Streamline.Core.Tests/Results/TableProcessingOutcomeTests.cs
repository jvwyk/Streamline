using AwesomeAssertions;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class TableProcessingOutcomeTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var outcome = new TableProcessingOutcome(
            TableName: "broker",
            RowsCommitted: 100,
            RowsRolledBack: 5,
            RowsQuarantined: 2,
            Duration: TimeSpan.FromMilliseconds(300));

        outcome.TableName.Should().Be("broker");
        outcome.RowsCommitted.Should().Be(100);
        outcome.RowsRolledBack.Should().Be(5);
        outcome.RowsQuarantined.Should().Be(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankTableName_Throws(string name)
    {
        var act = () => new TableProcessingOutcome(name, 0, 0, 0, TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Construct_WithNegativeRowCount_Throws(long committed, long rolled, long quarantined)
    {
        var act = () => new TableProcessingOutcome("t", committed, rolled, quarantined, TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_WithNegativeDuration_Throws()
    {
        var act = () => new TableProcessingOutcome("t", 0, 0, 0, TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
