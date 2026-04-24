using AwesomeAssertions;
using Streamline.Core.Results;
using Xunit;

namespace Streamline.Core.Tests.Results;

public class ProcessingResultTests
{
    [Fact]
    public void Totals_SumAcrossTables()
    {
        var result = new ProcessingResult(
        [
            new TableProcessingOutcome("broker", 10, 1, 0, TimeSpan.FromMilliseconds(100)),
            new TableProcessingOutcome("trade", 100, 0, 2, TimeSpan.FromMilliseconds(300)),
        ]);

        result.TotalRowsCommitted.Should().Be(110);
        result.TotalRowsRolledBack.Should().Be(1);
        result.TotalRowsQuarantined.Should().Be(2);
        result.TotalDuration.Should().Be(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public void Empty_IsSingletonWithNoTables()
    {
        ProcessingResult.Empty.Tables.Should().BeEmpty();
        ProcessingResult.Empty.TotalRowsCommitted.Should().Be(0);
    }

    [Fact]
    public void Construct_WithNullTables_Throws()
    {
        var act = () => new ProcessingResult(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Equality_IsStructuralOverTableSequence()
    {
        var a = new ProcessingResult(
            [new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero)]);
        var b = new ProcessingResult(
            [new TableProcessingOutcome("t", 1, 0, 0, TimeSpan.Zero)]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
