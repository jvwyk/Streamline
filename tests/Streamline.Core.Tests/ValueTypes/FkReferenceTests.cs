using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Xunit;

namespace Streamline.Core.Tests.ValueTypes;

public class FkReferenceTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var fk = new FkReference("broker", "broker_id", FkEnforcementMode.Always);

        fk.ParentTable.Should().Be("broker");
        fk.ParentColumn.Should().Be("broker_id");
        fk.EnforcementMode.Should().Be(FkEnforcementMode.Always);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_WithBlankParentTable_Throws(string parentTable)
    {
        var act = () => new FkReference(parentTable, "id", FkEnforcementMode.Always);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_WithBlankParentColumn_Throws(string parentColumn)
    {
        var act = () => new FkReference("broker", parentColumn, FkEnforcementMode.Always);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new FkReference("broker", "id", FkEnforcementMode.Always);
        var b = new FkReference("broker", "id", FkEnforcementMode.Always);
        var c = new FkReference("broker", "id", FkEnforcementMode.Never);

        a.Should().Be(b);
        a.Should().NotBe(c);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
