namespace Streamline.Application.Commands;

/// <summary>
/// Request to ingest every recognised file under
/// <see cref="SourceDirectory"/> into a fresh batch. The handler
/// resolves each file against the file-mapping registry, reads the
/// rows via the appropriate <c>IFileReader</c>, validates them, and
/// stages them under a new <c>BatchId</c>. Drift policy from each
/// matching <c>RegistryEntry</c> governs how schema differences are
/// handled per file.
/// </summary>
/// <param name="SourceDirectory">
/// Filesystem path the orchestrator scans for input files.
/// Recorded on the batch as the <c>Source</c> string for observability.
/// Required, non-blank.
/// </param>
public sealed record class IngestBatchCommand(string SourceDirectory)
{
    public string SourceDirectory { get; } = RequireNonBlank(SourceDirectory, nameof(SourceDirectory));

    private static string RequireNonBlank(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
