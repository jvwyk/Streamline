using AwesomeAssertions;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Domain.Tests.Transforms;

public class TransformOutcomeTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var outcome = new TransformOutcome(
            RowsInserted: 100,
            RowsUpdated: 5,
            RowsSkipped: 2,
            RowsFailed: 1,
            Duration: TimeSpan.FromSeconds(0.5),
            ErrorMessage: "one row had an unexpected discriminator");

        outcome.RowsInserted.Should().Be(100);
        outcome.RowsUpdated.Should().Be(5);
        outcome.RowsSkipped.Should().Be(2);
        outcome.RowsFailed.Should().Be(1);
        outcome.Duration.Should().Be(TimeSpan.FromSeconds(0.5));
        outcome.ErrorMessage.Should().Be("one row had an unexpected discriminator");
    }

    [Fact]
    public void Construct_WithoutErrorMessage_DefaultsToNull()
    {
        var outcome = new TransformOutcome(0, 0, 0, 0, TimeSpan.Zero);
        outcome.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void TotalRowsAttempted_SumsAllCategories()
    {
        var outcome = new TransformOutcome(10, 3, 2, 1, TimeSpan.Zero);
        outcome.TotalRowsAttempted.Should().Be(16);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Construct_WithNegativeRowCount_Throws(long inserted, long updated, long skipped, long failed)
    {
        var act = () => new TransformOutcome(inserted, updated, skipped, failed, TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_WithNegativeDuration_Throws()
    {
        var act = () => new TransformOutcome(0, 0, 0, 0, TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new TransformOutcome(1, 2, 3, 4, TimeSpan.FromMilliseconds(100), "err");
        var b = new TransformOutcome(1, 2, 3, 4, TimeSpan.FromMilliseconds(100), "err");
        a.Should().Be(b);
    }
}
