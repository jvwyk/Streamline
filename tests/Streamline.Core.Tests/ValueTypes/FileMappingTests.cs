using AwesomeAssertions;
using Streamline.Core.ValueTypes;
using Xunit;

namespace Streamline.Core.Tests.ValueTypes;

public class FileMappingTests
{
    [Fact]
    public void Construct_WithValidInputs_Succeeds()
    {
        var mapping = new FileMapping(
            filenamePattern: @"^brokers_\d{4}_\d{2}\.csv$",
            targetTable: "broker",
            format: "delimited",
            readerConfig: new Dictionary<string, string>
            {
                ["delimiter"] = ",",
                ["skip_rows"] = "0",
            });

        mapping.TargetTable.Should().Be("broker");
        mapping.Format.Should().Be("delimited");
        mapping.ReaderConfig.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankFilenamePattern_Throws(string pattern)
    {
        var act = () => new FileMapping(pattern, "t", "delimited");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankTargetTable_Throws(string target)
    {
        var act = () => new FileMapping(".*", target, "delimited");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankFormat_Throws(string format)
    {
        var act = () => new FileMapping(".*", "t", format);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithInvalidRegex_Throws()
    {
        var act = () => new FileMapping("[unterminated", "t", "delimited");
        act.Should().Throw<ArgumentException>()
            .WithMessage("*not a valid regex*");
    }

    [Fact]
    public void Construct_WithNullReaderConfig_UsesEmpty()
    {
        var mapping = new FileMapping(".*", "t", "delimited", readerConfig: null);
        mapping.ReaderConfig.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithDuplicateReaderConfigKey_Throws()
    {
        var config = new[]
        {
            KeyValuePair.Create("delimiter", ","),
            KeyValuePair.Create("delimiter", "\t"),
        };

        var act = () => new FileMapping(".*", "t", "delimited", config);
        act.Should().Throw<ArgumentException>().WithMessage("*delimiter*");
    }

    [Fact]
    public void Matches_RespectsPattern()
    {
        var mapping = new FileMapping(@"^brokers_\d{4}\.csv$", "broker", "delimited");

        mapping.Matches("brokers_2025.csv").Should().BeTrue();
        mapping.Matches("brokers_25.csv").Should().BeFalse();
        mapping.Matches("other.csv").Should().BeFalse();
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = new FileMapping(".*", "t", "delimited",
            new Dictionary<string, string> { ["k"] = "v" });
        var b = new FileMapping(".*", "t", "delimited",
            new Dictionary<string, string> { ["k"] = "v" });
        var different = new FileMapping(".*", "t", "delimited",
            new Dictionary<string, string> { ["k"] = "w" });

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(different);
    }
}
