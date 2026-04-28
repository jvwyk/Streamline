using AwesomeAssertions;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Files;
using Xunit;

namespace Streamline.Domain.Tests.Files;

public class IngestionUnitTests
{
    [Fact]
    public void Construct_WithStreamAndEntryName_Succeeds()
    {
        using var stream = new MemoryStream();
        var unit = new IngestionUnit(stream, "data.csv");

        unit.Content.Should().BeSameAs(stream);
        unit.EntryName.Should().Be("data.csv");
        unit.SuggestedMapping.Should().BeNull();
        unit.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void Construct_WithSuggestedMappingAndMetadata_PreservesThem()
    {
        using var stream = new MemoryStream();
        var mapping = new FileMapping(@"^data\.csv$", "broker", "delimited");

        var unit = new IngestionUnit(
            stream,
            "data.csv",
            suggestedMapping: mapping,
            metadata: new Dictionary<string, object?>
            {
                ["zip_entry_compressed_size"] = 1234L,
                ["zip_entry_method"] = "deflate",
            });

        unit.SuggestedMapping.Should().Be(mapping);
        unit.Metadata.Should().HaveCount(2);
        unit.Metadata["zip_entry_method"].Should().Be("deflate");
    }

    [Fact]
    public void Construct_WithNullStream_Throws()
    {
        var act = () => new IngestionUnit(null!, "data.csv");
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Construct_WithBlankEntryName_Throws(string entryName)
    {
        using var stream = new MemoryStream();
        var act = () => new IngestionUnit(stream, entryName);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithBlankMetadataKey_Throws()
    {
        using var stream = new MemoryStream();
        var bad = new[] { KeyValuePair.Create<string, object?>(" ", "v") };

        var act = () => new IngestionUnit(stream, "data.csv", metadata: bad);
        act.Should().Throw<ArgumentException>().WithMessage("*blank*");
    }

    [Fact]
    public void Construct_WithDuplicateMetadataKey_Throws()
    {
        using var stream = new MemoryStream();
        var bad = new[]
        {
            KeyValuePair.Create<string, object?>("k", 1),
            KeyValuePair.Create<string, object?>("k", 2),
        };

        var act = () => new IngestionUnit(stream, "data.csv", metadata: bad);
        act.Should().Throw<ArgumentException>().WithMessage("*k*");
    }

    [Fact]
    public void Stream_IsCapturedByReference_NotCopied()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var unit = new IngestionUnit(stream, "data.csv");

        // Same instance — the dispatcher owns the stream's lifetime
        // and the engine reads from this exact stream.
        unit.Content.Should().BeSameAs(stream);
    }
}
