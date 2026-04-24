using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Xunit;

namespace Streamline.Core.Tests.ValueTypes;

public class ColumnDefinitionTests
{
    [Fact]
    public void Construct_WithNameAndTypeCode_Succeeds()
    {
        var col = new ColumnDefinition("amount", ColumnTypeCode.Decimal);

        col.Name.Should().Be("amount");
        col.TypeCode.Should().Be(ColumnTypeCode.Decimal);
        col.IsRequired.Should().BeFalse();
        col.IsPrimaryKey.Should().BeFalse();
        col.MaxLength.Should().BeNull();
        col.MinValue.Should().BeNull();
        col.MaxValue.Should().BeNull();
        col.Pattern.Should().BeNull();
        col.FkReference.Should().BeNull();
        col.TargetColumn.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankName_Throws(string name)
    {
        var act = () => new ColumnDefinition(name, ColumnTypeCode.String);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InitProperties_SetEveryOptionalField()
    {
        var fk = new FkReference("broker", "id", FkEnforcementMode.Always);
        var col = new ColumnDefinition("broker_id", ColumnTypeCode.BigInt)
        {
            IsRequired = true,
            IsPrimaryKey = true,
            MaxLength = null,
            MinValue = "1",
            MaxValue = "9999",
            Pattern = null,
            FkReference = fk,
            TargetColumn = "broker_id_target",
        };

        col.IsRequired.Should().BeTrue();
        col.IsPrimaryKey.Should().BeTrue();
        col.MinValue.Should().Be("1");
        col.MaxValue.Should().Be("9999");
        col.FkReference.Should().Be(fk);
        col.TargetColumn.Should().Be("broker_id_target");
    }

    [Fact]
    public void DescribeInvariantViolations_HappyPath_IsEmpty()
    {
        var col = new ColumnDefinition("name", ColumnTypeCode.String)
        {
            MaxLength = 50,
            Pattern = @"^[A-Za-z]+$",
        };

        col.DescribeInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void DescribeInvariantViolations_NegativeMaxLength_Reports()
    {
        var col = new ColumnDefinition("name", ColumnTypeCode.String)
        {
            MaxLength = -1,
        };

        col.DescribeInvariantViolations().Should().ContainSingle()
            .Which.Should().Contain("MaxLength must be non-negative");
    }

    [Fact]
    public void DescribeInvariantViolations_InvalidRegexPattern_Reports()
    {
        var col = new ColumnDefinition("name", ColumnTypeCode.String)
        {
            Pattern = "[unterminated",
        };

        col.DescribeInvariantViolations().Should().ContainSingle()
            .Which.Should().Contain("not a valid regex");
    }

    [Fact]
    public void DescribeInvariantViolations_MaxLengthOnNonStringColumn_Reports()
    {
        var col = new ColumnDefinition("amount", ColumnTypeCode.Decimal)
        {
            MaxLength = 10,
        };

        col.DescribeInvariantViolations().Should().ContainSingle()
            .Which.Should().Contain("MaxLength is only valid for String columns");
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new ColumnDefinition("amount", ColumnTypeCode.Decimal) { IsRequired = true };
        var b = new ColumnDefinition("amount", ColumnTypeCode.Decimal) { IsRequired = true };
        var c = new ColumnDefinition("amount", ColumnTypeCode.Decimal) { IsRequired = false };

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
