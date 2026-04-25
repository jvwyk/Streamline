using Microsoft.Extensions.Logging;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;

namespace Streamline.Application.Commands;

/// <summary>
/// Thin entry point for an <see cref="IngestBatchCommand"/>. Opens a
/// batch via <see cref="IStagingRepository.StartBatchAsync"/>,
/// constructs a fresh <see cref="Batch"/> aggregate and a
/// per-batch <see cref="PerBatchScope"/>, enumerates files under
/// <see cref="IngestBatchCommand.SourceDirectory"/>, and delegates
/// to <see cref="IngestionOrchestrator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a Func for file enumeration?</b> Most testable surfaces
/// in the engine end up behind a Domain abstraction; file
/// enumeration doesn't (yet) — it's a single-line standard library
/// call, used in exactly one place. Injecting a
/// <see cref="Func{T, TResult}"/> with a default that wraps
/// <see cref="Directory.EnumerateFiles(string)"/> keeps the handler
/// trivially testable without standing up a new
/// <c>IFileEnumerator</c> abstraction in Domain. Phase 2 may
/// promote this if a real consumer wants more sophisticated
/// directory scanning (recursive, filtered, etc.); for v1, the Func
/// is enough.
/// </para>
/// <para>
/// <b>Exception flow.</b> Anything thrown by the orchestrator
/// propagates. The handler does NOT transition the aggregate to
/// <see cref="Streamline.Core.Enums.BatchStatus.Failed"/> on
/// ingestion error — the batch state machine has no
/// <c>Ingesting → Failed</c> transition (see
/// <c>BatchStateMachine</c>). A batch stuck in <c>Ingesting</c>
/// after a thrown error is the operator's problem to inspect via
/// <c>InspectBatchCommand</c>; v1 doesn't auto-fail it. If a future
/// sub-phase decides this should be different, the state machine
/// gains an <c>Ingesting → Failed</c> entry first, then this
/// handler wraps the orchestrator call in try/catch.
/// </para>
/// </remarks>
public sealed class IngestBatchHandler
{
    private readonly IStagingRepository _staging;
    private readonly IngestionOrchestrator _orchestrator;
    private readonly IObservationSink _sink;
    private readonly ILogger<ResilientObservationSink> _logger;
    private readonly Func<string, IReadOnlyList<string>> _enumerateFiles;

    public IngestBatchHandler(
        IStagingRepository staging,
        IngestionOrchestrator orchestrator,
        IObservationSink sink,
        ILogger<ResilientObservationSink> logger,
        Func<string, IReadOnlyList<string>>? enumerateFiles = null)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(logger);

        _staging = staging;
        _orchestrator = orchestrator;
        _sink = sink;
        _logger = logger;
        _enumerateFiles = enumerateFiles ?? DefaultEnumerate;
    }

    public async Task<IngestionResult> HandleAsync(
        IngestBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var batchId = await _staging.StartBatchAsync(command.SourceDirectory, cancellationToken)
            .ConfigureAwait(false);
        var batch = Batch.Create(batchId, command.SourceDirectory);
        var scope = PerBatchScope.CreateForBatch(_sink, _logger);
        var filePaths = _enumerateFiles(command.SourceDirectory);

        return await _orchestrator
            .ExecuteAsync(batch, filePaths, scope, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<string> DefaultEnumerate(string directory) =>
        [.. Directory.EnumerateFiles(directory)];
}
