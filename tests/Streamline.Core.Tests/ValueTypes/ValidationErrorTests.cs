using AwesomeAssertions;
using Streamline.Core.ValueTypes;
using Xunit;

namespace Streamline.Core.Tests.ValueTypes;

public class ValidationErrorTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var error = new ValidationError(
            Column: "amount",
            AttemptedValue: "abc",
            Code: "INVALID_TYPE",
            Message: "Value 'abc' is not a valid Decimal.");

        error.Column.Should().Be("amount");
        error.AttemptedValue.Should().Be("abc");
        error.Code.Should().Be("INVALID_TYPE");
        error.Message.Should().Be("Value 'abc' is not a valid Decimal.");
    }

    [Fact]
    public void Construct_WithEmptyColumn_Succeeds()
    {
        // Row-level errors (e.g. PK_VIOLATION) carry an empty Column.
        var error = new ValidationError(
            Column: string.Empty,
            AttemptedValue: null,
            Code: "PK_VIOLATION",
            Message: "Duplicate primary key within batch.");

        error.Column.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithNullColumn_Throws()
    {
        var act = () => new ValidationError(null!, null, "X", "m");
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankCode_Throws(string code)
    {
        var act = () => new ValidationError("c", null, code, "m");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankMessage_Throws(string message)
    {
        var act = () => new ValidationError("c", null, "X", message);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new ValidationError("amount", "abc", "INVALID_TYPE", "oops");
        var b = new ValidationError("amount", "abc", "INVALID_TYPE", "oops");
        var c = new ValidationError("amount", "xyz", "INVALID_TYPE", "oops");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }
}
