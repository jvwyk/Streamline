using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Xunit;

namespace Streamline.Core.Tests.ValueTypes;

public class SchemaDefinitionTests
{
    [Fact]
    public void Construct_WithColumns_PreservesDeclarationOrder()
    {
        var columns = new[]
        {
            new ColumnDefinition("c", ColumnTypeCode.String),
            new ColumnDefinition("a", ColumnTypeCode.Integer),
            new ColumnDefinition("b", ColumnTypeCode.Boolean),
        };

        var schema = new SchemaDefinition(columns);

        schema.Columns.Select(c => c.Name).Should().Equal("c", "a", "b");
    }

    [Fact]
    public void Construct_WithDuplicateColumnNames_Throws()
    {
        var columns = new[]
        {
            new ColumnDefinition("amount", ColumnTypeCode.Decimal),
            new ColumnDefinition("amount", ColumnTypeCode.String),
        };

        var act = () => new SchemaDefinition(columns);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*amount*");
    }

    [Fact]
    public void GetColumn_Existing_ReturnsColumn()
    {
        var amount = new ColumnDefinition("amount", ColumnTypeCode.Decimal);
        var schema = new SchemaDefinition([amount]);

        schema.GetColumn("amount").Should().Be(amount);
    }

    [Fact]
    public void GetColumn_Missing_ReturnsNull()
    {
        var schema = new SchemaDefinition(
            [new ColumnDefinition("amount", ColumnTypeCode.Decimal)]);

        schema.GetColumn("missing").Should().BeNull();
    }

    [Fact]
    public void GetColumn_CaseSensitive()
    {
        var schema = new SchemaDefinition(
            [new ColumnDefinition("Amount", ColumnTypeCode.Decimal)]);

        schema.GetColumn("amount").Should().BeNull();
        schema.GetColumn("Amount").Should().NotBeNull();
    }

    [Fact]
    public void PrimaryKey_ReturnsColumnsInDeclarationOrder()
    {
        var schema = new SchemaDefinition(
        [
            new ColumnDefinition("name", ColumnTypeCode.String),
            new ColumnDefinition("id", ColumnTypeCode.BigInt) { IsPrimaryKey = true },
            new ColumnDefinition("region", ColumnTypeCode.String) { IsPrimaryKey = true },
        ]);

        schema.PrimaryKey.Select(c => c.Name).Should().Equal("id", "region");
    }

    [Fact]
    public void Equality_IsStructuralOverColumnSequence()
    {
        var a = new SchemaDefinition(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt),
            new ColumnDefinition("name", ColumnTypeCode.String),
        ]);
        var b = new SchemaDefinition(
        [
            new ColumnDefinition("id", ColumnTypeCode.BigInt),
            new ColumnDefinition("name", ColumnTypeCode.String),
        ]);
        var reordered = new SchemaDefinition(
        [
            new ColumnDefinition("name", ColumnTypeCode.String),
            new ColumnDefinition("id", ColumnTypeCode.BigInt),
        ]);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(reordered);
    }
}
