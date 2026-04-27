using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Tests.Batches.StateMachine;
using Streamline.Domain.Validation;
using Xunit;

using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Domain.Tests.Validation;

public class RowValidatorTests
{
    private static SchemaDefinition Schema(params ColumnDefinition[] columns) =>
        new(columns);

    private static Record Row(params (string Key, object? Value)[] values)
    {
        var dict = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        return new Record("file.csv", 0, dict);
    }

    // ---- happy path ---------------------------------------------------

    [Fact]
    public void Validate_AllColumnsValid_ReturnsSuccessWithTypedRecord()
    {
        var schema = Schema(
            new ColumnDefinition("id", ColumnTypeCode.BigInt) { IsRequired = true },
            new ColumnDefinition("name", ColumnTypeCode.String) { IsRequired = true },
            new ColumnDefinition("amount", ColumnTypeCode.Decimal) { IsRequired = true });

        var raw = Row(("id", "42"), ("name", "ACME"), ("amount", "3.14"));

        var result = RowValidator.Validate(raw, schema);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.ValidatedRecord.Should().NotBeNull();

        result.ValidatedRecord!.GetTyped<long>("id").Should().Be(42L);
        result.ValidatedRecord.GetTyped<string>("name").Should().Be("ACME");
        result.ValidatedRecord.GetTyped<decimal>("amount").Should().Be(3.14m);
    }

    [Fact]
    public void Validate_PreservesProvenanceOnSuccess()
    {
        var schema = Schema(
            new ColumnDefinition("id", ColumnTypeCode.Integer) { IsRequired = true });
        var raw = new Record("source.csv", 17, new Dictionary<string, object?> { ["id"] = "1" });

        var result = RowValidator.Validate(raw, schema);

        result.ValidatedRecord!.SourceFileName.Should().Be("source.csv");
        result.ValidatedRecord.SourceRowIndex.Should().Be(17);
    }

    // ---- MISSING_REQUIRED ----------------------------------------------

    [Fact]
    public void Validate_RequiredColumnAbsent_ReportsMissingRequired()
    {
        var schema = Schema(
            new ColumnDefinition("id", ColumnTypeCode.Integer) { IsRequired = true });
        var raw = Row(); // 'id' absent

        var result = RowValidator.Validate(raw, schema);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ObservationCodes.MISSING_REQUIRED);
    }

    [Fact]
    public void Validate_RequiredColumnNull_ReportsMissingRequired()
    {
        var schema = Schema(
            new ColumnDefinition("id", ColumnTypeCode.Integer) { IsRequired = true });
        var raw = Row(("id", null));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ObservationCodes.MISSING_REQUIRED);
    }

    [Fact]
    public void Validate_RequiredColumnEmptyString_DoesNotReportMissing()
    {
        // Per Q8: empty string is a present value. MISSING_REQUIRED fires
        // only on null/absent. (An empty string still fails INVALID_TYPE
        // for non-string columns.)
        var schema = Schema(
            new ColumnDefinition("name", ColumnTypeCode.String) { IsRequired = true });
        var raw = Row(("name", ""));

        var result = RowValidator.Validate(raw, schema);

        result.IsValid.Should().BeTrue();
        result.ValidatedRecord!.GetTyped<string>("name").Should().Be("");
    }

    [Fact]
    public void Validate_OptionalColumnAbsent_TypedValueIsNull()
    {
        var schema = Schema(
            new ColumnDefinition("opt", ColumnTypeCode.Integer));  // IsRequired = false
        var raw = Row();

        var result = RowValidator.Validate(raw, schema);

        result.IsValid.Should().BeTrue();
        result.ValidatedRecord!.Contains("opt").Should().BeTrue();
        result.ValidatedRecord.GetTyped<int>("opt").Should().Be(0);  // default(int)
    }

    // ---- INVALID_TYPE -------------------------------------------------

    [Fact]
    public void Validate_UnparseableValue_ReportsInvalidType()
    {
        var schema = Schema(
            new ColumnDefinition("id", ColumnTypeCode.Integer) { IsRequired = true });
        var raw = Row(("id", "not a number"));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().ContainSingle(e => e.Code == ObservationCodes.INVALID_TYPE);
        result.ValidatedRecord.Should().BeNull();
    }

    // ---- INVALID_FORMAT ------------------------------------------------

    [Fact]
    public void Validate_PatternMismatch_ReportsInvalidFormat()
    {
        var schema = Schema(
            new ColumnDefinition("code", ColumnTypeCode.String)
            {
                IsRequired = true,
                Pattern = "^[YN]$",
            });
        var raw = Row(("code", "Maybe"));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().ContainSingle(e => e.Code == ObservationCodes.INVALID_FORMAT);
    }

    [Fact]
    public void Validate_PatternMatch_NoFormatError()
    {
        var schema = Schema(
            new ColumnDefinition("code", ColumnTypeCode.String)
            {
                IsRequired = true,
                Pattern = "^[YN]$",
            });
        var raw = Row(("code", "Y"));

        var result = RowValidator.Validate(raw, schema);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PatternAppliedToRawStringEvenForTypedColumn()
    {
        // A Pattern on a non-String column constrains the input shape
        // pre-parse. Here, "0042" parses fine as Integer (42) but
        // fails the Pattern that requires no leading zeros.
        var schema = Schema(
            new ColumnDefinition("id", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                Pattern = "^(0|[1-9][0-9]*)$",
            });
        var raw = Row(("id", "0042"));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().Contain(e => e.Code == ObservationCodes.INVALID_FORMAT);
    }

    // ---- INVALID_LENGTH ------------------------------------------------

    [Fact]
    public void Validate_StringExceedsMaxLength_ReportsInvalidLength()
    {
        var schema = Schema(
            new ColumnDefinition("name", ColumnTypeCode.String)
            {
                IsRequired = true,
                MaxLength = 5,
            });
        var raw = Row(("name", "TooLong"));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().ContainSingle(e => e.Code == ObservationCodes.INVALID_LENGTH);
    }

    [Fact]
    public void Validate_StringAtMaxLength_NoLengthError()
    {
        var schema = Schema(
            new ColumnDefinition("name", ColumnTypeCode.String)
            {
                IsRequired = true,
                MaxLength = 5,
            });
        var raw = Row(("name", "EXACT"));

        RowValidator.Validate(raw, schema).IsValid.Should().BeTrue();
    }

    // ---- INVALID_RANGE -------------------------------------------------

    [Fact]
    public void Validate_NumericBelowMin_ReportsInvalidRange()
    {
        var schema = Schema(
            new ColumnDefinition("age", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                MinValue = "0",
                MaxValue = "120",
            });
        var raw = Row(("age", "-1"));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().ContainSingle(e =>
            e.Code == ObservationCodes.INVALID_RANGE
            && e.Message.Contains("below"));
    }

    [Fact]
    public void Validate_NumericAboveMax_ReportsInvalidRange()
    {
        var schema = Schema(
            new ColumnDefinition("age", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                MinValue = "0",
                MaxValue = "120",
            });
        var raw = Row(("age", "200"));

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().ContainSingle(e =>
            e.Code == ObservationCodes.INVALID_RANGE
            && e.Message.Contains("above"));
    }

    [Fact]
    public void Validate_NumericInRange_NoRangeError()
    {
        var schema = Schema(
            new ColumnDefinition("age", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                MinValue = "0",
                MaxValue = "120",
            });
        RowValidator.Validate(Row(("age", "0")), schema).IsValid.Should().BeTrue();
        RowValidator.Validate(Row(("age", "120")), schema).IsValid.Should().BeTrue();
        RowValidator.Validate(Row(("age", "60")), schema).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DateBeforeMin_ReportsInvalidRange()
    {
        var schema = Schema(
            new ColumnDefinition("d", ColumnTypeCode.Date)
            {
                IsRequired = true,
                MinValue = "2025-01-01",
            });
        var raw = Row(("d", "2024-12-31"));

        RowValidator.Validate(raw, schema).Errors.Should().Contain(
            e => e.Code == ObservationCodes.INVALID_RANGE);
    }

    [Fact]
    public void Validate_UnparseableMinValue_SilentlyIgnoresBound()
    {
        // Defensive: a registry author who left a malformed bound
        // doesn't break row data. The bound is silently skipped;
        // RegistryValidator catches the registry-side issue at
        // config time (when that check lands).
        var schema = Schema(
            new ColumnDefinition("age", ColumnTypeCode.Integer)
            {
                IsRequired = true,
                MinValue = "not-a-number",  // registry author error
            });
        var raw = Row(("age", "5"));

        RowValidator.Validate(raw, schema).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RangeNotAppliedToString()
    {
        // Strings don't have a meaningful natural range; use Pattern.
        var schema = Schema(
            new ColumnDefinition("s", ColumnTypeCode.String)
            {
                IsRequired = true,
                MinValue = "AAA",
                MaxValue = "ZZZ",
            });
        var raw = Row(("s", "@@@"));  // outside lexicographic range

        RowValidator.Validate(raw, schema).IsValid.Should().BeTrue();
    }

    // ---- combinatorial -------------------------------------------------

    [Fact]
    [PreventsPredecessorBug("PB-2",
        "predecessor's RowValidator returned on the first error per row; operators " +
        "debugging quarantines saw incomplete information. Streamline's RowValidator " +
        "accumulates every error before returning a Failure result.")]
    public void Validate_RowWithMultipleViolations_ReturnsAllErrors()
    {
        var schema = Schema(
            new ColumnDefinition("required", ColumnTypeCode.Integer) { IsRequired = true },
            new ColumnDefinition("typed", ColumnTypeCode.Integer) { IsRequired = true },
            new ColumnDefinition("ranged", ColumnTypeCode.Integer)
                { IsRequired = true, MinValue = "0", MaxValue = "10" },
            new ColumnDefinition("longstr", ColumnTypeCode.String)
                { IsRequired = true, MaxLength = 3 },
            new ColumnDefinition("patterned", ColumnTypeCode.String)
                { IsRequired = true, Pattern = "^[A-Z]+$" });

        var raw = Row(
            // 'required' absent → MISSING_REQUIRED
            ("typed", "abc"),       // INVALID_TYPE
            ("ranged", "999"),      // INVALID_RANGE
            ("longstr", "TooLong"), // INVALID_LENGTH
            ("patterned", "lower")  // INVALID_FORMAT
        );

        var result = RowValidator.Validate(raw, schema);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(5);
        result.Errors.Select(e => e.Code).Should().BeEquivalentTo(
        [
            ObservationCodes.MISSING_REQUIRED,
            ObservationCodes.INVALID_TYPE,
            ObservationCodes.INVALID_RANGE,
            ObservationCodes.INVALID_LENGTH,
            ObservationCodes.INVALID_FORMAT,
        ]);
    }

    [Fact]
    public void Validate_SingleColumnWithMultipleViolations_ReturnsAll()
    {
        // A single column can produce both INVALID_FORMAT and
        // INVALID_LENGTH (and INVALID_TYPE, but length doesn't apply
        // to non-strings). Confirm we don't short-circuit per column.
        var schema = Schema(
            new ColumnDefinition("c", ColumnTypeCode.String)
            {
                IsRequired = true,
                MaxLength = 3,
                Pattern = "^[A-Z]+$",
            });
        var raw = Row(("c", "lowercase"));  // bad pattern AND too long

        var result = RowValidator.Validate(raw, schema);

        result.Errors.Should().Contain(e => e.Code == ObservationCodes.INVALID_FORMAT);
        result.Errors.Should().Contain(e => e.Code == ObservationCodes.INVALID_LENGTH);
    }

    // ---- argument validation ------------------------------------------

    [Fact]
    public void Validate_NullRecord_Throws()
    {
        var schema = Schema();
        var act = () => RowValidator.Validate(null!, schema);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_NullSchema_Throws()
    {
        var raw = Row();
        var act = () => RowValidator.Validate(raw, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
