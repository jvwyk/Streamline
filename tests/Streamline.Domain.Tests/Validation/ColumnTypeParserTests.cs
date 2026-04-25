using AwesomeAssertions;
using Streamline.Core.Enums;
using Streamline.Domain.Validation;
using Xunit;

namespace Streamline.Domain.Tests.Validation;

public class ColumnTypeParserTests
{
    // ---- String -------------------------------------------------------

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" leading space")]
    public void TryParse_String_AcceptsAnyNonNull(string raw)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.String, raw, out var parsed);

        success.Should().BeTrue();
        parsed.Should().Be(raw);  // String preserves whitespace verbatim
    }

    [Fact]
    public void TryParse_String_NullFails()
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.String, null, out var parsed);
        success.Should().BeFalse();
        parsed.Should().BeNull();
    }

    // ---- Integer ------------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void TryParse_Integer_AcceptsValidInts(string raw, int expected)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Integer, raw, out var parsed);
        success.Should().BeTrue();
        parsed.Should().Be(expected);
        parsed.Should().BeOfType<int>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("2147483648")]   // overflow
    [InlineData(" 42")]          // leading space — parser does not trim
    [InlineData("42 ")]          // trailing space
    public void TryParse_Integer_RejectsInvalid(string raw)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Integer, raw, out var parsed);
        success.Should().BeFalse();
        parsed.Should().BeNull();
    }

    // ---- BigInt -------------------------------------------------------

    [Theory]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("0", 0L)]
    public void TryParse_BigInt_AcceptsValidLongs(string raw, long expected)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.BigInt, raw, out var parsed);
        success.Should().BeTrue();
        parsed.Should().Be(expected);
        parsed.Should().BeOfType<long>();
    }

    [Theory]
    [InlineData("9223372036854775808")]  // overflow
    [InlineData("not a number")]
    public void TryParse_BigInt_RejectsInvalid(string raw)
    {
        ColumnTypeParser.TryParse(ColumnTypeCode.BigInt, raw, out _).Should().BeFalse();
    }

    // ---- Decimal ------------------------------------------------------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("3.14", 3.14)]
    [InlineData("-2.5", -2.5)]
    public void TryParse_Decimal_AcceptsValid(string raw, double expectedAsDouble)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Decimal, raw, out var parsed);
        success.Should().BeTrue();
        parsed.Should().BeOfType<decimal>();
        ((decimal)parsed!).Should().Be((decimal)expectedAsDouble);
    }

    [Theory]
    [InlineData("3,14")]      // German decimal comma — rejected under InvariantCulture
    [InlineData("1,234.56")]  // thousand separator — rejected under strict style
    [InlineData("abc")]
    [InlineData("")]
    public void TryParse_Decimal_RejectsInvalid(string raw)
    {
        ColumnTypeParser.TryParse(ColumnTypeCode.Decimal, raw, out _).Should().BeFalse();
    }

    // ---- Date ---------------------------------------------------------

    [Theory]
    [InlineData("2025-01-15", 2025, 1, 15)]
    [InlineData("2024-12-31", 2024, 12, 31)]
    public void TryParse_Date_AcceptsIsoDates(string raw, int year, int month, int day)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Date, raw, out var parsed);
        success.Should().BeTrue();
        parsed.Should().BeOfType<DateOnly>();
        ((DateOnly)parsed!).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2025-13-01")]  // invalid month
    [InlineData("")]
    public void TryParse_Date_RejectsInvalid(string raw)
    {
        ColumnTypeParser.TryParse(ColumnTypeCode.Date, raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_Date_ProducesDateOnlyNotDateTime()
    {
        // Confirm the Q3 contract — Date never round-trips through DateTime.
        ColumnTypeParser.TryParse(ColumnTypeCode.Date, "2025-01-15", out var parsed);
        parsed.Should().BeOfType<DateOnly>();
        parsed.Should().NotBeOfType<DateTime>();
    }

    // ---- Timestamp ----------------------------------------------------

    [Theory]
    [InlineData("2025-01-15T10:30:00Z")]
    [InlineData("2025-01-15T10:30:00+02:00")]
    [InlineData("2025-01-15T10:30:00.123Z")]
    public void TryParse_Timestamp_AcceptsIsoTimestamps(string raw)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Timestamp, raw, out var parsed);
        success.Should().BeTrue();
        parsed.Should().BeOfType<DateTimeOffset>();
    }

    [Fact]
    public void TryParse_Timestamp_AssumesUtcWhenOffsetMissing()
    {
        // A timestamp without an explicit offset is treated as UTC,
        // not as local time. This is the safe default — silent
        // server-locale interpretation is exactly the bug Q3 calls
        // out.
        ColumnTypeParser.TryParse(
            ColumnTypeCode.Timestamp, "2025-01-15T10:30:00", out var parsed);

        parsed.Should().BeOfType<DateTimeOffset>();
        ((DateTimeOffset)parsed!).Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TryParse_Timestamp_ProducesDateTimeOffsetNotDateTime()
    {
        ColumnTypeParser.TryParse(
            ColumnTypeCode.Timestamp, "2025-01-15T10:30:00Z", out var parsed);
        parsed.Should().BeOfType<DateTimeOffset>();
        parsed.Should().NotBeOfType<DateTime>();
    }

    [Theory]
    [InlineData("not a timestamp")]
    [InlineData("")]
    public void TryParse_Timestamp_RejectsInvalid(string raw)
    {
        ColumnTypeParser.TryParse(ColumnTypeCode.Timestamp, raw, out _).Should().BeFalse();
    }

    // ---- Boolean ------------------------------------------------------

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("y", true)]
    [InlineData("Y", true)]
    [InlineData("n", false)]
    [InlineData("N", false)]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("no", false)]
    [InlineData("NO", false)]
    public void TryParse_Boolean_AcceptsCommonEtlEncodings(string raw, bool expected)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Boolean, raw, out var parsed);
        success.Should().BeTrue($"'{raw}' should parse as {expected}");
        parsed.Should().Be(expected);
        parsed.Should().BeOfType<bool>();
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("t")]      // not in the accepted set
    [InlineData("f")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("")]
    public void TryParse_Boolean_RejectsUnrecognisedTokens(string raw)
    {
        ColumnTypeParser.TryParse(ColumnTypeCode.Boolean, raw, out _).Should().BeFalse();
    }

    // ---- UUID ---------------------------------------------------------

    [Theory]
    [InlineData("12345678-1234-1234-1234-123456789012")]   // hyphenated
    [InlineData("12345678123412341234123456789012")]       // no hyphens
    [InlineData("{12345678-1234-1234-1234-123456789012}")] // braced
    [InlineData("(12345678-1234-1234-1234-123456789012)")] // parenthesised
    public void TryParse_Uuid_AcceptsStandardFormats(string raw)
    {
        var success = ColumnTypeParser.TryParse(ColumnTypeCode.Uuid, raw, out var parsed);
        success.Should().BeTrue();
        parsed.Should().BeOfType<Guid>();
    }

    [Theory]
    [InlineData("not a guid")]
    [InlineData("12345678-1234-1234-1234")]   // too short
    [InlineData("")]
    public void TryParse_Uuid_RejectsInvalid(string raw)
    {
        ColumnTypeParser.TryParse(ColumnTypeCode.Uuid, raw, out _).Should().BeFalse();
    }

    // ---- Cross-type contract -------------------------------------------

    [Theory]
    [InlineData(ColumnTypeCode.Integer)]
    [InlineData(ColumnTypeCode.BigInt)]
    [InlineData(ColumnTypeCode.Decimal)]
    [InlineData(ColumnTypeCode.Date)]
    [InlineData(ColumnTypeCode.Timestamp)]
    [InlineData(ColumnTypeCode.Boolean)]
    [InlineData(ColumnTypeCode.Uuid)]
    public void TryParse_NonStringTypes_RejectNullAndEmpty(ColumnTypeCode typeCode)
    {
        ColumnTypeParser.TryParse(typeCode, null, out _).Should().BeFalse();
        ColumnTypeParser.TryParse(typeCode, "", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_DoesNotTrimNumericInput()
    {
        // Pinned contract: parser does not trim. Reader is responsible
        // for clean values per its config.
        ColumnTypeParser.TryParse(ColumnTypeCode.Integer, "  42  ", out _).Should().BeFalse();
        ColumnTypeParser.TryParse(ColumnTypeCode.Decimal, "  3.14  ", out _).Should().BeFalse();
    }
}
