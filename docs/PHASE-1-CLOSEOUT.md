# Phase 1 close-out

> **Frozen artifact, dated 2026-04-27.** This document captures Streamline's
> state at the close of Phase 1 and is not edited after that point.
> Future phases get their own close-out files (`PHASE-2-CLOSEOUT.md`, etc.).
> Living docs (`HANDOVER.md`, `PARKED.md`, the project plan) carry the
> project's present-tense state.

## What this document is for

A successor agent picking up Phase 2 reads this to know what Phase 1
produced, what's parked and where, and what's ready to inherit. The
narrative is deliberately compact — depth lives in `HANDOVER.md` (working
knowledge) and the authoritative docs (`PARKED.md`, the plan,
`OBSERVATIONS.md`, `TESTING.md`, `WORKFLOW.md`).

## Total scope

| Metric | Value |
|---|---|
| Sub-phases delivered | 11 (0, 1a, 1b, 1c, 1d, 1e, 1f-i, 1f-ii, 1g, 1h, 1i) |
| Commits on `claude/init-streamline-etl-kuSLu` | 83 |
| Total tests | 821 |
| Test breakdown | 144 Core, 401 Domain, 273 Application, 3 architecture |
| Warnings | 0 throughout |
| Predecessor bugs guarded by `[PreventsPredecessorBug]` | 11 (5 SM-* in 1d, 6 PB-* in 1i) |
| Active parked decisions | 9 (P-3 through P-11; P-1 and P-2 resolved in 1f-ii) |

The branch is current at commit `e6a2a77` (1i close-out commit) at the
moment this artifact is written. The full commit history is the
authoritative source for what landed when.

## What Phase 1 built

### Layered solution structure

`Streamline.Core` (value types, enums, observations, results),
`Streamline.Domain` (aggregates, state machines, validators, registries,
abstractions), `Streamline.Application` (orchestrators, handlers,
observability decorators), plus skeleton projects for `Streamline.Infrastructure`,
`Streamline.Files.*`, `Streamline.Registry.*`, and `Streamline.Console`.

### Core layer

Domain enums (`BatchStatus`, `RowStatus`, `ColumnTypeCode`, `DriftPolicy`,
`FkEnforcementMode`, `TransformKind`, `TransformInvocation`). Value types
(`Record`, `SchemaDefinition`, `ColumnDefinition`, `FileMapping`,
`FkReference`, `ValidationError`). The observation model (`Observation`,
`ObservationSeverity`, `ObservationCodes` with 43 codes,
`IObservationSink`). Result types (`IngestionResult`, `ProcessingResult`,
`FileIngestionOutcome`, `TableProcessingOutcome`, `UpsertOutcome`,
`ValidationResult`).

### Domain layer

The `Batch` aggregate as policy gate (not row store).
`BatchStateMachine` and `RowStateMachine` with throw-on-illegal
semantics. Six legal batch transitions, seven legal row transitions —
the matrices are the single source of truth. Pure-function services:
`RowValidator`, `FkResolver`, `ColumnTypeParser`, `RegistryValidator`,
`SchemaDriftDetector`. Eight infrastructure abstractions
(`IStagingRepository`, `IDestinationAdapter` + `ITransactionScope` +
`INestedScope`, `IRegistryRepository`, `IFileMappingRepository`,
`ILineageRepository`, `IObservationRepository`, `ITransformerRegistry`,
`IFileReader` + `IFileDispatcher` + `IFileReaderRegistry`) defined as
contracts only — no production implementations in Phase 1.

### Application layer

`IngestionOrchestrator` and `ProcessingOrchestrator` driving the full
batch lifecycle. Four use-case handlers (`IngestBatchHandler`,
`ProcessBatchHandler`, `RetryBatchHandler`, `InspectBatchHandler`).
`PerBatchScope` for per-batch observability state.
`ResilientObservationSink` decorator (3 retries, 3-failure degradation
threshold, severity-based critical fallback to `ILogger`).
`DomainEventPublisher` for event-to-observation translation.
`SchemaDriftPolicyApplier` for drift policy enforcement.

### Test infrastructure

Nine in-memory fakes (every infrastructure contract has one):
`InMemoryStagingRepository`, `InMemoryDestinationAdapter` (with
matching `ITransactionScope` and `INestedScope`),
`InMemoryRegistryRepository`, `InMemoryFileMappingRepository`,
`InMemoryLineageRepository`, `InMemoryObservationRepository`,
`InMemoryTransformerRegistry`, `InMemoryFileReaderRegistry`. Test fakes:
`ScriptedTransformer`, `FakeFileReader` (with throws/hangs factories).
Test adapters: `ForwardingObservationSink`,
`FailingOnUpsertDestinationAdapter`. Per-class private fixtures
established as the convention.

### Architectural enforcement

NetArchTest-based DDD layering rules. csproj-XML inspection for
project-level boundaries that NetArchTest can't reach (empty
assemblies, project references). Three architecture tests guard the
layering invariants.

### Predecessor-bug guards

Eleven documented bugs covered by `[PreventsPredecessorBug]` regression
tests: SM-1a, SM-1b, SM-2, SM-3, SM-4, SM-5 (state-machine bugs from
1d) plus PB-1, PB-2, PB-4, PB-5, PB-6a, PB-6b, PB-8, PB-9 (cross-
component bugs from 1i — counted as 6 unique IDs). Per-assembly meta-
tests in `RowStateMachineRegressionTests` (Domain) and
`CrossComponentRegressionTests` (Application) enforce that every
regression test in those classes carries the attribute.

### Documentation

`streamline-plan.md` (project plan), `WORKFLOW.md` (standing rhythm +
Appendix A sub-phase report format), `OBSERVATIONS.md` (catalog),
`TESTING.md` (test discipline), `PARKED.md` (deferred decisions
ledger), `AGENTS.md` (agent operating principles), `README.md`. The
four handover docs (`HANDOVER.md`, `ONBOARDING.md`, `STANDARDS.md`,
this file) land at Phase 1's close.

## Parked decisions risk assessment

Categorized by risk and timing. The category determines what a
successor agent should do when planning the relevant downstream phase.
Full context on each item lives in `PARKED.md`; this section is a
risk-shaped summary, not a replacement.

### Resolve before Phase 2 design opens (highest priority)

**P-11 — handler-to-persisted-batch-status bridging.** **The single
highest-risk parked decision in the active list.** Phase 1 handlers
operate on the in-memory aggregate; the persisted `BatchSnapshot` is
read at handler entry but never written back as the aggregate
transitions. The 1h flow tests had to call
`ForTestingOnly_SetBatchStatus` between handlers to drive the
persisted state forward. If Phase 2 ships without bridging, in-memory
aggregate state and persisted `batch_log` status diverge silently,
audit trails break, and recovery scripts get confused. Phase 4
retrofitting requires reverse-engineering "what should the persisted
state have been" for batches that ran during the gap. **Phase 2
design must open with this as a first-tier question, not an
implementation detail.**

**P-5 — TimeProvider injection (lifted from "when needed" to
"Phase 2").** Phase 2 has genuine clock-driven need:
`InMemoryRegistryRepository`'s "active right now" filter uses
`DateTimeOffset.UtcNow`; Phase 2's Postgres mirror needs the same
shape; contract-parity tests need controllable time to exercise
date-boundary scenarios cleanly. Cheap to add at the registry-
repository constructor (defaulting to `TimeProvider.System`); expensive
to retrofit if Phase 2's tests calcify around `UtcNow`. Lifted in 1i's
close-out from "when needed" to first-tier Phase 2 design item.

### Resolve in Phase 4 — operational-recovery cluster

P-3, P-6, P-7, P-8, P-10 form a coherent operational-recovery cluster.
Phase 4's original ~3-week scope (transformers, lineage, built-in
reconciliation) expands to ~5–6 weeks once these absorb. `PARKED.md`
documents this as a single sub-phase scope, not five independent items
on the Phase 4 backlog.

- **P-3 — bounded retry policy.** Per-row retry counter, lives on
  staging or a sidecar table. Defines what "give up" means.
- **P-6 — handler ↔ orchestrator observation flow asymmetry.** Today
  RetryBatchHandler emits `BATCH_RETRIED` outside the orchestrator's
  local capture and merges via post-hoc result reconstruction.
  Refactor to a shared capture surface on `PerBatchScope` if a second
  handler-side emission case arises.
- **P-7 — stuck-in-Ingesting recovery.** `BatchStateMachine` has no
  `Ingesting → Failed` transition. Add a `MarkBatchFailedCommand`
  with `BATCH_FAILED_BY_OPERATOR` audit observation.
- **P-8 — stuck-in-Processing recovery after cancellation.** Rows
  claimed (Pending → Processing) before a cancel/fail remain stuck.
  Add `ResetClaimedAsync` or claim-timeout pattern.
- **P-10 — reconciliation gap closure.** No automatic reconciliation
  in Phase 1; `BuiltinReconciliationRunner` lands here.

### Resolve in Phase 2 or Phase 4 (judgment call)

**P-9 — file-hash dedup (PB-3).** Default-targeted at Phase 2 because
dedup needs the staging schema to support a `content_hash` column. If
Phase 2 is over-scoped, defer to Phase 4's operational cluster.
Decision deferred to Phase 2 planning.

### Resolve in Phase 6 (real-data discovery)

**P-4 — decimal precision/scale on `ColumnDefinition`.** Can't be
designed ahead of real source data. Phase 6 surfaces the need; if a
Phase 3 reader hits a precision issue earlier, pull forward.
Acceptable risk because the cost of late discovery is one migration's
rerun, not architectural rework.

## What's ready for Phase 2

Phase 2 is Postgres infrastructure: real implementations of the eight
contracts that Phase 1's fakes back. Specifically, Phase 2 builds:

1. `PostgresStagingRepository`, `PostgresDestinationAdapter` (with
   real `ITransactionScope` and `INestedScope`),
   `PostgresRegistryRepository` and the YAML/Hybrid variants,
   `PostgresFileMappingRepository`, `PostgresLineageRepository`,
   `PostgresObservationRepository`, plus `IFileReaderRegistry`
   registration via DI.
2. The schema migrations: `staging.batch_log`, `staging.file_log`,
   `staging.incoming`, `staging.quarantine`, `staging.row_lineage`,
   `staging.transform_log`, `staging.reconciliation_log`, plus the
   registry tables.
3. **Contract-parity tests.** Every contract test in 1g/1h's fake suite
   should run against both the in-memory fake and Postgres. The fakes
   were designed for this — same observable end-state for the same
   operations. Phase 2's first deliverable is a test runner that proves
   the parity.

The two first-tier design questions Phase 2 opens with:

- **P-11**: how does the orchestrator (or handler) keep persisted
  batch-status in lockstep with the aggregate's view? Lean from
  Phase 1: the orchestrator emits a status-update call per aggregate
  lifecycle method (`Start`, `MarkIngested`, `BeginProcessing`,
  `Complete`, `Fail`).
- **P-5**: where does `TimeProvider` enter the design? Lean from
  Phase 1: at `IRegistryRepository`'s constructor, propagated to both
  fakes and Postgres adapters; do not reintroduce on
  `ResilientObservationSink` itself (its lockup history makes
  `TimeProvider` there expensive — see `HANDOVER.md` debug gotchas).

The fakes from 1g and the integration patterns from 1h are the
verification surface. If Phase 2 ships and the existing 1h tests run
unchanged against Postgres-backed fakes, Phase 2 is correct.

## Surprises (carried forward)

Five Phase 1 surprises worth a successor knowing, drawn from the
sub-phase reports. Full debug context for each lives in
`HANDOVER.md` §8.

1. **`FakeTimeProvider` deadlock on `Task.Delay(TimeSpan,
   FakeTimeProvider, CT)`** (1f-i). `Task.Delay` plus xUnit v3's async
   dispatch plus NSubstitute's `.Returns(callback)` hung `dotnet test`
   indefinitely. Resolution: explicit-backoff-list shape on
   `ResilientObservationSink` instead of `TimeProvider`. Documented as
   P-5; Phase 2 introduces `TimeProvider` at the registry layer, where
   the deadlock pattern doesn't apply.
2. **Bulk FK preload was wrong for in-batch parent-child** (1h commit
   6). `ProcessingOrchestrator` originally preloaded all FKs at batch
   start, before any table was processed. In-batch parent-child
   references saw an empty parent. Fix: lazy per-entry preload
   (`fd59469`). The bug surfaced because 1h's multi-table test was
   contract-not-artifact (asserted "the broker_address row commits"
   rather than "two rows committed").
3. **`InMemoryFileReaderRegistry` was missing from 1g** (1h commit 1).
   Surfaced when 1h's integration tests needed to wire the orchestrator.
   Filled as part of the plumbing commit. Honest gap.
4. **`RetryBatchHandler` observation merging** (1f-ii commit 10).
   `BATCH_RETRIED` emitted before the orchestrator runs reaches the
   inner sink but never lands in the orchestrator's local capture
   list. Fixed by reconstructing `ProcessingResult` with the retry
   observation prepended. Surfaced as P-6 because the asymmetry is
   real and a second handler-side emission case would warrant
   refactoring.
5. **The fakes don't auto-advance batch status across handlers** (1h).
   The `IngestThenProcess` flow tests have to call
   `ForTestingOnly_SetBatchStatus` between handlers because
   `IngestBatchHandler` operates only on the in-memory aggregate, not
   the persisted snapshot. P-11 captures this; Phase 2 must close the
   gap as a first-tier design point.

## Closing thought

Phase 1 was a four-month-equivalent effort compressed into a focused
build-out: domain types, state machines, validators, registries,
observability infrastructure, in-memory fakes, integration tests, and
predecessor-bug regression guards. Every test in the 821-test suite
passes; zero warnings; nine parked decisions documented with explicit
resolution targets and clear ownership.

What Phase 1 deliberately didn't build: any production-side
persistence, any real file readers (XLSX, JSON, etc.), any operator
commands, any transformer invocation, any reconciliation. Those are
Phase 2 through Phase 4 / 6. Phase 1's job was to make Phase 2 a
Postgres-implementation exercise, not a design exercise. Per the
parked-decisions risk assessment above, two design exercises remain
(P-5, P-11) — both surfaced explicitly so Phase 2 doesn't ship with
silent assumptions.

The handover docs (`HANDOVER.md`, `ONBOARDING.md`, `STANDARDS.md`,
plus this file) are the substrate that lets a successor open Phase 2
at Phase 1's standard. If a new agent reads them and produces a
Phase 2 plan-round response that matches the rhythm and rigor of
Phase 1, the handover is real.
