using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class TransformKindTests
{
    [Fact]
    public void Values_are_the_two_documented_kinds()
    {
        Enum.GetNames<TransformKind>().Should().BeEquivalentTo(
        [
            nameof(TransformKind.SqlFunction),
            nameof(TransformKind.CSharp),
        ]);
    }
}
