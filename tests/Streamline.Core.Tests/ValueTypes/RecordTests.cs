using AwesomeAssertions;
using Streamline.Core.ValueTypes;
using Xunit;

// Xunit also exports a Record type (for exception assertions). Our
// production Streamline.Core.ValueTypes.Record is what this file tests.
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Core.Tests.ValueTypes;

public class RecordTests
{
    [Fact]
    public void Construct_WithValues_CarriesProvenanceAndPayload()
    {
        var record = new Record(
            sourceFileName: "brokers_2025_01.csv",
            sourceRowIndex: 42,
            values: new Dictionary<string, object?>
            {
                ["id"] = "17",
                ["name"] = "ACME",
            });

        record.SourceFileName.Should().Be("brokers_2025_01.csv");
        record.SourceRowIndex.Should().Be(42);
        record.Values.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construct_WithBlankFileName_Throws(string name)
    {
        var act = () => new Record(name, 0, []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_WithNegativeRowIndex_Throws()
    {
        var act = () => new Record("file.csv", -1, []);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_WithDuplicateColumnKey_Throws()
    {
        var values = new[]
        {
            KeyValuePair.Create<string, object?>("id", "1"),
            KeyValuePair.Create<string, object?>("id", "2"),
        };

        var act = () => new Record("file.csv", 0, values);
        act.Should().Throw<ArgumentException>().WithMessage("*id*");
    }

    [Fact]
    public void GetRaw_ForStringValue_ReturnsString()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["name"] = "ACME" });

        record.GetRaw("name").Should().Be("ACME");
    }

    [Fact]
    public void GetRaw_ForNullStoredValue_ReturnsNull()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["name"] = null });

        record.GetRaw("name").Should().BeNull();
        record.Contains("name").Should().BeTrue();
    }

    [Fact]
    public void GetRaw_ForMissingColumn_ReturnsNull()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["name"] = "ACME" });

        record.GetRaw("missing").Should().BeNull();
        record.Contains("missing").Should().BeFalse();
    }

    [Fact]
    public void GetRaw_ForTypedValue_Throws()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["amount"] = 42.0m });

        var act = () => record.GetRaw("amount");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Decimal*GetTyped*");
    }

    [Fact]
    public void GetTyped_ForMatchingType_ReturnsValue()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["amount"] = 42.0m });

        record.GetTyped<decimal>("amount").Should().Be(42.0m);
    }

    [Fact]
    public void GetTyped_ForNullStoredValue_ReturnsDefault()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["amount"] = null });

        record.GetTyped<decimal>("amount").Should().Be(default);
        record.Contains("amount").Should().BeTrue();
    }

    [Fact]
    public void GetTyped_ForMismatchedType_Throws()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["amount"] = "42" });

        var act = () => record.GetTyped<decimal>("amount");
        act.Should().Throw<InvalidCastException>()
            .WithMessage("*String*Decimal*");
    }

    [Fact]
    public void GetTyped_ForMissingColumn_ReturnsDefault()
    {
        var record = new Record("f.csv", 0, []);

        record.GetTyped<decimal>("missing").Should().Be(default);
    }

    [Fact]
    public void Contains_DistinguishesMissingFromNull()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["present"] = null });

        record.Contains("present").Should().BeTrue();
        record.Contains("absent").Should().BeFalse();
    }

    [Fact]
    public void Values_DictionaryIsImmutable()
    {
        var record = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["name"] = "ACME" });

        // Ensure the exposed dictionary cannot be downcast to a mutable one.
        record.Values.Should().BeAssignableTo<System.Collections.Immutable.ImmutableDictionary<string, object?>>();
    }

    [Fact]
    public void Equality_IsStructuralOverProvenanceAndValues()
    {
        var a = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["name"] = "ACME", ["id"] = 1 });
        var b = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "ACME" });
        var differentRow = new Record("f.csv", 1,
            new Dictionary<string, object?> { ["name"] = "ACME", ["id"] = 1 });

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(differentRow);
    }

    /// <summary>
    /// Regression guard: equality must not depend on the insertion
    /// order of values into the underlying ImmutableDictionary. The
    /// default record-generated Equals would delegate to
    /// ImmutableDictionary.Equals, which is reference-based and
    /// would fail this test. The overridden Equals compares element
    /// by element. If a future contributor "simplifies" by removing
    /// the override, this test catches it.
    /// </summary>
    [Fact]
    public void Equality_IgnoresInsertionOrderOfValues()
    {
        var insertAscending = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, ["c"] = 3 });
        var insertDescending = new Record("f.csv", 0,
            new Dictionary<string, object?> { ["c"] = 3, ["b"] = 2, ["a"] = 1 });

        insertAscending.Should().Be(insertDescending);
        insertAscending.GetHashCode().Should().Be(insertDescending.GetHashCode());
    }
}
