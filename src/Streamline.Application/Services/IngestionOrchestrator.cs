using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Streamline.Application.Observability;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Detection;
using Streamline.Domain.Validation;
using Record = Streamline.Core.ValueTypes.Record;

namespace Streamline.Application.Services;

/// <summary>
/// Drives one batch from <see cref="Batch.Create"/> +
/// <see cref="Batch.Start"/> through to
/// <see cref="Batch.MarkIngested"/>: resolves each input file's
/// mapping and registry entry, applies drift policy, opens a
/// file-log entry, reads + validates rows, stages valid rows, and
/// counts per-row validation failures as quarantine candidates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregate engagement.</b> The orchestrator drives the
/// <see cref="Batch"/> aggregate's lifecycle methods
/// (<see cref="Batch.Start"/>, <see cref="Batch.MarkIngested"/>) and
/// drains accumulated <c>IDomainEvent</c>s after each transition.
/// Per-row state transitions are NOT engaged during ingestion —
/// fresh rows go directly into <c>staging.incoming</c> in
/// <c>Pending</c> status; ingestion-time validation failures emit
/// observations directly without going through the aggregate's row
/// methods (those are processing-time gates).
/// </para>
/// <para>
/// <b>Validated vs quarantined rows.</b> Rows whose
/// <c>RowValidator</c> result is <see cref="ValidationResult.IsValid"/>
/// are staged in bulk via
/// <see cref="IStagingRepository.BulkInsertIncomingAsync"/>. Rows
/// that fail validation are NOT inserted; the orchestrator emits one
/// observation per <see cref="ValidationError"/> and increments the
/// per-file <c>RowsQuarantined</c> counter. Persistence of
/// quarantined rows to <c>staging.quarantine</c> at ingestion time
/// is deferred — the in-memory fake (sub-phase 1g) and the Postgres
/// adapter (Phase 2) own that storage detail; for now the count and
/// observation trail are the engine's record of what was rejected.
/// </para>
/// <para>
/// <b>Skipped files.</b> Files with no matching mapping are skipped
/// silently for v1 — this matches "the consumer pre-filters their
/// directory" assumption from the plan. Files with a mapping but no
/// active registry entry, or no resolvable reader, emit
/// <c>FILE_READ_FAILED</c> at <c>Critical</c> and contribute zero
/// rows to the result.
/// </para>
/// <para>
/// <b>Cancellation.</b> Honours the provided
/// <see cref="CancellationToken"/> at every async call site;
/// cancellation propagates as <see cref="OperationCanceledException"/>.
/// The orchestrator does not catch — the handler decides whether to
/// transition the batch to <see cref="Streamline.Core.Enums.BatchStatus.Failed"/>.
/// </para>
/// </remarks>
public sealed class IngestionOrchestrator
{
    private readonly IFileMappingRepository _mappings;
    private readonly IRegistryRepository _registry;
    private readonly IFileReaderRegistry _readers;
    private readonly IStagingRepository _staging;

    public IngestionOrchestrator(
        IFileMappingRepository mappings,
        IRegistryRepository registry,
        IFileReaderRegistry readers,
        IStagingRepository staging)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(staging);

        _mappings = mappings;
        _registry = registry;
        _readers = readers;
        _staging = staging;
    }

    public async Task<IngestionResult> ExecuteAsync(
        Batch batch,
        IReadOnlyList<string> filePaths,
        PerBatchScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(scope);

        var collected = new List<Observation>();
        var fileOutcomes = new List<FileIngestionOutcome>();

        batch.Start();
        await PublishDrainedEventsAsync(batch, scope, collected, cancellationToken).ConfigureAwait(false);

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await IngestFileAsync(batch, filePath, scope, collected, cancellationToken).ConfigureAwait(false);
            if (outcome is not null)
            {
                fileOutcomes.Add(outcome);
            }
        }

        batch.MarkIngested();
        await PublishDrainedEventsAsync(batch, scope, collected, cancellationToken).ConfigureAwait(false);

        return new IngestionResult(
            fileOutcomes,
            collected,
            scope.DegradationState.IsDegraded);
    }

    private async Task<FileIngestionOutcome?> IngestFileAsync(
        Batch batch,
        string filePath,
        PerBatchScope scope,
        List<Observation> collected,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        var stopwatch = Stopwatch.StartNew();

        var mapping = await _mappings.ResolveAsync(fileName, cancellationToken).ConfigureAwait(false);
        if (mapping is null)
        {
            // No mapping — silently skip; consumer is expected to
            // pre-filter the directory. No outcome is recorded.
            return null;
        }

        var entry = await _registry.GetActiveAsync(mapping.TargetTable, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            await EmitAsync(scope, collected, new Observation(
                ObservationSeverity.Critical,
                ObservationCodes.FILE_READ_FAILED,
                $"No active registry entry for table '{mapping.TargetTable}' (file '{fileName}').",
                batch.Id.Value,
                DateTimeOffset.UtcNow,
                new Dictionary<string, object?>
                {
                    ["file_name"] = fileName,
                    ["target_table"] = mapping.TargetTable,
                })
            {
                TableName = mapping.TargetTable,
            }, cancellationToken).ConfigureAwait(false);
            return new FileIngestionOutcome(fileName, 0, 0, 0, stopwatch.Elapsed);
        }

        var reader = _readers.ResolveReader(mapping.Format);
        if (reader is null)
        {
            await EmitAsync(scope, collected, new Observation(
                ObservationSeverity.Critical,
                ObservationCodes.FILE_READ_FAILED,
                $"No reader registered for format '{mapping.Format}' (file '{fileName}').",
                batch.Id.Value,
                DateTimeOffset.UtcNow,
                new Dictionary<string, object?>
                {
                    ["file_name"] = fileName,
                    ["format"] = mapping.Format,
                })
            {
                TableName = mapping.TargetTable,
            }, cancellationToken).ConfigureAwait(false);
            return new FileIngestionOutcome(fileName, 0, 0, 0, stopwatch.Elapsed);
        }

        var headers = await reader.GetHeadersAsync(mapping, filePath, cancellationToken).ConfigureAwait(false);
        var driftReport = SchemaDriftDetector.Detect(headers, entry.Schema);
        var driftDecision = SchemaDriftPolicyApplier.Apply(driftReport, entry);

        foreach (var emission in driftDecision.Emissions)
        {
            await EmitAsync(scope, collected, BuildDriftObservation(emission, batch, fileName, mapping.TargetTable), cancellationToken).ConfigureAwait(false);
        }

        if (driftDecision.FileBlocked)
        {
            return new FileIngestionOutcome(fileName, 0, 0, 0, stopwatch.Elapsed);
        }

        var fileLogId = await _staging.OpenFileLogAsync(batch.Id, fileName, cancellationToken).ConfigureAwait(false);

        var rowsRead = 0L;
        var rowsQuarantined = 0L;
        var validatedRecords = new List<Record>();

        await foreach (var record in reader.ReadRowsAsync(mapping, filePath, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowsRead++;

            var validation = RowValidator.Validate(record, entry.Schema);
            if (validation.IsValid)
            {
                validatedRecords.Add(validation.ValidatedRecord!);
            }
            else
            {
                rowsQuarantined++;
                foreach (var error in validation.Errors)
                {
                    await EmitAsync(scope, collected, BuildValidationObservation(error, batch, mapping.TargetTable, fileLogId, record), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var rowsStaged = await _staging
            .BulkInsertIncomingAsync(batch.Id, fileLogId, mapping.TargetTable, ToAsyncEnumerable(validatedRecords, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await EmitAsync(scope, collected, new Observation(
            ObservationSeverity.Info,
            ObservationCodes.FILE_INGESTED,
            $"Ingested file '{fileName}' into '{mapping.TargetTable}': {rowsStaged} staged, {rowsQuarantined} quarantined.",
            batch.Id.Value,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>
            {
                ["file_name"] = fileName,
                ["target_table"] = mapping.TargetTable,
                ["rows_read"] = rowsRead,
                ["rows_staged"] = rowsStaged,
                ["rows_quarantined"] = rowsQuarantined,
            })
        {
            FileLogId = fileLogId,
            TableName = mapping.TargetTable,
        }, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        return new FileIngestionOutcome(fileName, rowsRead, rowsStaged, rowsQuarantined, stopwatch.Elapsed);
    }

    private static Observation BuildDriftObservation(
        DriftObservationEmission emission,
        Batch batch,
        string fileName,
        string targetTable) =>
        new(
            emission.Severity,
            emission.Code,
            emission.Message,
            batch.Id.Value,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>
            {
                ["file_name"] = fileName,
                ["target_table"] = targetTable,
                [emission.ContextDimension] = emission.AffectedColumns.IsDefaultOrEmpty
                    ? ImmutableArray<string>.Empty
                    : emission.AffectedColumns,
            })
        {
            TableName = targetTable,
        };

    private static Observation BuildValidationObservation(
        ValidationError error,
        Batch batch,
        string targetTable,
        long fileLogId,
        Record record) =>
        new(
            ObservationSeverity.Error,
            error.Code,
            error.Message,
            batch.Id.Value,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>
            {
                ["column"] = error.Column,
                ["attempted_value"] = error.AttemptedValue,
                ["source_file"] = record.SourceFileName,
                ["source_row_index"] = record.SourceRowIndex,
                ["target_table"] = targetTable,
            })
        {
            FileLogId = fileLogId,
            TableName = targetTable,
        };

    private static async Task PublishDrainedEventsAsync(
        Batch batch,
        PerBatchScope scope,
        List<Observation> collected,
        CancellationToken cancellationToken)
    {
        foreach (var ev in batch.DrainEvents())
        {
            var observation = await scope.EventPublisher.PublishAsync(ev, cancellationToken).ConfigureAwait(false);
            collected.Add(observation);
        }
    }

    private static async Task EmitAsync(
        PerBatchScope scope,
        List<Observation> collected,
        Observation observation,
        CancellationToken cancellationToken)
    {
        await scope.Sink.RecordAsync(observation, cancellationToken).ConfigureAwait(false);
        collected.Add(observation);
    }

    private static async IAsyncEnumerable<Record> ToAsyncEnumerable(
        IEnumerable<Record> records,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }
}
