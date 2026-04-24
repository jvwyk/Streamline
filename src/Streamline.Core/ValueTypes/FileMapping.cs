using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Streamline.Core.ValueTypes;

/// <summary>
/// Associates an incoming file with a registered target table and
/// reader format. One registry entry has one or more mappings — a
/// single logical table can accept files matching several filename
/// patterns or arriving in several formats.
/// </summary>
/// <remarks>
/// <see cref="ReaderConfig"/> is an opaque string-to-string property
/// bag. Per-format typed config types (XlsxConfig, DelimitedConfig,
/// ZipConfig, etc.) live in the respective Files.* projects and parse
/// their own keys. The Core layer intentionally knows nothing about
/// the config contents; see plan §5.3 "Reader configuration is rich
/// and format-specific". Nested structures encode as flat dot-
/// delimited keys (e.g. <c>sheet.selector.kind = ByName</c>) for
/// Phase 1; revisit in Phase 3 if real reader configs show that
/// flat-with-dots is painful.
/// </remarks>
public sealed record class FileMapping
{
    public string FilenamePattern { get; }
    public string TargetTable { get; }
    public string Format { get; }
    public ImmutableDictionary<string, string> ReaderConfig { get; }

    private readonly Regex _compiledPattern;

    public FileMapping(
        string filenamePattern,
        string targetTable,
        string format,
        IEnumerable<KeyValuePair<string, string>>? readerConfig = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filenamePattern, nameof(filenamePattern));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable, nameof(targetTable));
        ArgumentException.ThrowIfNullOrWhiteSpace(format, nameof(format));

        try
        {
            _compiledPattern = new Regex(
                filenamePattern,
                RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"FilenamePattern '{filenamePattern}' is not a valid regex: {ex.Message}",
                nameof(filenamePattern),
                ex);
        }

        FilenamePattern = filenamePattern;
        TargetTable = targetTable;
        Format = format;

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (readerConfig is not null)
        {
            foreach (var kvp in readerConfig)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(kvp.Key, nameof(readerConfig));
                ArgumentNullException.ThrowIfNull(kvp.Value, nameof(readerConfig));
                if (builder.ContainsKey(kvp.Key))
                {
                    throw new ArgumentException(
                        $"Duplicate reader-config key: {kvp.Key}.", nameof(readerConfig));
                }
                builder.Add(kvp.Key, kvp.Value);
            }
        }
        ReaderConfig = builder.ToImmutable();
    }

    /// <summary>
    /// True if the given filename matches this mapping's pattern.
    /// </summary>
    public bool Matches(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);
        return _compiledPattern.IsMatch(filename);
    }

    public bool Equals(FileMapping? other) =>
        other is not null
        && FilenamePattern == other.FilenamePattern
        && TargetTable == other.TargetTable
        && Format == other.Format
        && ReaderConfigEqual(ReaderConfig, other.ReaderConfig);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(FilenamePattern);
        hash.Add(TargetTable);
        hash.Add(Format);
        foreach (var kvp in ReaderConfig.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }
        return hash.ToHashCode();
    }

    private static bool ReaderConfigEqual(
        ImmutableDictionary<string, string> a,
        ImmutableDictionary<string, string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        foreach (var (key, valueA) in a)
        {
            if (!b.TryGetValue(key, out var valueB) || valueA != valueB)
            {
                return false;
            }
        }
        return true;
    }
}
