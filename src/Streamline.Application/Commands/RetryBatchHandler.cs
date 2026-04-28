using Microsoft.Extensions.Logging;
using Streamline.Application.Observability;
using Streamline.Application.Services;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;

namespace Streamline.Application.Commands;

/// <summary>
/// Thin entry point for a <see cref="RetryBatchCommand"/>. Looks up
/// the batch's persisted state via
/// <see cref="IStagingRepository.GetBatchAsync"/>, rehydrates the
/// aggregate via <see cref="Batch.FromState"/>, resets every
/// <see cref="Streamline.Core.Enums.RowStatus.RolledBack"/> row back
/// to <see cref="Streamline.Core.Enums.RowStatus.Pending"/> via
/// <see cref="IStagingRepository.ResetForRetryAsync"/>, emits a
/// <c>BATCH_RETRIED</c> observation, and delegates to
/// <see cref="ProcessingOrchestrator"/> — which will drive the legal
/// <c>Failed → Processing</c> transition and re-run the batch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pre-condition.</b> The batch must be in
/// <see cref="Streamline.Core.Enums.BatchStatus.Failed"/>; the
/// orchestrator's <see cref="Batch.BeginProcessing"/> call enforces
/// this via <c>BatchStateMachine</c>. Retrying a batch in any other
/// status throws <c>IllegalStateTransitionException</c>.
/// </para>
/// <para>
/// <b>Quarantine untouched.</b>
/// <see cref="IStagingRepository.ResetForRetryAsync"/> is contracted
/// to leave <see cref="Streamline.Core.Enums.RowStatus.Quarantined"/>
/// rows alone — quarantine resolution is a separate manual
/// operation. Only <see cref="Streamline.Core.Enums.RowStatus.RolledBack"/>
/// rows transition back to <see cref="Streamline.Core.Enums.RowStatus.Pending"/>
/// (T7).
/// </para>
/// <para>
/// <b>BATCH_RETRIED ordering.</b> The observation is emitted
/// <em>before</em> the orchestrator runs so the chronological
/// observation timeline reads "retry initiated → processing
/// started → ..." rather than "processing started → retry
/// initiated".
/// </para>
/// </remarks>
public sealed class RetryBatchHandler
{
    private readonly IStagingRepository _staging;
    private readonly ProcessingOrchestrator _orchestrator;
    private readonly IObservationSink _sink;
    private readonly ILogger<ResilientObservationSink> _logger;

    public RetryBatchHandler(
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
        RetryBatchCommand command,
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

        await _staging.ResetForRetryAsync(command.BatchId, cancellationToken)
            .ConfigureAwait(false);

        var retryObservation = new Observation(
            ObservationSeverity.Info,
            ObservationCodes.BATCH_RETRIED,
            $"Batch {command.BatchId} retry initiated.",
            command.BatchId.Value,
            DateTimeOffset.UtcNow);
        await scope.Sink.RecordAsync(retryObservation, cancellationToken).ConfigureAwait(false);

        var inner = await _orchestrator
            .ExecuteAsync(batch, scope, cancellationToken)
            .ConfigureAwait(false);

        // Prepend the retry observation so the chronological order
        // reads "retry initiated → batch processing started → ...".
        return new ProcessingResult(
            inner.Tables,
            [retryObservation, .. inner.Observations],
            inner.ObservabilityDegraded);
    }
}
