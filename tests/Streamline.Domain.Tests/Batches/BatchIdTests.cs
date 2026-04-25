using AwesomeAssertions;
using Streamline.Domain.Batches;
using Xunit;

namespace Streamline.Domain.Tests.Batches;

public class BatchIdTests
{
    [Fact]
    public void Construct_WithValue_PreservesValue()
    {
        var id = new BatchId("batch-1");
        id.Value.Should().Be("batch-1");
        id.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_WithBlankValue_Throws(string value)
    {
        var act = () => new BatchId(value);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void New_ProducesValidBatchId()
    {
        var id = BatchId.New();

        id.IsValid.Should().BeTrue();
        id.Value.Should().HaveLength(32);
        id.Value.Should().MatchRegex("^[0-9a-f]{32}$",
            "BatchId.New uses Guid.NewGuid().ToString(\"N\") format");
    }

    /// <summary>
    /// Pinning the distinctness contract: 100 sequential calls produce
    /// 100 distinct values. Catches a future regression where someone
    /// accidentally caches the underlying Guid (e.g., a static
    /// readonly field initialised once).
    /// </summary>
    [Fact]
    public void New_ProducesOneHundredDistinctIdsInARow()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => BatchId.New()).ToArray();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Default_HasNullValueAndIsNotValid()
    {
        var id = default(BatchId);
        id.IsValid.Should().BeFalse();
        id.Value.Should().BeNull();
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        var id = new BatchId("abc-123");
        id.ToString().Should().Be("abc-123");
    }

    [Fact]
    public void ToString_OnDefault_ReturnsEmpty()
    {
        // Avoids NullReferenceException in callers that log a default
        // BatchId before noticing the validity issue.
        default(BatchId).ToString().Should().BeEmpty();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = new BatchId("same");
        var b = new BatchId("same");
        var c = new BatchId("other");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }
}
