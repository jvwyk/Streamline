using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Detection;
using Xunit;

namespace Streamline.Domain.Tests.Detection;

public class SchemaDriftDetectorTests
{
    private static SchemaDefinition Schema(params (string Name, bool IsRequired)[] columns) =>
        new(columns.Select(c => new ColumnDefinition(c.Name, ColumnTypeCode.String) { IsRequired = c.IsRequired }));

    [Fact]
    public void Detect_PerfectMatch_IsEmpty()
    {
        var schema = Schema(("id", true), ("name", false));
        var headers = new[] { "id", "name" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        report.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Detect_NewColumnInFile_AppearsInNewColumns()
    {
        var schema = Schema(("id", true));
        var headers = new[] { "id", "extra" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        report.NewColumns.Should().Equal("extra");
        report.MissingRequiredColumns.Should().BeEmpty();
        report.MissingOptionalColumns.Should().BeEmpty();
    }

    [Fact]
    public void Detect_RequiredColumnMissingFromFile_AppearsInMissingRequired()
    {
        var schema = Schema(("id", true), ("name", true));
        var headers = new[] { "id" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        report.MissingRequiredColumns.Should().Equal("name");
        report.MissingOptionalColumns.Should().BeEmpty();
        report.NewColumns.Should().BeEmpty();
    }

    [Fact]
    public void Detect_OptionalColumnMissingFromFile_AppearsInMissingOptional()
    {
        var schema = Schema(("id", true), ("description", false));
        var headers = new[] { "id" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        report.MissingOptionalColumns.Should().Equal("description");
        report.MissingRequiredColumns.Should().BeEmpty();
        report.NewColumns.Should().BeEmpty();
    }

    [Fact]
    public void Detect_MultipleDimensionsAtOnce()
    {
        var schema = Schema(("id", true), ("name", true), ("notes", false));
        var headers = new[] { "id", "extra1", "extra2" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        report.NewColumns.Should().BeEquivalentTo(new[] { "extra1", "extra2" });
        report.MissingRequiredColumns.Should().Equal("name");
        report.MissingOptionalColumns.Should().Equal("notes");
    }

    [Fact]
    public void Detect_HeaderOrderInFile_PreservedInNewColumns()
    {
        var schema = Schema(("id", true));
        var headers = new[] { "id", "z_extra", "a_extra" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        // Order matches file order, not alphabetical — operators
        // looking at the report see the columns in the same order
        // they appear in the file.
        report.NewColumns.Should().Equal("z_extra", "a_extra");
    }

    [Fact]
    public void Detect_DuplicateNewColumnInFile_DeduplicatedInReport()
    {
        var schema = Schema(("id", true));
        var headers = new[] { "id", "extra", "extra" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        report.NewColumns.Should().HaveCount(1).And.Contain("extra");
    }

    [Fact]
    public void Detect_DefaultsToOrdinalCaseSensitive()
    {
        var schema = Schema(("ID", true));
        var headers = new[] { "id" };

        var report = SchemaDriftDetector.Detect(headers, schema);

        // "id" doesn't match "ID" under ordinal — both surface as drift.
        report.NewColumns.Should().Equal("id");
        report.MissingRequiredColumns.Should().Equal("ID");
    }

    [Fact]
    public void Detect_WithCaseInsensitiveComparer_TreatsCaseDifferenceAsMatch()
    {
        var schema = Schema(("ID", true));
        var headers = new[] { "id" };

        var report = SchemaDriftDetector.Detect(
            headers, schema, StringComparer.OrdinalIgnoreCase);

        report.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Detect_NullHeaders_Throws()
    {
        var schema = Schema(("id", true));
        var act = () => SchemaDriftDetector.Detect(null!, schema);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Detect_NullSchema_Throws()
    {
        var act = () => SchemaDriftDetector.Detect(new[] { "id" }, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Detect_EmptyHeadersAndSchema_IsEmpty()
    {
        var schema = new SchemaDefinition([]);
        var headers = Array.Empty<string>();

        SchemaDriftDetector.Detect(headers, schema).IsEmpty.Should().BeTrue();
    }
}
