using System.Globalization;
using Streamline.Core.Enums;

namespace Streamline.Domain.Validation;

/// <summary>
/// Pure-function gate over <see cref="ColumnTypeCode"/> to .NET-typed
/// value coercion. Lives in <c>Streamline.Domain.Validation</c>
/// because parsing is behaviour, not vocabulary — Core stays flat.
/// Invoked by <c>RowValidator</c> (commit 3 of sub-phase 1e) for the
/// <c>INVALID_TYPE</c> check.
/// </summary>
/// <remarks>
/// <para>
/// <b>Culture.</b> v1 parses every numeric and temporal value with
/// <see cref="CultureInfo.InvariantCulture"/>. Predictable, repeatable,
/// no surprise from server locale. Per-file or per-column culture
/// becomes a reader-config concern in Phase 3 if a real job needs
/// non-English number/date conventions.
/// </para>
/// <para>
/// <b>Trim.</b> Parser does NOT trim. The reader is responsible for
/// emitting clean values per its config. Preserving whitespace is
/// sometimes meaningful (codes like <c>" AAA"</c>); pushing
/// normalisation to the reader keeps the validator purely
/// structural.
/// </para>
/// <para>
/// <b>Empty / null.</b> Parser maps null and empty string to a parse
/// failure for non-string types. <c>RowValidator</c> distinguishes
/// "missing" (MISSING_REQUIRED) from "present but unparseable"
/// (INVALID_TYPE) before calling this; the parser itself only sees
/// values that the validator already knows are present.
/// </para>
/// <para>
/// <b>Date vs Timestamp.</b>
/// <see cref="ColumnTypeCode.Date"/> parses to <see cref="DateOnly"/>
/// (no time component, no timezone — the right answer for "this is
/// a calendar date"). <see cref="ColumnTypeCode.Timestamp"/> parses
/// to <see cref="DateTimeOffset"/>; we never round-trip through a
/// naked <see cref="DateTime"/> because silent UTC-vs-local
/// confusion is the worst class of date bug.
/// </para>
/// <para>
/// <b>Boolean.</b> Accepts the four common ETL serialisations
/// case-insensitively: <c>true</c> / <c>false</c>, <c>1</c> / <c>0</c>,
/// <c>y</c> / <c>n</c>, <c>yes</c> / <c>no</c>. Anything else fails
/// INVALID_TYPE. Centralised here rather than per-reader because
/// boolean encodings are a known mess; per-reader handling
/// produces inconsistency.
/// </para>
/// </remarks>
public static class ColumnTypeParser
{
    /// <summary>
    /// Attempt to coerce <paramref name="raw"/> to the .NET type
    /// corresponding to <paramref name="typeCode"/>. Returns true on
    /// success, with the typed value in <paramref name="parsed"/>.
    /// Returns false on failure, with <paramref name="parsed"/> set
    /// to null. Empty and null inputs always fail; whitespace inputs
    /// fail unless the type is <see cref="ColumnTypeCode.String"/>
    /// (where whitespace is a legitimate value).
    /// </summary>
    public static bool TryParse(ColumnTypeCode typeCode, string? raw, out object? parsed)
    {
        parsed = null;

        if (typeCode == ColumnTypeCode.String)
        {
            // String accepts any non-null value, including empty and
            // whitespace. Required-ness is a separate concern handled
            // by RowValidator before reaching here.
            if (raw is null)
            {
                return false;
            }
            parsed = raw;
            return true;
        }

        // Non-string types reject null / empty / whitespace.
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        return typeCode switch
        {
            ColumnTypeCode.Integer   => TryParseInteger(raw, out parsed),
            ColumnTypeCode.BigInt    => TryParseBigInt(raw, out parsed),
            ColumnTypeCode.Decimal   => TryParseDecimal(raw, out parsed),
            ColumnTypeCode.Date      => TryParseDate(raw, out parsed),
            ColumnTypeCode.Timestamp => TryParseTimestamp(raw, out parsed),
            ColumnTypeCode.Boolean   => TryParseBoolean(raw, out parsed),
            ColumnTypeCode.Uuid      => TryParseUuid(raw, out parsed),
            _ => false,
        };
    }

    // Strict numeric styles: no whitespace, no thousands separators,
    // no parentheses-as-negative. Sign + digits (+ decimal point for
    // Decimal). These are the styles that match the no-trim contract
    // and avoid culture-ambiguous separator interpretation.
    private const NumberStyles StrictIntegerStyle = NumberStyles.AllowLeadingSign;
    private const NumberStyles StrictDecimalStyle =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private static bool TryParseInteger(string raw, out object? parsed)
    {
        if (int.TryParse(raw, StrictIntegerStyle, CultureInfo.InvariantCulture, out var v))
        {
            parsed = v;
            return true;
        }
        parsed = null;
        return false;
    }

    private static bool TryParseBigInt(string raw, out object? parsed)
    {
        if (long.TryParse(raw, StrictIntegerStyle, CultureInfo.InvariantCulture, out var v))
        {
            parsed = v;
            return true;
        }
        parsed = null;
        return false;
    }

    private static bool TryParseDecimal(string raw, out object? parsed)
    {
        // Strict: no thousand separators. "1,234.56" fails because
        // ',' is culture-ambiguous (en-US thousands; de-DE decimal).
        // Registry authors needing thousand separators declare a
        // String column with a Pattern and convert downstream.
        if (decimal.TryParse(raw, StrictDecimalStyle, CultureInfo.InvariantCulture, out var v))
        {
            parsed = v;
            return true;
        }
        parsed = null;
        return false;
    }

    private static bool TryParseDate(string raw, out object? parsed)
    {
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v))
        {
            parsed = v;
            return true;
        }
        parsed = null;
        return false;
    }

    private static bool TryParseTimestamp(string raw, out object? parsed)
    {
        // Use DateTimeOffset directly — never DateTime — to avoid
        // silent UTC-vs-local confusion. AssumeUniversal: a value
        // without an explicit offset is treated as UTC. This is
        // the safe default for ETL where source data may or may not
        // carry timezone info.
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var v))
        {
            parsed = v;
            return true;
        }
        parsed = null;
        return false;
    }

    private static bool TryParseBoolean(string raw, out object? parsed)
    {
        // Centralised handling of common ETL boolean encodings.
        // bool.TryParse handles "true"/"false" case-insensitively
        // already; we add 1/0, y/n, yes/no.
        switch (raw.ToUpperInvariant())
        {
            case "TRUE":
            case "1":
            case "Y":
            case "YES":
                parsed = true;
                return true;

            case "FALSE":
            case "0":
            case "N":
            case "NO":
                parsed = false;
                return true;

            default:
                parsed = null;
                return false;
        }
    }

    private static bool TryParseUuid(string raw, out object? parsed)
    {
        if (Guid.TryParse(raw, out var v))
        {
            parsed = v;
            return true;
        }
        parsed = null;
        return false;
    }
}
