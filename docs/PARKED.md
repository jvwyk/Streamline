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
| **Resolve in** | Sub-phase 1g (use-case handlers / orchestrators). |
| **Question** | When `IObservationSink.RecordAsync` throws inside a per-row hot loop (e.g. `RowValidator` emitting `MISSING_REQUIRED` per row), should the orchestrator abort the row currently being processed, abort the whole batch, or let the in-flight row complete before bubbling? |
| **Context / leanings** | The interface contract (1b) says exceptions propagate; production sinks buffer to avoid hot-path faulting. But buffer overflow / retry-exhausted scenarios still surface as exceptions, and the orchestrator has to decide what counts as recoverable. Lean: aborting the whole batch on exhausted-sink failure is right — losing the ability to observe means losing audit, which is a critical concern. But "abort mid-row" vs "complete row, then abort" matters for partial-write situations. Revisit when the orchestrator code actually has the choice in front of it. |
| **Status** | parked |

### P-2 — Observation threading through repositories

| | |
|---|---|
| **Surfaced in** | Sub-phase 1c (planning round). |
| **Resolve in** | Sub-phase 1g (orchestrators). |
| **Question** | Should infrastructure interfaces (repositories, adapters) take an `IObservationSink` parameter on every call, or is observation emission strictly the orchestrator's job? |
| **Context / leanings** | Three options (1c plan): (1) every method takes a sink, (2) orchestrator emits, (3) repositories receive a sink via constructor. Decision in 1c: option (2) for the canonical case, option (3) for the quarantine path specifically (which has internal context the orchestrator doesn't have). 1c interfaces deliberately have no sink parameter; if 1g shows the balance is wrong, adding parameters to interfaces that haven't been implemented yet is cheap. |
| **Status** | parked |

### P-3 — Bounded retry policy

| | |
|---|---|
| **Surfaced in** | Sub-phase 1d (commit 4, predecessor-bug review). The original "retry loops" bug was misclassified as state-machine; transitions in a retry loop (`RolledBack → Pending → Processing → RolledBack`) are individually legal. The bug is orchestrator-level: the engine should give up after N retries against the same underlying error code. |
| **Resolve in** | Sub-phase 1j (predecessor-bug regression tests at the orchestrator level) or Phase 4 (operational features — `RetryBatchCommand` is a Phase 4 work item per plan §6). |
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
