# PARKED.md — Deferred decisions

A running ledger of decisions that surfaced during a sub-phase but
weren't yet ready to make. Each entry names where it surfaced, where
it gets revisited, and what the call needs to address. Review at the
start of every sub-phase whose name appears in **Resolve in**.

This is not a backlog of work. It is a backlog of **calls to make**.
When a phase opens that owns one of these decisions, the agent's
plan-round must surface the parked item and propose a resolution.

> **Surrounding context for new agents.** If you're a new agent landing
> on this repository, read `docs/ONBOARDING.md` first for the reading
> order. The four handover docs surround this one:
> `HANDOVER.md` (working knowledge — disciplines, how the user works,
> warnings), `STANDARDS.md` (the rules indexed),
> `PHASE-N-CLOSEOUT.md` (frozen artifacts of each phase). PARKED.md is
> consulted constantly during work; the others establish the operating
> model that produced Phase 1's quality.

---

## How to read this file

| Field | Meaning |
|---|---|
| **ID** | Stable reference (`P-1`, `P-2`, ...). Cite in commit messages and reports. |
| **Surfaced in** | Sub-phase / commit where the decision became visible. |
| **Resolve in** | Sub-phase or phase that owns the call. |
| **Question** | The actual decision to be made. |
| **Context / leanings** | What's known so far, options considered, the agent's preference if any. |
| **Status** | `parked`, `in progress`, `resolved → <pointer>`. |

When a decision lands, change Status to `resolved` and link to the
commit / doc / PR. Don't delete entries — historical context is
useful when revisiting.

---

## Active deferrals

### Phase 4 cluster — operational-recovery items

The deferrals tagged "Resolve in: Phase 4" — currently P-3, P-6,
P-7, P-8, and P-10 — are not independent additions to the Phase 4
backlog. They are an **operational-recovery sub-phase scope** that
Phase 4's planning round must address as a unit, not piecemeal.

The original Phase 4 scope ("operational features: transformers,
lineage, built-in reconciliation") is too narrow to absorb these
items silently. Phase 4 design should open with:

- The original three items (transformer invocation,
  row-level lineage population for engine-driven writes,
  `BuiltinReconciliationRunner`).
- Plus the operational-recovery cluster:
  P-3 (bounded retry policy),
  P-6 (handler ↔ orchestrator observation flow asymmetry),
  P-7 (stuck-in-Ingesting recovery via `MarkBatchFailedCommand`),
  P-8 (stuck-in-Processing recovery via `ResetClaimedAsync` or
  claim-timeout),
  P-10 (reconciliation gap closure — overlaps with the original
  reconciliation runner work).

Estimated impact: original Phase 4 estimate of ~3 weeks expands
to ~5–6 weeks once these items are absorbed. Treating them as
silent additions instead of acknowledged scope is how phases slip;
calling them out keeps the plan honest.

P-9 (file-hash dedup) targets Phase 2 by default but is genuinely
Phase 2 *or* Phase 4 — when Phase 2 design opens, decide whether
to ship dedup with the Postgres adapter or defer to Phase 4's
operational cluster.

### P-3 — Bounded retry policy

| | |
|---|---|
| **Surfaced in** | Sub-phase 1d (commit 4, predecessor-bug review). The original "retry loops" bug was misclassified as state-machine; transitions in a retry loop (`RolledBack → Pending → Processing → RolledBack`) are individually legal. The bug is orchestrator-level: the engine should give up after N retries against the same underlying error code. |
| **Resolve in** | Sub-phase 1i (predecessor-bug regression tests at the orchestrator level) or Phase 4 (operational features — `RetryBatchCommand` is a Phase 4 work item per plan §6). |
| **Question** | What's the bounded-retry policy? Specifically: (a) maximum retry count per row before the row is forced to a terminal Quarantined state, (b) whether the count is per-row or per-(row, error_code), (c) whether resolved Quarantined rows reset the retry counter when they re-enter Pending. |
| **Context / leanings** | None yet. The state machine doesn't carry a counter; that would have to live on the staging row or in a sidecar table. Phase 2's Postgres schema is the natural place to add a `retry_count` column on `staging.incoming`. Deferring; revisit when 1j or Phase 4 actively designs `RetryBatchCommand`. |
| **Status** | parked |

### P-4 — Decimal precision / scale tracking on ColumnDefinition

| | |
|---|---|
| **Surfaced in** | Sub-phase 1e (commit 2, ColumnTypeParser). v1 parses `Decimal` as `decimal` and uses `MinValue`/`MaxValue` for bounded ranges. There's no precision/scale on `ColumnDefinition`. |
| **Resolve in** | Phase 6 (job migrations) — or earlier if a Phase 3 reader needs it. |
| **Question** | Should `ColumnDefinition` gain `Precision` and `Scale` fields, and should `RowValidator` enforce them? |
| **Context / leanings** | bounds-only validation handles most ranges, but a Postgres `NUMERIC(10,2)` mismatch with a value that has more than 2 fractional digits would silently round at insert time. If Phase 6 migrations surface a real case, add the fields and the corresponding INVALID_PRECISION code (currently not in OBSERVATIONS.md). Until then: YAGNI. |
| **Status** | parked |

### P-5 — TimeProvider injection (lifted to Phase 2)

| | |
|---|---|
| **Surfaced in** | Sub-phase 1f-i (commit 5, hash `a148632`). Initial design took a `TimeProvider` constructor parameter on `ResilientObservationSink` (defaulting to `TimeProvider.System`) so tests could inject a `FakeTimeProvider`. Deadlocked the test runner: `Task.Delay(TimeSpan, FakeTimeProvider, CancellationToken)` plus xUnit v3's async dispatch plus NSubstitute's `.Returns(callback)` hung `dotnet test` indefinitely. Replacement: backoff delays passed as `IReadOnlyList<TimeSpan>`. |
| **Resolve in** | Phase 2. **Lifted from "when needed" because Phase 2 has genuine clock-driven need.** `InMemoryRegistryRepository`'s "active right now" filter uses `DateTimeOffset.UtcNow`; Phase 2's `PostgresRegistryRepository` will mirror it; contract-parity tests need controllable time to exercise date-boundary scenarios cleanly. Without a `TimeProvider` injected at the registry-repository layer (and matching test fixtures), every time-dependent contract-parity test risks date-boundary flakiness. |
| **Question** | Where does `TimeProvider` enter the design? Lean: at `IRegistryRepository` (where date-windowed "active" filtering already reads `UtcNow`). Don't reintroduce on `ResilientObservationSink` itself — its lockup history makes `TimeProvider` there expensive; the explicit-backoff-list shape continues to work for the sink. |
| **Context / leanings** | Phase 2 design must inject `TimeProvider` into `InMemoryRegistryRepository` (constructor parameter, defaulting to `TimeProvider.System`) and `PostgresRegistryRepository`. Tests that exercise the active-window filter at date boundaries pass a `FakeTimeProvider`. Same pattern can extend to other registries (`InMemoryFileMappingRepository`, lineage) if any of them gain time-dependent reads — none do today, so don't over-eagerly inject. The `ResilientObservationSink` stays as-is. Cheap to do in Phase 2; expensive to retrofit if Phase 2's contract-parity tests calcify around `DateTimeOffset.UtcNow`. |
| **Status** | parked → first-tier Phase 2 design item |

### P-6 — Observation flow asymmetry between handler and orchestrator

| | |
|---|---|
| **Surfaced in** | Sub-phase 1f-ii (commit 10, `679eb7b`). `RetryBatchHandler` emits `BATCH_RETRIED` before delegating to `ProcessingOrchestrator`; the observation reaches the inner sink but never lands in the orchestrator's local `collected` list, so it's missing from the returned `ProcessingResult.Observations`. Fixed by reconstructing the result with the retry observation prepended after the orchestrator returns. |
| **Resolve in** | Phase 4 if a second handler-side emission case arises, or earlier if the asymmetry causes a bug. |
| **Question** | Should `PerBatchScope` (or another shared surface) own the captured-observation list so both handler and orchestrator write into the same buffer, replacing the post-hoc result reconstruction? |
| **Context / leanings** | Today the asymmetry is real but contained. Result types' `Observations` field is documented as "the orchestrator's view"; the handler's pre/post emissions are merged in by hand. Works. The risk is silent drift: a future handler that emits an observation around an orchestrator call without remembering to merge produces a result that omits its own emissions. The fix when it matters: lift the captured-observation list onto `PerBatchScope` (e.g. `PerBatchScope.CapturedObservations`), have orchestrators and handlers both write through it, and return a result whose `Observations` reads from the scope rather than from a per-call local list. Don't refactor preemptively — the surface is single-call-site today, and a wrong abstraction is harder to undo than a noticed second case. |
| **Status** | parked |

### P-7 — Stuck-in-Ingesting recovery path

| | |
|---|---|
| **Surfaced in** | Sub-phase 1f-ii (commit 8, `2f25927`). `BatchStateMachine` has no `Ingesting → Failed` transition (locked in 1d). A batch that throws during ingestion stays in `Ingesting` forever; `IngestBatchHandler` does not transition to `Failed` because the state machine would reject it. Operators can inspect via `InspectBatchHandler` but have no command-level recovery. |
| **Resolve in** | Phase 4 (operator commands consolidate). |
| **Question** | How do operators recover a batch stuck in `Ingesting` after a thrown ingestion error? |
| **Context / leanings** | Three options. (a) Add a `MarkBatchFailedCommand` that operator-explicitly transitions a stuck batch to `Failed`, with an audit observation `BATCH_FAILED_BY_OPERATOR`. (b) Add `Ingesting → Failed` as a legal state-machine transition driven by some other signal. (c) Leave operators to manually update `batch_log` via SQL. (a) is the right answer: operator commands should be auditable; manual SQL is opaque. (b) couples state-machine semantics to recovery semantics, which is the wrong direction. (c) is what we have today by default and isn't acceptable long-term. Resolve when Phase 4 introduces operator commands. |
| **Status** | parked |

### P-8 — Stuck-in-Processing recovery after cancellation

| | |
|---|---|
| **Surfaced in** | Sub-phase 1h (planning round, formalised in commit 7's `ProcessingRows_AreNotResetByRetry_DocumentingP8` test). `IStagingRepository.GetPendingRowsAsync` atomically transitions claimed rows from `Pending` to `Processing` per the contract. If a batch is cancelled (or fails for a non-row-level reason) mid-processing, those rows remain in `Processing` indefinitely. `ResetForRetryAsync`'s contract is to reset only `RolledBack` rows; `Processing` rows have no recovery path. |
| **Resolve in** | Phase 4 (operator commands / claim-recovery semantics). Part of the operational-recovery cluster. |
| **Question** | How do orphaned `Processing` rows from a cancelled or failed batch return to `Pending` so they can be re-claimed? |
| **Context / leanings** | Two options. (a) Add a separate `ResetClaimedAsync` method that transitions `Processing → Pending` for orphaned rows during retry. (b) Add a timestamp-based timeout on `Processing` claims that `ResetForRetryAsync` honors when the claim is older than a threshold. (a) is more explicit but requires the caller to know "this batch was cancelled, reset the claims"; (b) is more automatic but adds a clock dependency. The fix affects both Phase 1 fakes and Phase 2 Postgres equally — designing here would mean designing two implementations when only one is needed today. Park it; let Phase 4 design the right semantics for both. The 1h test documents the current (correct-but-gappy) behaviour; when Phase 4 ships, the test gets updated to reflect the new recovery path. |
| **Status** | parked |

### P-9 — File-hash dedup (PB-3)

| | |
|---|---|
| **Surfaced in** | Sub-phase 1i (planning round). `FILE_SKIPPED_DUPLICATE` is in the `ObservationCodes` catalog (since 1b) but no orchestrator emits it and no staging surface tracks file hashes. PB-3 from the predecessor-bug list maps directly: a redelivered file with the same content was processed twice in the predecessor system, producing duplicate destination rows. |
| **Resolve in** | Phase 2 by default — dedup needs the staging schema to support a `staging.file_log.content_hash` column that the orchestrator can `SELECT EXISTS(...)` against. **Or** defer to Phase 4's operational-recovery cluster if Phase 2 is over-scoped. Decide during Phase 2 planning. |
| **Question** | Where does the dedup check live, and what does the staging contract look like? Lean: a new `IStagingRepository.IsFileAlreadyIngestedAsync(string contentHash, BatchId? exclude)` method invoked by `IngestionOrchestrator` before staging rows. Returns true → emit `FILE_SKIPPED_DUPLICATE` Info, skip the file with no rows. Returns false → proceed and persist the hash alongside `OpenFileLogAsync`. |
| **Context / leanings** | The hashing itself is straightforward (SHA-256 over the file bytes; for stream-based readers, hash as the stream is consumed). The harder design question is the dedup window: dedup against any prior batch ever, or only recent batches? Postgres can index `content_hash` cheaply enough to query the full history. Recommend "dedup against any prior batch where the file was successfully ingested" — re-staging a quarantined file should still run because the operator may have fixed the data. Phase 2 spec: `IsFileAlreadyIngestedAsync` returns true when a `file_log` row exists with the same `content_hash` AND its parent batch reached `Completed` status. |
| **Status** | parked |

### P-10 — Reconciliation gap (PB-7)

| | |
|---|---|
| **Surfaced in** | Sub-phase 1i (planning round). The Phase 1 result types carry the count fields (`RowsRead`, `RowsStaged`, `RowsCommitted`, `RowsQuarantined`, `RowsRolledBack`) but no automatic reconciliation runner compares them. Predecessor PB-7: row counts in vs out diverged silently when transformer logic dropped rows or ingestion lost rows mid-batch; operators only noticed via downstream data-quality complaints. |
| **Resolve in** | Phase 4 (`BuiltinReconciliationRunner` is on the Phase 4 plan). Part of the operational-recovery cluster. |
| **Question** | What reconciliation rules should `BuiltinReconciliationRunner` enforce, and at what severity does a mismatch fire? |
| **Context / leanings** | Two rules at minimum: (1) per-file `RowsRead == RowsStaged + RowsQuarantined`; (2) per-table `RowsCommitted + RowsRolledBack + RowsQuarantined` matches the count of staged rows for that table. Mismatch fires `RECONCILIATION_MISMATCH` at Warning, `RECONCILIATION_PASSED` at Info on success. Phase 4 designs whether mismatch triggers automatic batch-fail (probably not — let the operator decide) or just records the observation. The reconciliation runner reads from the result types after each handler returns, so it doesn't need staging-side queries — it inspects the in-memory outcome. No Phase 1 regression test because no Phase 1 implementation; the gap is closed in Phase 4 by the runner. |
| **Status** | parked |

### P-11 — Handler-to-persisted-batch-status bridging

| | |
|---|---|
| **Surfaced in** | Sub-phase 1h (Surface for next sub-phases note 1, formalised here in 1i close-out). Phase 1's handlers operate on the in-memory `Batch` aggregate; the persisted `BatchSnapshot` (returned by `IStagingRepository.GetBatchAsync`) is read at handler entry but not written back as the aggregate transitions. The 1h flow tests had to call `ForTestingOnly_SetBatchStatus(batch, BatchStatus.Ingested)` between handlers to drive the persisted snapshot forward; Phase 1 has no production-side path to do this automatically. |
| **Resolve in** | Phase 2 (Postgres infrastructure). **First-tier design question, not an implementation detail.** |
| **Question** | Who writes the persisted batch status as the aggregate transitions: the handler, the orchestrator, or a separate persistence pass? |
| **Context / leanings** | Three options. (a) The orchestrator writes the persisted status when each aggregate lifecycle method fires (e.g., `batch.Start()` triggers a staging-side `UpdateBatchStatusAsync`). (b) The handler writes the persisted status after the orchestrator returns (one write per transition the orchestrator drove). (c) A new `IStagingRepository.SaveBatchAsync(BatchSnapshot)` method that accepts the aggregate's current state and writes whatever the persisted form lacks. Lean (a): orchestrator emits a single status-update call per transition, persisted state stays in lockstep with the aggregate's view. The aggregate already exposes `Status` and `CompletedAt` for consumption. The risk if deferred: Phase 2 ships without bridging, contract-parity tests pass against fakes that don't exercise the bridge, production batches show "Processing in batch_log, Completed in aggregate" inconsistencies that surface as audit-trail gaps. **The single highest-risk parked decision.** Make it Phase 2's first-tier design question. Phase 2 planning commits to a specific `IStagingRepository` method signature for the bridging call (whether option-(a)-style `UpdateBatchStatusAsync(BatchId, BatchStatus, ...)`, option-(c)-style `SaveBatchAsync(BatchSnapshot)`, or another shape entirely); this entry describes direction, not specifics. |
| **Status** | parked → first-tier Phase 2 design item |

---

## Process

When you surface a new deferral:

1. Add an entry below the active list with the next free ID.
2. Cite the ID in the commit message that creates the entry.
3. Mention the entry in the sub-phase report so it's visible to the
   user before the next sub-phase opens.

When you resolve a deferral:

1. Change **Status** to `resolved → <commit hash or doc reference>`.
2. Move the entry to a "Resolved" section at the bottom (kept for
   historical context).
3. Note the resolution in the resolving commit's message.

---

## Resolved

### P-1 — IObservationSink failure semantics on the hot path

| | |
|---|---|
| **Surfaced in** | Sub-phase 1b (commit 3, IObservationSink interface). |
| **Resolved in** | Sub-phase 1f (design landed in 1f-i `a148632`; orchestrator wiring landed in 1f-ii commits 6–11). |
| **Question** | When `IObservationSink.RecordAsync` throws inside a per-row hot loop (e.g. `RowValidator` emitting `MISSING_REQUIRED` per row), should the orchestrator abort the row currently being processed, abort the whole batch, or let the in-flight row complete before bubbling? |
| **Resolution** | Tiered failure handling at the decorator level. `ResilientObservationSink` (1f-i `a148632`) implements 3 retries with backoff, 3-consecutive-failure degradation threshold, severity-based critical detection, and ILogger fallback for critical observations in degraded mode. `PerBatchScope.CreateForBatch` (1f-ii commit 6) wires one resilient sink + degradation-state pair per batch; `IngestionOrchestrator` (1f-ii commit 6), `ProcessingOrchestrator` (1f-ii commit 7), and the four handlers (1f-ii commits 8–11) all consume the scope. Per-row hot loops never see a sink throw — the decorator absorbs transient failures and degrades silently for non-critical observations once the threshold is crossed. The `ObservabilityDegraded` flag on `IngestionResult` and `ProcessingResult` surfaces the degradation to operators after the batch completes. |
| **Status** | resolved → 1f-i `a148632` (decorator) + 1f-ii commits 6–11 (orchestrator wiring) |

### P-2 — Observation threading through repositories

| | |
|---|---|
| **Surfaced in** | Sub-phase 1c (planning round). |
| **Resolved in** | Sub-phase 1f (`DomainEventPublisher` landed 1f-i `20b4719`; orchestrator emission paths landed 1f-ii commits 6–7). |
| **Question** | Should infrastructure interfaces (repositories, adapters) take an `IObservationSink` parameter on every call, or is observation emission strictly the orchestrator's job? |
| **Resolution** | Option (2) — orchestrators emit observations; repositories stay sink-free. `IngestionOrchestrator` (1f-ii commit 6) emits drift, validation, and file-ingested observations; `ProcessingOrchestrator` (1f-ii commit 7) emits upsert-completed, upsert-failed, savepoint-rolled-back, and FK-quarantine observations; both drain aggregate events through `DomainEventPublisher` for batch-lifecycle events. Repositories (`IStagingRepository`, `IRegistryRepository`, `IFileMappingRepository`, `IDestinationAdapter`, `ITransactionScope`) carry no `IObservationSink` parameters. Validated end-to-end across the orchestrator + handler test suites. |
| **Status** | resolved → 1f-i `20b4719` (publisher) + 1f-ii commits 6–7 (orchestrator emission) |
