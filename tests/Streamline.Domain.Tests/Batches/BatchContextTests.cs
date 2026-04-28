using AwesomeAssertions;
using NSubstitute;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Domain.Tests.Batches;

public class BatchContextTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var lineage = Substitute.For<ILineageWriter>();
        var context = new BatchContext(
            BatchId: new BatchId("batch-1"),
            FileLogIds: [1, 2, 3],
            SourceSchema: "staging",
            Lineage: lineage);

        context.BatchId.Value.Should().Be("batch-1");
        context.FileLogIds.Should().Equal(1L, 2L, 3L);
        context.SourceSchema.Should().Be("staging");
        context.Lineage.Should().BeSameAs(lineage);
    }

    [Fact]
    public void Construct_WithDefaultBatchId_Throws()
    {
        var lineage = Substitute.For<ILineageWriter>();
        var act = () => new BatchContext(default, [1], "staging", lineage);
        act.Should().Throw<ArgumentException>().WithMessage("*default(BatchId)*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankSourceSchema_Throws(string schema)
    {
        var lineage = Substitute.For<ILineageWriter>();
        var act = () => new BatchContext(new BatchId("b"), [1], schema, lineage);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithNullLineage_Throws()
    {
        var act = () => new BatchContext(new BatchId("b"), [1], "staging", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
