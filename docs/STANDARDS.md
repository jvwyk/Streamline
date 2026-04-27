# STANDARDS.md — the rules, indexed

> Flat index of every load-bearing standard. Each entry: rule (one
> sentence), rationale (one sentence), authoritative source. Scannable in
> 5 minutes. If you want depth, follow the pointer.

## Index

| § | Category |
|---|---|
| 1 | [Code organization](#1-code-organization) |
| 2 | [Testing](#2-testing) |
| 3 | [Commits and PRs](#3-commits-and-prs) |
| 4 | [Architecture enforcement](#4-architecture-enforcement) |
| 5 | [Documentation](#5-documentation) |
| 6 | [Observability](#6-observability) |
| 7 | [Process](#7-process) |
| 8 | [Tooling](#8-tooling) |

---

## 1. Code organization

**Layering.** Dependencies flow Core → Domain → Application →
Infrastructure → Console; reverse references are illegal. Why: the
deliberate inverse of the predecessor system's cross-layer coupling.
**Source:** `streamline-plan.md` §5; `tests/Streamline.Architecture.Tests/`.

**Project structure.** One project per layer; test projects mirror
production projects (`Streamline.Domain.Tests` for `Streamline.Domain`).
Why: per-project boundaries are enforced by csproj reference rules and
by NetArchTest. **Source:** `streamline-plan.md` §5.2; the solution
file `Streamline.slnx`.

**File placement.** Production code lives in
`src/<ProjectName>/<Subfolder>/`; tests live in
`tests/<ProjectName>.Tests/<Subfolder>/`, mirroring the production
folder structure. Why: a test for `src/Streamline.Domain/Validation/RowValidator.cs`
is at `tests/Streamline.Domain.Tests/Validation/RowValidatorTests.cs`,
not somewhere else. **Source:** the existing folder structure;
`HANDOVER.md` §1 for the per-class fixture pattern.

**Test fakes location.** Phase 1 in-memory fakes live in
`tests/Streamline.Application.Tests/Fakes/`, public (not internal).
Why: 1h's integration tests across handler boundaries need them; per
Q17 of 1g, public is simpler than `internal` + IVT. **Source:**
`HANDOVER.md` §1; `PHASE-1-CLOSEOUT.md` "Test infrastructure."

---

## 2. Testing

**Tests required for every production change.** No exceptions. The
test asserts at least one contract behavior; artifact-only assertions
silently rot when the implementation changes. Why: integration tests
that pass for the wrong reason are the silent-failure mode `HANDOVER.md`
§3 warns about. **Source:** `TESTING.md`; `HANDOVER.md` §1
("Contract-not-artifact assertion bar").

**Test categories.** Unit (one production type), integration
(handler + orchestrator + real fakes), regression
(`[PreventsPredecessorBug]`-tagged), architecture (NetArchTest +
csproj-XML). Each lives in its own subfolder under the relevant test
project. **Source:** `TESTING.md`; existing test folders.

**Per-class private fixtures.** Each test class builds its own
fixtures inline or via a private nested `Fixture` class; shared
fixtures get extracted only when 3+ classes want the same shape.
Why: shared fixtures calcify too early; per-class is the right
default. **Source:** Q1 of the 1h plan, codified across
`tests/Streamline.Application.Tests/Integration/`.

**Contract-parity bar.** Phase 1's in-memory fakes must mirror Phase
2's Postgres adapters in observable end-state for the same inputs.
Why: the fakes are the safety net that lets 1h tests run unchanged
against Postgres in Phase 2. **Source:** Q2 of the 1g plan;
`HANDOVER.md` §4.

**Perturb-recover for load-bearing tests.** Three or four times per
sub-phase, deliberately break production code, run the test, verify
it fails with a useful assertion message, restore. Why: catches
"test passes but design is wrong" failures. **Source:** `HANDOVER.md`
§1; the 1h close-out's perturb-recover documentation.

**`ForTestingOnly_` pattern.** Test-only methods/properties on fakes
are prefixed `ForTestingOnly_` so they're unmistakably test surface,
not production API. Why: 1h tests sometimes need audit-trail detail
the production query API doesn't expose; the prefix prevents
accidental production use. **Source:** Q7 of the 1g plan;
`tests/Streamline.Application.Tests/Fakes/InMemoryStagingRepository.cs`.

**Predecessor-bug regression tests.** Tests guarding against
documented predecessor bugs carry the `[PreventsPredecessorBug(id,
description)]` attribute; per-assembly meta-tests assert every
regression test in the dedicated regression classes carries it.
Why: documents the bug context inline, prevents attribute-less
additions. **Source:** `HANDOVER.md` §3 ("Predecessor-bug guards");
`tests/Streamline.Domain.Tests/Batches/StateMachine/RowStateMachineRegressionTests.cs`
and `tests/Streamline.Application.Tests/Regression/CrossComponentRegressionTests.cs`.

---

## 3. Commits and PRs

**Conventional commit format.** `feat(scope): subject` /
`fix(scope): subject` / `docs(scope): subject` / `test(scope):
subject` / `chore(scope): subject`. Why: scannable git log, scoped
to the affected area. **Source:** existing commit history;
`WORKFLOW.md`.

**One logical change per commit.** A commit covers one purpose: a
new feature, a bug fix, a refactor, or a docs update — never a mix.
Why: individually-revertable history; clean diffs for review.
**Source:** `WORKFLOW.md`.

**Design fixes don't hide inside test commits.** When a test surfaces
a production-code bug, the fix lands as a separate `feat(application):
adjust X for Y` commit before the test commit. Why: mixed-concern
commits are a workflow violation; the design change must be visible
in the log. **Source:** `WORKFLOW.md`; the 1h `fd59469` commit
(lazy-FK-preload fix landed ahead of the test commit that surfaced
it).

**Sub-phase commit numbering.** Commits within a sub-phase carry
"Sub-phase X commit N of M" in the message body. Why: the close-out
report references commits by number; the numbering makes it
unambiguous. **Source:** every sub-phase commit message in Phase 1.

---

## 4. Architecture enforcement

**NetArchTest layering rules.** The `Streamline.Architecture.Tests`
project asserts that types in higher layers don't depend on types in
lower layers. Why: catches drift at test-run time, not at "we
noticed a year later" time. **Source:**
`tests/Streamline.Architecture.Tests/`.

**csproj-XML inspection for empty assemblies.** NetArchTest can't
enforce rules on empty/skeletal projects (no types loaded). The
architecture-test project parses csproj XML directly to validate
project references for those cases. Why: the layering invariants
must hold even before a project accumulates types. **Source:**
`tests/Streamline.Architecture.Tests/`; `HANDOVER.md` §8.

**Adding a new architecture rule.** New rules go in
`Streamline.Architecture.Tests` with a clear test name and a
docstring explaining what's being enforced. Why: architecture rules
are themselves a contract; they need their own discipline.
**Source:** existing tests in
`tests/Streamline.Architecture.Tests/`.

---

## 5. Documentation

**`PARKED.md` updates.** When a decision can't be made now, add an
entry with stable ID (`P-N`), `Surfaced in`, `Resolve in`,
`Question`, `Context / leanings`, `Status`. Why: the ledger is the
project's memory of deferred calls; without it, parked items get
silently forgotten. **Source:** `PARKED.md` itself; `HANDOVER.md` §1.

**Project plan updates.** When a sub-phase resolves a question that
was open in the plan, the relevant plan section gets updated;
historical decisions in the plan are preserved (don't rewrite
history). Why: the plan is the present-tense reference for what the
project is. **Source:** `streamline-plan.md`; `WORKFLOW.md`.

**New documentation.** A new doc is warranted when a topic doesn't
fit the existing docs cleanly. Most "I want a new doc" wants are
better served by adding a section to an existing one. Why: doc
sprawl is a real cost; each doc is a context-load. **Source:**
`HANDOVER.md` §1.

**Cross-reference, don't duplicate.** When one doc references
another's content, link to the section; don't restate. Why:
duplication produces drift; the authoritative source becomes
ambiguous. **Source:** the four handover docs; `STANDARDS.md` (this
file) is itself a flat index that points, never duplicates.

**Phase close-out artifacts are frozen.** `PHASE-N-CLOSEOUT.md` files
capture the state at a phase's close and are not edited after that
point. Why: the close-out is dated history, not a living doc; living
state lives in `HANDOVER.md` and the authoritative docs. **Source:**
`PHASE-1-CLOSEOUT.md`'s preamble.

---

## 6. Observability

**Observation severity assignment.** Info = "this happened"; Warning
= "this is unusual but the operation continues"; Error = "this row /
file failed but the batch continues"; Critical = "this is something
the operator must see." Why: severities map to operational urgency,
not to log levels. **Source:** `OBSERVATIONS.md`.

**Emission discipline.** Orchestrators emit observations; services
(repositories, validators, drift detectors, transformers) do not
take an `IObservationSink`. Why: per P-2's resolution, services stay
sink-free; the orchestrator is the translation surface. **Source:**
`PARKED.md` (P-2 resolved); `HANDOVER.md` §4.

**The catalog as wire-protocol.** Observation codes
(`UPPER_SNAKE_CASE`, `nameof()`-locked) are stable wire-protocol
values; renaming or repurposing is a breaking change. Why:
operators query, count, and alert on these codes; stability matters.
**Source:** `OBSERVATIONS.md`;
`src/Streamline.Core/Observations/ObservationCodes.cs`.

**The publisher pattern.** `DomainEventPublisher` is the single
event-to-observation translation surface; aggregates emit
`IDomainEvent`s that the publisher converts to `Observation`s with
the correct code, severity, and context. Why: one place for the
mapping table; no scattered translations across handlers. **Source:**
`src/Streamline.Application/Services/DomainEventPublisher.cs`.

---

## 7. Process

**Plan-confirm-implement-test-commit cadence.** Every sub-phase
follows this rhythm; skipping steps produces work that has to be
rewritten. Why: the cadence is what produced Phase 1's quality.
**Source:** `WORKFLOW.md`; `HANDOVER.md` §1.

**Parked decisions get explicit phase ownership.** `Resolve in:
<phase>` is required; "later" or "TBD" is not acceptable. When 5+
items target the same phase, frame as a cluster. Why: vague
ownership produces parked items that linger forever. **Source:**
`PARKED.md`; `HANDOVER.md` §1.

**Context-awareness as default mode.** Notice and surface adjacent
issues; don't silently absorb them. Why: this discipline is what
caught the FluentAssertions license, the bulk-FK-preload bug, the
InMemoryFileReaderRegistry gap — multiple Phase 1 saves.
**Source:** `HANDOVER.md` §1, §6.

**Standing report format.** Sub-phase reports use the eight-section
format from `WORKFLOW.md` Appendix A. Empty sections are explicit
("Surprises: none"), not omitted. Why: scan-fast structure; the
user can find what they need in 30 seconds. **Source:** `WORKFLOW.md`
Appendix A.

**Stop at sub-phase boundaries.** Don't run into the next sub-phase;
the user owns when the next prompt opens. Mid-sub-phase pauses are
surfaced explicitly, not unilateral. Why: tired sessions produce
worse work; clean handoffs preserve context. **Source:** `HANDOVER.md`
§1, §2.

**The 30-minute tooling-fight rule.** If a tooling issue costs more
than 30 minutes of debug time, fall back to a workaround and surface
as a parked decision. Why: prevents sessions from being eaten by
tool fights. **Source:** `HANDOVER.md` §1; the P-5 deferral case.

---

## 8. Tooling

**Analyzer baseline.** `.NET 10` defaults plus the project's
`Directory.Build.props` settings; no project-wide suppressions.
Why: the analyzers' rules are mostly correct; suppressing globally
hides real issues. **Source:** `Directory.Build.props`;
`HANDOVER.md` §5.

**Suppression at smallest scope.** When an analyzer rule is wrong
for a specific case, suppress with a class-level
`[SuppressMessage]` and a multi-line `Justification` explaining
why. Never suppress globally. Why: keeps the analyzer's signal
strong; documents the exception. **Source:** examples on
`ObservationCodes` (CA1707), `ColumnTypeCode` (CA1720),
`ResilientObservationSink` and `InMemoryDestinationAdapter`
(CA1848); `HANDOVER.md` §8.

**Central Package Management.** All package versions live in
`Directory.Packages.props`; project files reference packages by
name without a version. Why: consistent versions across projects;
single place to update. **Source:** `Directory.Packages.props`;
the project files referencing packages without `Version`
attributes.

**Test-runner risk patterns.** `IAsyncEnumerable` mocking is
fragile; cancellation timing across async is fragile; ordering
assertions across async are fragile. The 1g/1h tests use specific
patterns to avoid each: hand-rolled async iterators, pre-cancelled
tokens (never delay-based cancellation), capture-into-list +
assert-on-recorded-sequence. Why: deflakes the suite; makes
failures meaningful. **Source:** `HANDOVER.md` §1; pattern examples
across `tests/Streamline.Application.Tests/Fakes/`.

**`[EnumeratorCancellation]` only on async-iterator methods.**
Wrapper methods that delegate to an async iterator and are not
themselves one trip CS8424. Inline the iteration, or write the
helper directly as `async IAsyncEnumerable<T>`. Why: compiler
constraint, not a style preference. **Source:** `HANDOVER.md` §8.
