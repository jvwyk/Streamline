# PARKED.md — Deferred decisions

A running ledger of decisions that surfaced during a sub-phase but
weren't yet ready to make. Each entry names where it surfaced, where
it gets revisited, and what the call needs to address. Review at the
start of every sub-phase whose name appears in **Resolve in**.

This is not a backlog of work. It is a backlog of **calls to make**.
When a phase opens that owns one of these decisions, the agent's
plan-round must surface the parked item and propose a resolution.

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

### P-5 — TimeProvider injection in ResilientObservationSink

| | |
|---|---|
| **Surfaced in** | Sub-phase 1f-i (commit 5, hash `a148632`). Initial design took a `TimeProvider` constructor parameter (defaulting to `TimeProvider.System`) so tests could inject a `FakeTimeProvider` and advance the clock without real delays. The design deadlocked the test runner: `Task.Delay(TimeSpan, FakeTimeProvider, CancellationToken)` plus xUnit v3's async dispatch plus NSubstitute's `.Returns(callback)` had an interaction that hung `dotnet test` indefinitely on multiple attempts. |
| **Resolve in** | Whichever sub-phase introduces a genuine clock-driven need in the sink. None today. |
| **Question** | Should `ResilientObservationSink` carry a `TimeProvider` again, or stay with the explicit-backoff-list shape it ended up with? |
| **Context / leanings** | Replacement: backoff delays are passed via constructor as `IReadOnlyList<TimeSpan>`. Production callers use `ResilientObservationSink.WithDefaults(...)` for the v1 50/200/800ms schedule; tests construct with zero-delay arrays so the suite runs in milliseconds. Functionally equivalent for what the sink does today; arguably cleaner because backoff schedules are data, not behavior. The `TimeProvider` route is still the right choice when a sub-phase has a genuine clock-driven invariant (e.g., timestamping inside the sink, measuring elapsed time, scheduling future work) — none of those exist today. Don't reintroduce until a real consumer needs it. |
| **Status** | parked |

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
