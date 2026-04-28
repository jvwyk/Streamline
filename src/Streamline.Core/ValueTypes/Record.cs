using System.Collections.Immutable;

namespace Streamline.Core.ValueTypes;

/// <summary>
/// One source row as it flows through the pipeline. Carries its own
/// provenance (<see cref="SourceFileName"/>, <see cref="SourceRowIndex"/>)
/// so lineage can trace destination rows back to source without a
/// parallel envelope structure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Raw vs typed values.</b> The same <c>Record</c> type carries both
/// representations. File readers emit records whose values are
/// <c>string?</c> (or null for missing columns). <c>RowValidator</c>
/// returns a new record with values coerced to their <see cref="ColumnDefinition.TypeCode"/>:
/// <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>,
/// <see cref="DateOnly"/>, <see cref="DateTimeOffset"/>, <see cref="bool"/>,
/// <see cref="Guid"/>, or <see cref="string"/>. Destinations consume
/// typed records; they will not see strings for typed columns.
/// </para>
/// <para>
/// <see cref="GetRaw"/> and <see cref="GetTyped{T}"/> encode the
/// distinction at the call site: use <see cref="GetRaw"/> when you
/// expect a pre-validation record, use <see cref="GetTyped{T}"/> when
/// you expect a post-validation record. Both return <c>null</c>/default
/// for missing columns; <see cref="Contains"/> distinguishes
/// missing-from-null.
/// </para>
/// </remarks>
public sealed record class Record
{
    public string SourceFileName { get; }
    public long SourceRowIndex { get; }
    public ImmutableDictionary<string, object?> Values { get; }

    public Record(
        string sourceFileName,
        long sourceRowIndex,
        IEnumerable<KeyValuePair<string, object?>> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName, nameof(sourceFileName));
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRowIndex, nameof(sourceRowIndex));
        ArgumentNullException.ThrowIfNull(values);

        SourceFileName = sourceFileName;
        SourceRowIndex = sourceRowIndex;

        var builder = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(kvp.Key, nameof(values));
            if (builder.ContainsKey(kvp.Key))
            {
                throw new ArgumentException(
                    $"Duplicate column in record: {kvp.Key}.", nameof(values));
            }
            builder.Add(kvp.Key, kvp.Value);
        }
        Values = builder.ToImmutable();
    }

    /// <summary>
    /// Returns the raw (reader-emitted) string value for a column.
    /// Returns null if the column is absent OR the stored value is null.
    /// Throws if the column is present but carries a typed, non-string
    /// value (indicates the record was already validated; call
    /// <see cref="GetTyped{T}"/> instead).
    /// </summary>
    public string? GetRaw(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        if (!Values.TryGetValue(column, out var value))
        {
            return null;
        }

        return value switch
        {
            null => null,
            string s => s,
            _ => throw new InvalidOperationException(
                $"Column '{column}' holds a typed {value.GetType().Name}; use GetTyped<T>() instead."),
        };
    }

    /// <summary>
    /// Returns the typed value for a column, cast to <typeparamref name="T"/>.
    /// Returns <c>default(T)</c> if the column is absent OR the stored
    /// value is null. Throws if the column is present but carries a value
    /// of an unrelated type.
    /// </summary>
    public T? GetTyped<T>(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column, nameof(column));
        if (!Values.TryGetValue(column, out var value))
        {
            return default;
        }

        if (value is null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        throw new InvalidCastException(
            $"Column '{column}' holds {value.GetType().Name}; expected {typeof(T).Name}.");
    }

    /// <summary>
    /// True if the column is present in the record (even if its value is null).
    /// Distinguishes "column absent" from "column present with null value".
    /// </summary>
    public bool Contains(string column) => Values.ContainsKey(column);

    public bool Equals(Record? other) =>
        other is not null
        && SourceFileName == other.SourceFileName
        && SourceRowIndex == other.SourceRowIndex
        && ValuesEqual(Values, other.Values);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(SourceFileName);
        hash.Add(SourceRowIndex);
        foreach (var kvp in Values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        return hash.ToHashCode();
    }

    private static bool ValuesEqual(
        ImmutableDictionary<string, object?> a,
        ImmutableDictionary<string, object?> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, valueA) in a)
        {
            if (!b.TryGetValue(key, out var valueB))
            {
                return false;
            }

            if (!Equals(valueA, valueB))
            {
                return false;
            }
        }

        return true;
    }
}
