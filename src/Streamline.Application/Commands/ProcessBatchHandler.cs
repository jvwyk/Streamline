using Microsoft.Extensions.Logging;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;

namespace Streamline.Application.Commands;

/// <summary>
/// Thin entry point for a <see cref="ProcessBatchCommand"/>. Looks
/// up the batch's persisted state via
/// <see cref="IStagingRepository.GetBatchAsync"/>, rehydrates a
/// <see cref="Batch"/> aggregate via <see cref="Batch.FromState"/>,
/// builds a per-batch <see cref="PerBatchScope"/>, and delegates to
/// <see cref="ProcessingOrchestrator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pre-condition.</b> The batch must already be in
/// <see cref="BatchStatus.Ingested"/> (or <see cref="BatchStatus.Failed"/>
/// when this is invoked from <c>RetryBatchHandler</c>). The
/// orchestrator's <see cref="Batch.BeginProcessing"/> call enforces
/// that — an aggregate in <c>Created</c>, <c>Ingesting</c>,
/// <c>Processing</c>, or <c>Completed</c> throws
/// <c>IllegalStateTransitionException</c>. The handler does not
/// pre-check; the state machine is the single source of truth.
/// </para>
/// <para>
/// <b>Missing batch.</b> A <see cref="ProcessBatchCommand"/> for an
/// unknown <see cref="BatchId"/> surfaces as
/// <see cref="KeyNotFoundException"/> per the orchestrator-side
/// agreement: the repository returns null for a missing batch, the
/// handler turns null into a typed exception so callers don't have
/// to test for null themselves.
/// </para>
/// </remarks>
public sealed class ProcessBatchHandler
{
    private readonly IStagingRepository _staging;
    private readonly ProcessingOrchestrator _orchestrator;
    private readonly IObservationSink _sink;
    private readonly ILogger<ResilientObservationSink> _logger;

    public ProcessBatchHandler(
        IStagingRepository staging,
        ProcessingOrchestrator orchestrator,
        IObservationSink sink,
        ILogger<ResilientObservationSink> logger)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(logger);

        _staging = staging;
        _orchestrator = orchestrator;
        _sink = sink;
        _logger = logger;
    }

    public async Task<ProcessingResult> HandleAsync(
        ProcessBatchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var snapshot = await _staging.GetBatchAsync(command.BatchId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            throw new KeyNotFoundException(
                $"No batch found with id '{command.BatchId}'.");
        }

        var batch = Batch.FromState(snapshot);
        var scope = PerBatchScope.CreateForBatch(_sink, _logger);

        return await _orchestrator
            .ExecuteAsync(batch, scope, cancellationToken)
            .ConfigureAwait(false);
    }
}
