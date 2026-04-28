using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Transforms;
using Xunit;

namespace Streamline.Domain.Tests.Transforms;

public class TransformReferenceTests
{
    [Fact]
    public void Construct_WithAllFields_Succeeds()
    {
        var reference = new TransformReference(
            Kind: TransformKind.SqlFunction,
            Reference: "domain.transform_broker_v3",
            DestinationTable: "intembeko.dim_broker",
            Invocation: TransformInvocation.PerBatch);

        reference.Kind.Should().Be(TransformKind.SqlFunction);
        reference.Reference.Should().Be("domain.transform_broker_v3");
        reference.DestinationTable.Should().Be("intembeko.dim_broker");
        reference.Invocation.Should().Be(TransformInvocation.PerBatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankReference_Throws(string reference)
    {
        var act = () => new TransformReference(
            TransformKind.SqlFunction, reference, "schema.table", TransformInvocation.PerBatch);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankDestinationTable_Throws(string destinationTable)
    {
        var act = () => new TransformReference(
            TransformKind.SqlFunction, "func", destinationTable, TransformInvocation.PerBatch);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new TransformReference(
            TransformKind.CSharp, "Type, Asm", "schema.table", TransformInvocation.PerBatch);
        var b = new TransformReference(
            TransformKind.CSharp, "Type, Asm", "schema.table", TransformInvocation.PerBatch);
        var different = new TransformReference(
            TransformKind.SqlFunction, "Type, Asm", "schema.table", TransformInvocation.PerBatch);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }
}
