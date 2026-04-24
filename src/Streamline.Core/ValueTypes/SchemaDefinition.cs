using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Streamline.Core.ValueTypes;

/// <summary>
/// An ordered collection of <see cref="ColumnDefinition"/> that describes
/// the source schema of a registry entry. Columns preserve declaration
/// order (load order matters for CSV readers with positional columns);
/// name lookup is O(1) via a frozen dictionary.
/// </summary>
public sealed record class SchemaDefinition
{
    public ImmutableArray<ColumnDefinition> Columns { get; }

    private readonly FrozenDictionary<string, ColumnDefinition> _byName;

    public SchemaDefinition(IEnumerable<ColumnDefinition> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        Columns = [.. columns];

        var duplicates = Columns
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new ArgumentException(
                $"Duplicate column names: {string.Join(", ", duplicates)}.",
                nameof(columns));
        }

        _byName = Columns.ToFrozenDictionary(c => c.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the column with the given name, or null if no such column
    /// is declared. Matching is case-sensitive.
    /// </summary>
    public ColumnDefinition? GetColumn(string name) =>
        _byName.TryGetValue(name, out var column) ? column : null;

    public bool Contains(string name) => _byName.ContainsKey(name);

    public IReadOnlyList<ColumnDefinition> PrimaryKey =>
        [.. Columns.Where(c => c.IsPrimaryKey)];

    public bool Equals(SchemaDefinition? other) =>
        other is not null && Columns.SequenceEqual(other.Columns);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var column in Columns)
        {
            hash.Add(column);
        }
        return hash.ToHashCode();
    }
}
