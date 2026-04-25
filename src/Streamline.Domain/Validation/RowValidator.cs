using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Domain.Validation;

/// <summary>
/// Pure-function gate over <see cref="Record"/> structural validity.
/// Iterates the columns of a <see cref="SchemaDefinition"/>, applies
/// the five built-in rules per column, accumulates every error
/// before returning a <see cref="ValidationResult"/>. On success the
/// result carries a NEW <see cref="Record"/> with typed values per
/// <see cref="ColumnTypeCode"/>; on failure the result carries every
/// <see cref="ValidationError"/> found.
/// </summary>
/// <remarks>
/// <para>
/// <b>The five built-in rules.</b>
/// <list type="bullet">
///   <item><c>MISSING_REQUIRED</c> — required column is null/absent.
///     Empty string is a present value, NOT missing (per the strict
///     interpretation; reader-side empty-becomes-null is a Phase 3
///     reader-config concern).</item>
///   <item><c>INVALID_TYPE</c> — value cannot be coerced to its
///     declared <see cref="ColumnTypeCode"/> via
///     <see cref="ColumnTypeParser"/>.</item>
///   <item><c>INVALID_FORMAT</c> — value doesn't match the column's
///     <see cref="ColumnDefinition.Pattern"/> regex. Applied to the
///     raw string, even on typed columns (a Pattern on Integer
///     constrains the input shape pre-parse).</item>
///   <item><c>INVALID_LENGTH</c> — string length exceeds
///     <see cref="ColumnDefinition.MaxLength"/>. Applies to
///     <see cref="ColumnTypeCode.String"/> only.</item>
///   <item><c>INVALID_RANGE</c> — typed value outside
///     <see cref="ColumnDefinition.MinValue"/> /
///     <see cref="ColumnDefinition.MaxValue"/> bounds. Applies to
///     numeric and temporal types only.</item>
/// </list>
/// </para>
/// <para>
/// <b>FK validation is NOT here.</b> <c>FkResolver</c> (commit 4)
/// handles FK references on the raw <see cref="Record"/>; the
/// orchestrator (sub-phase 1g) sequences the two. Loose coupling per
/// 1e Q15.
/// </para>
/// <para>
/// <b>No observations.</b> Services never emit observations
/// (1c P-2). The orchestrator turns each
/// <see cref="ValidationError"/> into an observation with the
/// matching <see cref="ObservationCodes"/> code.
/// </para>
/// <para>
/// <b>Defensive bounds parsing.</b> If a column's
/// <see cref="ColumnDefinition.MinValue"/> or
/// <see cref="ColumnDefinition.MaxValue"/> cannot itself be parsed
/// (registry-author error), the bound is silently ignored rather
/// than throwing — the row data isn't at fault. Detection of
/// bound-parseability belongs to <c>RegistryValidator</c> at
/// config time (deferred per the 1e plan; revisit when a real
/// case arises).
/// </para>
/// </remarks>
public static class RowValidator
{
    public static ValidationResult Validate(Record record, SchemaDefinition schema)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(schema);

        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        var typedValues = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);

        foreach (var column in schema.Columns)
        {
            var raw = record.GetRaw(column.Name);

            // MISSING_REQUIRED — null or absent on a required column.
            if (raw is null)
            {
                if (column.IsRequired)
                {
                    errors.Add(new ValidationError(
                        column.Name,
                        AttemptedValue: null,
                        Code: ObservationCodes.MISSING_REQUIRED,
                        Message: $"Required column '{column.Name}' has no value."));
                }
                // Optional column with no value: nothing more to check;
                // typed value is null.
                typedValues[column.Name] = null;
                continue;
            }

            // INVALID_FORMAT — pattern mismatch on the raw string.
            if (!string.IsNullOrEmpty(column.Pattern)
                && !Regex.IsMatch(raw, column.Pattern, RegexOptions.CultureInvariant))
            {
                errors.Add(new ValidationError(
                    column.Name,
                    AttemptedValue: raw,
                    Code: ObservationCodes.INVALID_FORMAT,
                    Message: $"Value '{raw}' does not match pattern '{column.Pattern}'."));
                // Continue to other checks on this column — accumulate
                // every violation rather than short-circuiting.
            }

            // INVALID_LENGTH — String only.
            if (column.TypeCode == ColumnTypeCode.String
                && column.MaxLength is int max
                && raw.Length > max)
            {
                errors.Add(new ValidationError(
                    column.Name,
                    AttemptedValue: raw,
                    Code: ObservationCodes.INVALID_LENGTH,
                    Message: $"Value length {raw.Length} exceeds MaxLength {max}."));
            }

            // INVALID_TYPE — coerce via ColumnTypeParser.
            if (!ColumnTypeParser.TryParse(column.TypeCode, raw, out var parsedValue))
            {
                errors.Add(new ValidationError(
                    column.Name,
                    AttemptedValue: raw,
                    Code: ObservationCodes.INVALID_TYPE,
                    Message: $"Value '{raw}' is not a valid {column.TypeCode}."));
                // No typed value to add. Skip range check (no value
                // to compare).
                typedValues[column.Name] = null;
                continue;
            }

            typedValues[column.Name] = parsedValue;

            // INVALID_RANGE — bounded numeric/temporal types only.
            if (IsRangeable(column.TypeCode) && parsedValue is not null)
            {
                if (TryGetTypedBound(column, column.MinValue, out var minTyped)
                    && CompareTyped(parsedValue, minTyped) < 0)
                {
                    errors.Add(new ValidationError(
                        column.Name,
                        AttemptedValue: raw,
                        Code: ObservationCodes.INVALID_RANGE,
                        Message: $"Value '{raw}' is below MinValue '{column.MinValue}'."));
                }

                if (TryGetTypedBound(column, column.MaxValue, out var maxTyped)
                    && CompareTyped(parsedValue, maxTyped) > 0)
                {
                    errors.Add(new ValidationError(
                        column.Name,
                        AttemptedValue: raw,
                        Code: ObservationCodes.INVALID_RANGE,
                        Message: $"Value '{raw}' is above MaxValue '{column.MaxValue}'."));
                }
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToImmutable());
        }

        // Success: build the typed Record. Provenance carries over
        // from the raw record.
        var validated = new Record(
            record.SourceFileName,
            record.SourceRowIndex,
            typedValues.ToImmutable());

        return ValidationResult.Success(validated);
    }

    private static bool IsRangeable(ColumnTypeCode code) => code switch
    {
        ColumnTypeCode.Integer or
        ColumnTypeCode.BigInt or
        ColumnTypeCode.Decimal or
        ColumnTypeCode.Date or
        ColumnTypeCode.Timestamp => true,
        _ => false,
    };

    private static bool TryGetTypedBound(ColumnDefinition column, string? rawBound, out object? typed)
    {
        typed = null;
        if (string.IsNullOrEmpty(rawBound))
        {
            return false;
        }
        // Defensive: bound parsing failure is a registry-author bug,
        // not a row-data bug. Skip the bound rather than fail the row.
        return ColumnTypeParser.TryParse(column.TypeCode, rawBound, out typed);
    }

    private static int CompareTyped(object? value, object? bound) =>
        (value, bound) switch
        {
            (int v, int b) => v.CompareTo(b),
            (long v, long b) => v.CompareTo(b),
            (decimal v, decimal b) => v.CompareTo(b),
            (DateOnly v, DateOnly b) => v.CompareTo(b),
            (DateTimeOffset v, DateTimeOffset b) => v.CompareTo(b),
            // Cross-type comparison shouldn't happen — bound and
            // value share a ColumnTypeCode by construction. Treat as
            // "in range" rather than throwing on the registry author.
            _ => 0,
        };
}
