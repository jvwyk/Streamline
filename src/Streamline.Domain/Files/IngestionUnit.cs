using System.Collections.Immutable;
using Streamline.Core.ValueTypes;

namespace Streamline.Domain.Files;

/// <summary>
/// One unit of work yielded by an <c>IFileDispatcher</c> — typically
/// a single entry inside a container (e.g. one entry from a zip).
/// The dispatcher hands each unit to the engine, which routes it to
/// the matching reader without ever materialising the entry to disk.
/// See plan §15 Appendix C and plan §6 Phase 3b.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stream ownership.</b> The dispatcher owns
/// <see cref="Content"/>'s lifetime; the engine and its readers must
/// not dispose it. The dispatcher disposes the stream after the
/// engine signals it is done with the unit (typically by completing
/// the <c>IFileReader.ReadRowsAsync</c> enumeration).
/// </para>
/// <para>
/// <b>Suggested mapping.</b> In <c>Route</c> dispatch mode the
/// dispatcher leaves <see cref="SuggestedMapping"/> null and the
/// engine resolves a fresh mapping per entry name, exactly as if the
/// entry had arrived as its own file. In <c>Combine</c> and
/// <c>Pick</c> modes the dispatcher pre-resolves the mapping (every
/// entry feeds the same target) and supplies it here.
/// </para>
/// <para>
/// <b>Metadata.</b> Free-form bag for dispatcher-specific context
/// the reader or orchestrator may want to inspect — e.g. the zip
/// dispatcher might attach <c>{"zip_entry_compressed_size": 1234}</c>.
/// </para>
/// </remarks>
public sealed record class IngestionUnit
{
    public Stream Content { get; }
    public string EntryName { get; }
    public FileMapping? SuggestedMapping { get; }
    public ImmutableDictionary<string, object?> Metadata { get; }

    public IngestionUnit(
        Stream content,
        string entryName,
        FileMapping? suggestedMapping = null,
        IEnumerable<KeyValuePair<string, object?>>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName, nameof(entryName));

        Content = content;
        EntryName = entryName;
        SuggestedMapping = suggestedMapping;

        var builder = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach (var kvp in metadata)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key))
                {
                    throw new ArgumentException(
                        "Metadata keys must not be null or blank.", nameof(metadata));
                }
                if (builder.ContainsKey(kvp.Key))
                {
                    throw new ArgumentException(
                        $"Duplicate metadata key: {kvp.Key}.", nameof(metadata));
                }
                builder.Add(kvp.Key, kvp.Value);
            }
        }
        Metadata = builder.ToImmutable();
    }
}
