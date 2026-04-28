using System.Collections.Immutable;
using Streamline.Core.ValueTypes;

namespace Streamline.Core.Results;

/// <summary>
/// The outcome of validating a single source <see cref="Record"/>
/// against its declared <see cref="SchemaDefinition"/>. Produced by
/// <c>RowValidator</c> (sub-phase 1e).
/// </summary>
/// <remarks>
/// Validation is all-or-nothing per row:
/// <list type="bullet">
///   <item>
///     On success, <see cref="IsValid"/> is true, <see cref="Errors"/>
///     is empty, and <see cref="ValidatedRecord"/> holds a new
///     <see cref="Record"/> with values coerced to their declared
///     <see cref="Streamline.Core.Enums.ColumnTypeCode"/>.
///   </item>
///   <item>
///     On failure, <see cref="IsValid"/> is false,
///     <see cref="Errors"/> lists one or more
///     <see cref="ValidationError"/> entries, and
///     <see cref="ValidatedRecord"/> is null — the row is destined
///     for quarantine, no typed form exists.
///   </item>
/// </list>
/// </remarks>
public sealed record class ValidationResult
{
    public ImmutableArray<ValidationError> Errors { get; }
    public Record? ValidatedRecord { get; }

    public bool IsValid => Errors.IsEmpty;

    private ValidationResult(ImmutableArray<ValidationError> errors, Record? validatedRecord)
    {
        Errors = errors;
        ValidatedRecord = validatedRecord;
    }

    public static ValidationResult Success(Record validatedRecord)
    {
        ArgumentNullException.ThrowIfNull(validatedRecord);
        return new ValidationResult([], validatedRecord);
    }

    public static ValidationResult Failure(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var arr = errors.ToImmutableArray();
        if (arr.IsEmpty)
        {
            throw new ArgumentException("Failure requires at least one validation error.", nameof(errors));
        }
        return new ValidationResult(arr, validatedRecord: null);
    }

    public bool Equals(ValidationResult? other) =>
        other is not null
        && Errors.SequenceEqual(other.Errors)
        && Equals(ValidatedRecord, other.ValidatedRecord);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(ValidatedRecord);
        foreach (var error in Errors)
        {
            hash.Add(error);
        }
        return hash.ToHashCode();
    }
}
