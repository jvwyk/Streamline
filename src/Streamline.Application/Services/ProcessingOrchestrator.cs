using System.Diagnostics;
using Streamline.Core.Enums;
using Streamline.Core.Observations;
using Streamline.Core.Results;
using Streamline.Core.ValueTypes;
using Streamline.Domain.Abstractions;
using Streamline.Domain.Batches;
using Streamline.Domain.Registry;
using Streamline.Domain.Validation;

namespace Streamline.Application.Services;

/// <summary>
/// Drives one batch from <see cref="Batch.BeginProcessing"/> through
/// to <see cref="Batch.Complete"/> (or <see cref="Batch.Fail"/> on
/// unrecoverable error). For each registry entry in load order:
/// preloads FKs once for the batch, opens a per-table savepoint,
/// claims pending rows from staging, validates FKs, quarantines FK
/// failures, upserts the rest into the destination, and marks them
/// <see cref="RowStatus.Committed"/>. Per-table failures roll back
/// only that table's savepoint and continue with the next table;
/// non-table-level failures (FK preload, registry resolution,
/// outer-transaction commit) abort the batch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Replication only in v1.</b> Entries with
/// <see cref="RegistryEntry.IsTransform"/> are skipped with a
/// <c>TRANSFORMER_NOT_FOUND</c> observation at <c>Warning</c> for
/// visibility — transform-mode invocation lands in Phase 4 along
/// with <see cref="ITransformerRegistry"/> wiring. The orchestrator
/// does not invoke transformers itself.
/// </para>
/// <para>
/// <b>FK cache scoping.</b> A fresh <see cref="FkResolver"/> is
/// constructed per <see cref="ExecuteAsync"/> call so the cache
/// cannot leak across batches — predecessor flag #11. The resolver
/// is never shared with other batches or processors.
/// </para>
/// <para>
/// <b>Per-table savepoints.</b> A failure during one table's upsert
/// rolls back that table's savepoint and marks claimed rows as
/// <see cref="RowStatus.RolledBack"/> (legal T4); the outer
/// transaction continues, and other tables in the batch can still
/// commit. The batch-level <see cref="Batch.Complete"/> still fires
/// at the end if the outer transaction commits.
/// </para>
/// <para>
/// <b>Batch lifecycle on exception.</b> Anything thrown outside a
/// table's per-table try block (FK preload, registry call,
/// transaction commit, cancellation) is caught by the outer try,
/// the batch is moved to <see cref="BatchStatus.Failed"/>, and the
/// exception is rethrown for the handler to observe.
/// </para>
/// </remarks>
public sealed class ProcessingOrchestrator
{
    /// <summary>
    /// Per-call upper bound on the number of pending rows to claim
    /// from staging in one <see cref="IStagingRepository.GetPendingRowsAsync"/>
    /// call. Hardcoded for v1; tuning lands when Phase 2's Postgres
    /// adapter shows real numbers.
    /// </summary>
    public const int PendingRowBatchSize = 10_000;

    private readonly IRegistryRepository _registry;
    private readonly IStagingRepository _staging;
    private readonly IDestinationAdapter _destination;

    public ProcessingOrchestrator(
        IRegistryRepository registry,
        IStagingRepository staging,
        IDestinationAdapter destination)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(destination);

        _registry = registry;
        _staging = staging;
        _destination = destination;
    }

    public async Task<ProcessingResult> ExecuteAsync(
        Batch batch,
        PerBatchScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(scope);

        var collected = new List<Observation>();
        var tableOutcomes = new List<TableProcessingOutcome>();

        batch.BeginProcessing();
        await PublishDrainedEventsAsync(batch, scope, collected, cancellationToken).ConfigureAwait(false);

        try
        {
            var loadOrder = await _registry.GetLoadOrderAsync(cancellationToken).ConfigureAwait(false);
            var entries = await ResolveEntriesAsync(loadOrder, cancellationToken).ConfigureAwait(false);

            await using var txn = await _destination.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var fkResolver = new FkResolver();
            await PreloadFksAsync(entries, fkResolver, txn, cancellationToken).ConfigureAwait(false);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsTransform)
                {
                    await EmitAsync(scope, collected, new Observation(
                        ObservationSeverity.Warning,
                        ObservationCodes.TRANSFORMER_NOT_FOUND,
                        $"Transform-mode entry '{entry.TableName}' skipped — engine-driven transformer invocation is a Phase 4 work item.",
                        batch.Id.Value,
                        DateTimeOffset.UtcNow)
                    {
                        TableName = entry.TableName,
                    }, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var outcome = await ProcessTableAsync(
                    batch, entry, txn, fkResolver, scope, collected, cancellationToken).ConfigureAwait(false);
                tableOutcomes.Add(outcome);
            }

            await txn.CommitAsync(cancellationToken).ConfigureAwait(false);

            batch.Complete();
            await PublishDrainedEventsAsync(batch, scope, collected, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Don't transition to Failed on cancellation — let the
            // handler decide. Cancellation isn't a batch failure.
            throw;
        }
        catch (Exception ex)
        {
            batch.Fail(ex.Message);
            await PublishDrainedEventsAsync(batch, scope, collected, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return new ProcessingResult(
            tableOutcomes, collected, scope.DegradationState.IsDegraded);
    }

    private async Task<List<RegistryEntry>> ResolveEntriesAsync(
        IReadOnlyList<string> loadOrder, CancellationToken cancellationToken)
    {
        var entries = new List<RegistryEntry>();
        foreach (var tableName in loadOrder)
        {
            var entry = await _registry.GetActiveAsync(tableName, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }
        return entries;
    }

    private static async Task PreloadFksAsync(
        IReadOnlyList<RegistryEntry> entries,
        FkResolver fkResolver,
        ITransactionScope txn,
        CancellationToken cancellationToken)
    {
        var byName = entries.ToDictionary(e => e.TableName, StringComparer.Ordinal);
        var loaded = new HashSet<FkReference>();

        foreach (var entry in entries)
        {
            foreach (var column in entry.Schema.Columns)
            {
                var fk = column.FkReference;
                if (fk is null || !loaded.Add(fk))
                {
                    continue;
                }

                if (!byName.TryGetValue(fk.ParentTable, out var parent) || parent.TargetSchema is null)
                {
                    // Parent isn't in the active load set or doesn't
                    // have a destination schema declared — skip the
                    // preload; FkResolver.Validate will throw if the
                    // FK is referenced anyway, which surfaces the bug
                    // loudly.
                    continue;
                }

                await fkResolver.LoadAsync(fk, parent.TargetSchema, txn, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<TableProcessingOutcome> ProcessTableAsync(
        Batch batch,
        RegistryEntry entry,
        ITransactionScope txn,
        FkResolver fkResolver,
        PerBatchScope scope,
        List<Observation> collected,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long rowsCommitted = 0;
        long rowsRolledBack = 0;
        long rowsQuarantined = 0;

        var savepointName = $"sp_{entry.TableName}";
        await using var savepoint = await txn.BeginNestedScopeAsync(savepointName, cancellationToken)
            .ConfigureAwait(false);

        var validated = new List<StagedRow>();

        await foreach (var row in _staging.GetPendingRowsAsync(
            batch.Id, entry.TableName, PendingRowBatchSize, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fkFailure = FindFkFailure(row, entry, fkResolver);
            if (fkFailure is null)
            {
                validated.Add(row);
            }
            else
            {
                batch.QuarantineRow(row, ObservationCodes.FK_VIOLATION, fkFailure);
                await _staging.QuarantineAsync(row.IncomingId, ObservationCodes.FK_VIOLATION, fkFailure, cancellationToken)
                    .ConfigureAwait(false);
                rowsQuarantined++;
            }
        }

        await PublishDrainedEventsAsync(batch, scope, collected, cancellationToken).ConfigureAwait(false);

        try
        {
            if (validated.Count > 0 && entry.TargetSchema is not null)
            {
                var typedRecords = validated.Select(r => r.Record).ToList();
                var upsert = await txn.UpsertAsync(
                    entry.TargetSchema, entry.TableName, entry.Schema, typedRecords, cancellationToken)
                    .ConfigureAwait(false);

                await EmitAsync(scope, collected, new Observation(
                    ObservationSeverity.Info,
                    ObservationCodes.UPSERT_COMPLETED,
                    $"Upsert into '{entry.TargetSchema}.{entry.TableName}': {upsert.RowsInserted} inserted, {upsert.RowsUpdated} updated, {upsert.RowsUnchanged} unchanged.",
                    batch.Id.Value,
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, object?>
                    {
                        ["rows_inserted"] = upsert.RowsInserted,
                        ["rows_updated"] = upsert.RowsUpdated,
                        ["rows_unchanged"] = upsert.RowsUnchanged,
                        ["target_schema"] = entry.TargetSchema,
                        ["target_table"] = entry.TableName,
                    })
                {
                    TableName = entry.TableName,
                }, cancellationToken).ConfigureAwait(false);
            }

            foreach (var row in validated)
            {
                batch.TransitionRow(row, RowStatus.Committed);
                await _staging.TransitionAsync(
                    row.IncomingId, RowStatus.Processing, RowStatus.Committed, cancellationToken)
                    .ConfigureAwait(false);
                rowsCommitted++;
            }

            await savepoint.ReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per-table failure: roll back this table's savepoint and
            // mark every claimed-but-not-yet-committed row as
            // RolledBack. Continue with the next table — outer
            // transaction is unaffected per the savepoint contract.
            await savepoint.RollbackAsync(cancellationToken).ConfigureAwait(false);

            await EmitAsync(scope, collected, new Observation(
                ObservationSeverity.Error,
                ObservationCodes.UPSERT_FAILED,
                $"Upsert into '{entry.TargetSchema ?? "(none)"}.{entry.TableName}' failed: {ex.Message}.",
                batch.Id.Value,
                DateTimeOffset.UtcNow,
                new Dictionary<string, object?>
                {
                    ["target_schema"] = entry.TargetSchema,
                    ["target_table"] = entry.TableName,
                    ["error"] = ex.Message,
                })
            {
                TableName = entry.TableName,
            }, cancellationToken).ConfigureAwait(false);

            await EmitAsync(scope, collected, new Observation(
                ObservationSeverity.Warning,
                ObservationCodes.SAVEPOINT_ROLLED_BACK,
                $"Savepoint '{savepointName}' rolled back.",
                batch.Id.Value,
                DateTimeOffset.UtcNow,
                new Dictionary<string, object?>
                {
                    ["savepoint"] = savepointName,
                    ["target_table"] = entry.TableName,
                })
            {
                TableName = entry.TableName,
            }, cancellationToken).ConfigureAwait(false);

            // Mark every validated row as RolledBack so staging is
            // consistent with the destination's actual state.
            foreach (var row in validated)
            {
                batch.TransitionRow(row, RowStatus.RolledBack);
                await _staging.TransitionAsync(
                    row.IncomingId, RowStatus.Processing, RowStatus.RolledBack, cancellationToken)
                    .ConfigureAwait(false);
                rowsRolledBack++;
            }
        }

        stopwatch.Stop();
        return new TableProcessingOutcome(
            entry.TableName, rowsCommitted, rowsRolledBack, rowsQuarantined, stopwatch.Elapsed);
    }

    private static string? FindFkFailure(StagedRow row, RegistryEntry entry, FkResolver fkResolver)
    {
        foreach (var column in entry.Schema.Columns)
        {
            var fk = column.FkReference;
            if (fk is null || !fkResolver.IsLoaded(fk))
            {
                continue;
            }

            // FkResolver compares raw strings. Records here may be
            // typed (post-validation), so render via ToString() with
            // a null-safe path. Round-trip imprecision is documented
            // on FkResolver — for v1, ToString() is sufficient given
            // the cache holds whatever GetDistinctColumnValuesAsync
            // returns.
            var rawValue = row.Record.Values.TryGetValue(column.Name, out var v)
                ? v?.ToString()
                : null;

            if (!fkResolver.Validate(fk, rawValue))
            {
                return $"Column '{column.Name}' value '{rawValue ?? "<null>"}' has no parent in {fk.ParentTable}.{fk.ParentColumn}.";
            }
        }
        return null;
    }

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
}
