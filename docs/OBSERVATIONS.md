# OBSERVATIONS.md — Streamline Observation Taxonomy

**Observations are structured records of things the engine noticed
during a batch.** They are not logs (those are for developers) and not
exceptions (those represent failures). Observations sit between: they
capture ETL events with enough structure to be queried, counted,
alerted on, or ignored — per the operator's choice.

This document defines the observation model, the stable set of codes,
severity conventions, and where observations are emitted in the engine.

---

## Model

Every observation has:

| Field | Type | Purpose |
|---|---|---|
| `observation_id` | bigint (auto) | unique row in `observation_log` |
| `batch_id` | varchar | which batch produced this |
| `file_log_id` | bigint, nullable | which file, if applicable |
| `incoming_id` | bigint, nullable | which row, if applicable |
| `table_name` | varchar, nullable | which target table, if applicable |
| `severity` | enum | Info / Warning / Error / Critical |
| `code` | varchar | stable identifier — see codes below |
| `message` | text | human-readable description |
| `context` | JSONB | structured payload relevant to the event |
| `raised_at` | timestamptz | when the engine observed it |

**Stable codes** mean the same code always means the same thing.
Codes never change semantics between releases. Adding a new code is
fine; renaming or repurposing an existing one is a breaking change.

**Severity is an engine judgment, not a config choice.** Operators
cannot downgrade an `Error` to a `Warning` via config. They can choose
what to *do* about a severity (block the batch, just log, send to
alerting), but they cannot rewrite what the engine thinks happened.

The exceptions are policy-driven severities — e.g., schema drift's
severity depends on the registry entry's `drift_policy`. See each code's
entry below.

---

## Severity Levels

### Info

*Something happened worth recording, no action needed.*

Operators who want a quiet log filter `Info` out. Info exists so that
post-hoc analysis (e.g., "did this batch retry?") has data to work
with.

Examples:
- A file was successfully read and staged.
- A transformer was invoked.
- A retry succeeded on first re-attempt.
- An advisory lock was acquired and released normally.
- A reconciliation passed within tolerance.

### Warning

*Something non-ideal happened but the batch proceeded.*

Warnings are the most operator-interesting category. They're the "you
probably want to know about this, but it didn't fail." Accumulating
warnings across batches is an early signal that something's drifting.

Examples:
- Schema drift detected (new column present, with policy `warn`).
- FK cache was empty for a referenced parent table (policy
  `when_parent_populated`).
- A zip entry was skipped per `on_unmatched_entry: ignore`.
- An optional column was missing from the source file.
- A transformer returned fewer rows than expected.
- A file took significantly longer to read than historical baseline.

### Error

*Something failed but was handled.*

Errors mean a row was quarantined, a savepoint was rolled back, or a
specific operation didn't complete — but the engine caught it and
moved on. The batch may still succeed overall.

Examples:
- Row failed validation (specific code: `VALIDATION_FAILED`,
  `MISSING_REQUIRED`, `INVALID_TYPE`, etc.).
- FK violation during validation.
- Transformer returned `rows_failed > 0`.
- Savepoint rolled back due to destination write failure.
- A specific zip entry was corrupted (with the rest of the zip still
  processable per policy).

### Critical

*Something failed that prevents batch completion.*

Criticals fail the batch. Operators must intervene before the batch
can resume (via retry, reconcile, or manual action).

Examples:
- File could not be read at all (corruption, missing, permissions).
- Required registry entry is missing.
- Schema drift with `block` policy detected.
- Deferred transaction failed at commit.
- Advisory lock held by another process.
- Unknown exception not caught by any handler.

---

## Observation Codes

The canonical list lives in `Streamline.Core.Observations.ObservationCodes`
as a static class with `const string` fields. This document mirrors
that list and documents the context shape for each code.

**Naming convention:** `UPPER_SNAKE_CASE`, subject-verb-object where
possible. Stable; never renamed.

### Ingestion

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `FILE_INGESTED` | Info | A file finished staging successfully. | `{rows_staged, rows_failed, duration_ms}` |
| `FILE_SKIPPED_DUPLICATE` | Info | File hash matches a previously ingested file. | `{previous_batch_id, previous_file_log_id}` |
| `FILE_READ_FAILED` | Critical | File could not be opened or parsed at the file level. | `{error_message, underlying_exception}` |
| `FILE_ENCODING_DETECTED` | Info | Encoding was auto-detected (not explicitly configured). | `{detected_encoding, method}` |
| `FILE_EMPTY` | Warning | File contained no data rows (header only, or zero bytes). | `{file_size}` |
| `HEADER_NOT_FOUND` | Critical | `find_by_pattern` header strategy found no matching row. | `{pattern, rows_scanned}` |

### Schema Drift

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `SCHEMA_DRIFT_NEW_COLUMN` | Warning or Critical (per policy) | Source file has columns not in registry. | `{new_columns: [...]}` |
| `SCHEMA_DRIFT_MISSING_REQUIRED` | Critical | Source file is missing a column marked `required` in registry. | `{missing_columns: [...]}` |
| `SCHEMA_DRIFT_MISSING_OPTIONAL` | Warning | Source file is missing a non-required column. | `{missing_columns: [...]}` |
| `SCHEMA_DRIFT_BLOCKED` | Critical | Drift policy is `block`; ingestion rejected the file. | `{drift_summary: {...}}` |

### Validation

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `MISSING_REQUIRED` | Error | A required field is null or empty. | `{column, row_index}` |
| `INVALID_TYPE` | Error | Value cannot be cast to declared type. | `{column, value, expected_type}` |
| `INVALID_FORMAT` | Error | Value doesn't match declared pattern or format. | `{column, value, pattern}` |
| `INVALID_LENGTH` | Error | String exceeds `max_length` or fails other length check. | `{column, value_length, max_length}` |
| `INVALID_RANGE` | Error | Value outside min/max range. | `{column, value, min, max}` |
| `FK_VIOLATION` | Error | Referenced parent row doesn't exist. | `{column, value, fk_ref}` |
| `PK_VIOLATION` | Error | Duplicate primary key within batch or against destination. | `{pk_columns, pk_values}` |
| `VALIDATION_FAILED` | Error | Custom validation rule failed (catch-all). | `{rule_name, details}` |

### FK Resolution

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `FK_CACHE_EMPTY` | Warning | Parent table returned no values during FK preload; FK validation may be skipped per policy. | `{fk_ref, enforcement}` |
| `FK_CACHE_LOADED` | Info | FK cache populated successfully. | `{fk_ref, value_count, duration_ms}` |

### Transformation

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `TRANSFORMER_INVOKED` | Info | A transformer was called for a batch + table. | `{reference, kind, duration_ms}` |
| `TRANSFORMER_COMPLETED` | Info | Transformer returned a clean outcome (no failures). | `{reference, rows_inserted, rows_updated, rows_skipped}` |
| `TRANSFORMER_PARTIAL_FAILURE` | Error | Transformer returned `rows_failed > 0`. | `{reference, rows_failed, error_message}` |
| `TRANSFORMER_THREW` | Critical | Transformer threw an exception. | `{reference, exception_type, message}` |
| `TRANSFORMER_NOT_FOUND` | Critical | Registry references a transformer that isn't registered. | `{reference, kind}` |
| `TRANSFORM_MODE_DEFERRED` | Warning | A transform-mode registry entry was encountered by an engine version that doesn't yet implement transformer invocation (Phase 1 v1). The entry is skipped; ship Phase 4 to enable it. Distinct from `TRANSFORMER_NOT_FOUND`, which fires when the registry references a transformer the runtime cannot resolve. | `{table, reference, kind}` |

### Destination Writes (Replication case)

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `UPSERT_COMPLETED` | Info | Upsert for a batch + table succeeded. | `{table, inserted, updated, unchanged, duration_ms}` |
| `UPSERT_FAILED` | Error | Specific upsert operation failed (savepoint rolled back). | `{table, rows_affected, underlying_exception}` |
| `SAVEPOINT_ROLLED_BACK` | Error | Table-level savepoint was rolled back. | `{table, reason}` |

### Reconciliation

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `RECONCILIATION_PASSED` | Info | Row-count reconciliation at batch completion matched. | `{counts: {...}}` |
| `RECONCILIATION_MISMATCH` | Warning or Error (per magnitude) | Counts don't reconcile. Warning for small mismatches; Error for large. | `{expected_counts, actual_counts, delta}` |
| `RECONCILIATION_CHECK_FAILED` | Error | Custom reconciliation check (Phase 9) returned fail. | `{check_name, message, measured_values}` |

### Container Handling (zip)

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `ZIP_UNPACKED` | Info | Zip was opened and dispatched successfully. | `{entry_count, dispatch_mode}` |
| `ZIP_ENTRY_UNMATCHED` | Warning or Critical (per policy) | Entry matched no file mapping (route mode) or expected pattern (combine mode). | `{entry_name, policy}` |
| `ZIP_ENTRY_CORRUPTED` | Error or Critical | Entry couldn't be read. Critical if policy is "fail whole zip." | `{entry_name, underlying_exception}` |
| `ZIP_NESTING_EXCEEDED` | Critical | Zip contained another zip beyond the configured depth cap. | `{depth_encountered, depth_cap}` |

### Retry and Recovery

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `BATCH_RETRIED` | Info | Batch was resumed via `--retry`. | `{previous_failure, rows_reset}` |
| `BATCH_RECONCILED` | Info | Manual reconcile command completed. | `{rows_reset, tables_affected}` |
| `QUARANTINE_RESOLVED` | Info | Operator marked quarantined rows as resolved. | `{row_count, resolver}` |

### Batch Lifecycle

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `BATCH_STARTED` | Info | New batch created. | `{source_directory, created_by}` |
| `BATCH_COMPLETED` | Info | Batch finished with no critical issues. | `{duration_ms, summary}` |
| `BATCH_FAILED` | Critical | Batch ended in failed state. | `{reason, last_step}` |
| `ADVISORY_LOCK_CONTENDED` | Warning | Another process held the advisory lock; this invocation waited or skipped. | `{waited_ms, acquired}` |

### Catch-all

| Code | Severity | Emitted When | Context |
|---|---|---|---|
| `UNKNOWN` | Error or Critical | Unexpected exception not otherwise categorized. | `{exception_type, message, stack_trace_ref}` |

Use `UNKNOWN` sparingly. Every time it fires in production is a signal
that a new, more specific code should be added.

---

## Where Observations Are Emitted

Observations are emitted in these locations:

- **File reader** emits `FILE_INGESTED`, `FILE_READ_FAILED`,
  `FILE_EMPTY`, `FILE_ENCODING_DETECTED`, schema drift codes.
- **File dispatcher** emits `ZIP_UNPACKED`, `ZIP_ENTRY_*`,
  `ZIP_NESTING_EXCEEDED`.
- **Validation** emits all validation codes
  (`MISSING_REQUIRED`, `INVALID_TYPE`, etc.).
- **FK resolver** emits `FK_CACHE_*` codes.
- **Processing orchestrator** emits `UPSERT_*`, `SAVEPOINT_ROLLED_BACK`.
- **Transformer registry** emits `TRANSFORMER_*` codes.
- **Reconciliation runner** emits `RECONCILIATION_*` codes.
- **Batch lifecycle** (orchestrator / retry command) emits `BATCH_*`,
  `ADVISORY_LOCK_*`, `QUARANTINE_RESOLVED`.

No component should emit observations outside its responsibility. A
file reader that emits `UPSERT_FAILED` is doing something wrong.

---

## How the Engine Uses Observations

Observations drive:

- **`--inspect <batch>`** groups and counts observations by severity
  and code.
- **Batch status** — a batch with any `Critical` observation is
  `failed`. A batch with only `Error` and below can still be
  `completed` (partial success is a legitimate outcome).
- **Reconciliation** reads observations as inputs (how many
  validation failures, how many transformer partial failures).
- **Alerting integration** (out of scope for v1 but designed for) —
  an external alerting tool can subscribe to observations at or above
  a configured severity.

Observations are **not** used for:
- Control flow inside the engine. The engine reacts to actual state
  (row statuses, transformer return values), not to observation
  emission.
- Replacing exceptions. Exceptions still exist for programmer errors
  and unexpected conditions.

---

## Adding a New Observation Code

Process:

1. **Justify it.** Is an existing code close enough? If yes, use it
   with a richer `context` payload.
2. **Propose it.** In a PR, add the code to
   `Streamline.Core.Observations.ObservationCodes` and to this document
   (in the appropriate section table).
3. **Assign severity thoughtfully.** The severity table at the top of
   this doc is the guide. Don't make something `Critical` just because
   it seems bad; Critical means "batch cannot complete."
4. **Emit it.** In exactly one place, with a stable context shape.
5. **Test it.** At least one test must assert the code fires with the
   right severity and context for its trigger condition.

Removing a code is a breaking change. Consumers may have built
dashboards or alerts on specific codes. Deprecate first (mark
`[Obsolete]`), remove in a later major version.

---

## For Agents: How to Think About Observations

When implementing a feature or fixing a bug:

1. **What ETL events does this code touch?** List them.
2. **Which observation code should fire for each event?** Use the
   table above. If no code fits, propose a new one (see "Adding a
   New Observation Code").
3. **Is the severity appropriate?** Apply the severity rubric.
4. **Does the context payload give an operator enough to
   investigate?** A code + message without context is a dead-end
   observation. Include IDs, counts, filenames, anything that helps
   diagnose.
5. **Are there tests asserting the right code fires?** If not, add
   them.

**Anti-patterns to avoid:**

- Emitting `UNKNOWN` when a specific code would fit.
- Using exceptions for control flow and skipping the observation.
- Emitting at `Info` when it should be `Warning` (hides the signal).
- Emitting at `Critical` when it should be `Error` (cries wolf).
- Stuffing all context into `message` as prose instead of structured
  fields in `context`.
- Adding a new code without adding its row to this doc.

Observations are the primary way operators (and future agents)
understand what happened in a batch. Treat them as first-class API,
not as logging.
