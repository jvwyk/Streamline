using AwesomeAssertions;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class IngestionResultTests
{
    [Fact]
    public void Totals_SumAcrossFiles()
    {
        var result = new IngestionResult(
        [
            new FileIngestionOutcome("a.csv", 10, 9, 1, TimeSpan.FromMilliseconds(100)),
            new FileIngestionOutcome("b.csv", 20, 20, 0, TimeSpan.FromMilliseconds(200)),
            new FileIngestionOutcome("c.csv", 5, 3, 2, TimeSpan.FromMilliseconds(50)),
        ]);

        result.TotalRowsRead.Should().Be(35);
        result.TotalRowsStaged.Should().Be(32);
        result.TotalRowsQuarantined.Should().Be(3);
        result.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public void Empty_IsSingletonWithNoFiles()
    {
        IngestionResult.Empty.Files.Should().BeEmpty();
        IngestionResult.Empty.TotalRowsRead.Should().Be(0);
        IngestionResult.Empty.TotalDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Construct_WithNullFiles_Throws()
    {
        var act = () => new IngestionResult(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_IsStructuralOverFileSequence()
    {
        var a = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero)]);
        var b = new IngestionResult(
            [new FileIngestionOutcome("a.csv", 1, 1, 0, TimeSpan.Zero)]);
        var c = new IngestionResult(
            [new FileIngestionOutcome("b.csv", 1, 1, 0, TimeSpan.Zero)]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }
}
