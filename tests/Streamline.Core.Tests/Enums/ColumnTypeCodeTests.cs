using AwesomeAssertions;
using Streamline.Core.Enums;
using Xunit;

namespace Streamline.Core.Tests.Enums;

public class ColumnTypeCodeTests
{
    [Fact]
    public void Values_are_the_eight_documented_type_codes()
    {
        Enum.GetNames<ColumnTypeCode>().Should().BeEquivalentTo(
        [
            nameof(ColumnTypeCode.String),
            nameof(ColumnTypeCode.Integer),
            nameof(ColumnTypeCode.BigInt),
            nameof(ColumnTypeCode.Decimal),
            nameof(ColumnTypeCode.Date),
            nameof(ColumnTypeCode.Timestamp),
            nameof(ColumnTypeCode.Boolean),
            nameof(ColumnTypeCode.Uuid),
        ]);
    }
}
