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

### P-1 — IObservationSink failure semantics on the hot path

| | |
|---|---|
| **Surfaced in** | Sub-phase 1b (commit 3, IObservationSink interface). |
| **Resolve in** | Sub-phase 1f-ii (orchestrators wire the resilient sink in). |
| **Question** | When `IObservationSink.RecordAsync` throws inside a per-row hot loop (e.g. `RowValidator` emitting `MISSING_REQUIRED` per row), should the orchestrator abort the row currently being processed, abort the whole batch, or let the in-flight row complete before bubbling? |
| **Context / leanings** | Resolved at design time during the 1f planning round: option (d) — tiered failure handling at the decorator level. `ResilientObservationSink` (commit 5 of 1f-i, hash `a148632`) implements 3 retries with backoff, 3-consecutive-failure degradation threshold, severity-based critical detection, and ILogger fallback for critical observations in degraded mode. Full wiring happens in 1f-ii (orchestrators must construct one resilient sink + degradation-state pair per batch). |
| **Status** | in progress (design landed `a148632`; orchestrator wiring pending 1f-ii) |

### P-2 — Observation threading through repositories

| | |
|---|---|
| **Surfaced in** | Sub-phase 1c (planning round). |
| **Resolve in** | Sub-phase 1f-ii (orchestrators emit observations directly via `DomainEventPublisher` and the resilient sink). |
| **Question** | Should infrastructure interfaces (repositories, adapters) take an `IObservationSink` parameter on every call, or is observation emission strictly the orchestrator's job? |
| **Context / leanings** | Three options (1c plan): (1) every method takes a sink, (2) orchestrator emits, (3) repositories receive a sink via constructor. Decision in 1c: option (2) for the canonical case, option (3) for the quarantine path specifically. 1c–1e interfaces and services have stayed sink-free; `DomainEventPublisher` (commit 4 of 1f-i, hash `20b4719`) is the orchestrator-side translation surface. The remaining orchestrator wiring in 1f-ii will validate the design end-to-end. |
| **Status** | in progress (services have stayed sink-free across 1c–1e and 1f-i; `DomainEventPublisher` lands the translation; final validation pending 1f-ii orchestrators) |

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

*(empty)*
