using AwesomeAssertions;
using Streamline.Domain.Lineage;
using Xunit;

namespace Streamline.Domain.Tests.Lineage;

public class RowLineageTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var lineage = new RowLineage(
            IncomingId: 42,
            DestinationSchema: "intembeko",
            DestinationTable: "dim_broker",
            DestinationPkValues: ["BRK-001", new DateOnly(2025, 1, 1)]);

        lineage.IncomingId.Should().Be(42);
        lineage.DestinationSchema.Should().Be("intembeko");
        lineage.DestinationTable.Should().Be("dim_broker");
        lineage.DestinationPkValues.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construct_WithNonPositiveIncomingId_Throws(long incomingId)
    {
        var act = () => new RowLineage(incomingId, "s", "t", ["x"]);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankSchema_Throws(string schema)
    {
        var act = () => new RowLineage(1, schema, "t", ["x"]);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankTable_Throws(string table)
    {
        var act = () => new RowLineage(1, "s", table, ["x"]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithEmptyPkValues_Throws()
    {
        var act = () => new RowLineage(1, "s", "t", []);
        act.Should().Throw<ArgumentException>().WithMessage("*at least one*");
    }

    [Fact]
    public void Equality_IsStructuralOverPkSequence()
    {
        var a = new RowLineage(1, "s", "t", ["a", "b"]);
        var b = new RowLineage(1, "s", "t", ["a", "b"]);
        var different = new RowLineage(1, "s", "t", ["b", "a"]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }
}
