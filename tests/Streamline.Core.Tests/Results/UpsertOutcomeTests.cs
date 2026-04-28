using AwesomeAssertions;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class UpsertOutcomeTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var outcome = new UpsertOutcome(
            RowsInserted: 10,
            RowsUpdated: 3,
            RowsUnchanged: 1,
            Duration: TimeSpan.FromSeconds(0.5));

        outcome.RowsInserted.Should().Be(10);
        outcome.RowsUpdated.Should().Be(3);
        outcome.RowsUnchanged.Should().Be(1);
        outcome.Duration.Should().Be(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void TotalRows_SumsInsertedUpdatedAndUnchanged()
    {
        var outcome = new UpsertOutcome(10, 3, 1, TimeSpan.Zero);
        outcome.TotalRows.Should().Be(14);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Construct_WithNegativeRowCount_Throws(long inserted, long updated, long unchanged)
    {
        var act = () => new UpsertOutcome(inserted, updated, unchanged, TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_WithNegativeDuration_Throws()
    {
        var act = () => new UpsertOutcome(0, 0, 0, TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Empty_AllZero()
    {
        UpsertOutcome.Empty.TotalRows.Should().Be(0);
        UpsertOutcome.Empty.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new UpsertOutcome(1, 2, 3, TimeSpan.FromMilliseconds(10));
        var b = new UpsertOutcome(1, 2, 3, TimeSpan.FromMilliseconds(10));
        a.Should().Be(b);
    }
}
