using System.Collections.Immutable;
using Streamline.Core.Observations;
using Streamline.Domain.Batches.DomainEvents;

namespace Streamline.Application.Services;

/// <summary>
/// Translates <see cref="IDomainEvent"/>s drained from the
/// <c>Batch</c> aggregate into <see cref="Observation"/>s and
/// forwards them through an <see cref="IObservationSink"/>. One
/// place for the event-to-observation mapping table; one set of
/// tests; every handler reuses it.
/// </summary>
/// <remarks>
/// Per the 1f Q8 resolution: extracting this class avoids four
/// near-duplicate translation tables across the four handlers.
/// Each event type maps to a specific observation code, severity,
/// and structured context dictionary; the mapping rules live here
/// and nowhere else.
/// </remarks>
public sealed class DomainEventPublisher
{
    private readonly IObservationSink _sink;

    public DomainEventPublisher(IObservationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>
    /// Translate a single domain event to an observation and emit
    /// via the underlying sink. Failures propagate per the sink's
    /// contract; production sinks decorated with
    /// <c>ResilientObservationSink</c> handle retry and graceful
    /// degradation.
    /// </summary>
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var observation = Translate(domainEvent);
        return _sink.RecordAsync(observation, cancellationToken);
    }

    /// <summary>
    /// Map an event to its corresponding observation. Public for
    /// testing — handlers call <see cref="PublishAsync"/>; the
    /// translation function is exposed so unit tests can verify
    /// the mapping table without going through a sink.
    /// </summary>
    public static Observation Translate(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            BatchStartedEvent e => TranslateBatchStarted(e),
            RowQuarantinedEvent e => TranslateRowQuarantined(e),
            TransformInvokedEvent e => TranslateTransformInvoked(e),
            ReconciliationCompletedEvent e => TranslateReconciliationCompleted(e),
            BatchCompletedEvent e => TranslateBatchCompleted(e),
            BatchFailedEvent e => TranslateBatchFailed(e),
            _ => throw new InvalidOperationException(
                $"DomainEventPublisher does not have a translation for event type {domainEvent.GetType().Name}. " +
                "Add a mapping in DomainEventPublisher.Translate or extract a registry pattern."),
        };
    }

    private static Observation TranslateBatchStarted(BatchStartedEvent e) =>
        new(
            ObservationSeverity.Info,
            ObservationCodes.BATCH_STARTED,
            $"Batch started from source '{e.Source}'.",
            e.BatchId.Value,
            e.OccurredAt,
            new Dictionary<string, object?>
            {
                ["source"] = e.Source,
            });

    private static Observation TranslateRowQuarantined(RowQuarantinedEvent e) =>
        new(
            ObservationSeverity.Error,
            e.Code,  // Use the validation/observation code carried on the event
            e.Message,
            e.BatchId.Value,
            e.OccurredAt,
            new Dictionary<string, object?>
            {
                ["incoming_id"] = e.IncomingId,
                ["target_table"] = e.TargetTable,
            })
        {
            IncomingId = e.IncomingId,
            TableName = e.TargetTable,
        };

    private static Observation TranslateTransformInvoked(TransformInvokedEvent e)
    {
        var partialFailure = e.Outcome.RowsFailed > 0;
        var code = partialFailure
            ? ObservationCodes.TRANSFORMER_PARTIAL_FAILURE
            : ObservationCodes.TRANSFORMER_COMPLETED;
        var severity = partialFailure
            ? ObservationSeverity.Error
            : ObservationSeverity.Info;
        var message = partialFailure
            ? $"Transformer {e.Reference.Reference} reported {e.Outcome.RowsFailed} failed row(s)."
            : $"Transformer {e.Reference.Reference} completed.";

        var ctx = new Dictionary<string, object?>
        {
            ["transform_reference"] = e.Reference.Reference,
            ["transform_kind"] = e.Reference.Kind.ToString(),
            ["destination_table"] = e.Reference.DestinationTable,
            ["rows_inserted"] = e.Outcome.RowsInserted,
            ["rows_updated"] = e.Outcome.RowsUpdated,
            ["rows_skipped"] = e.Outcome.RowsSkipped,
            ["rows_failed"] = e.Outcome.RowsFailed,
            ["duration_ms"] = e.Outcome.Duration.TotalMilliseconds,
        };
        if (e.Outcome.ErrorMessage is not null)
        {
            ctx["error_message"] = e.Outcome.ErrorMessage;
        }

        return new Observation(severity, code, message, e.BatchId.Value, e.OccurredAt, ctx)
        {
            TableName = e.Reference.DestinationTable,
        };
    }

    private static Observation TranslateReconciliationCompleted(ReconciliationCompletedEvent e)
    {
        var code = e.Passed
            ? ObservationCodes.RECONCILIATION_PASSED
            : ObservationCodes.RECONCILIATION_MISMATCH;
        var severity = e.Passed
            ? ObservationSeverity.Info
            : ObservationSeverity.Warning;

        var ctx = new Dictionary<string, object?>
        {
            ["passed"] = e.Passed,
        };
        if (e.Summary is not null)
        {
            ctx["summary"] = e.Summary;
        }

        return new Observation(
            severity,
            code,
            e.Passed ? "Reconciliation passed." : (e.Summary ?? "Reconciliation mismatch detected."),
            e.BatchId.Value,
            e.OccurredAt,
            ctx);
    }

    private static Observation TranslateBatchCompleted(BatchCompletedEvent e) =>
        new(
            ObservationSeverity.Info,
            ObservationCodes.BATCH_COMPLETED,
            "Batch completed.",
            e.BatchId.Value,
            e.OccurredAt);

    private static Observation TranslateBatchFailed(BatchFailedEvent e) =>
        new(
            ObservationSeverity.Critical,
            ObservationCodes.BATCH_FAILED,
            $"Batch failed: {e.Reason}.",
            e.BatchId.Value,
            e.OccurredAt,
            new Dictionary<string, object?>
            {
                ["reason"] = e.Reason,
            });
}
