# TESTING.md — Streamline Testing Requirements

**Testing on Streamline is strict. These requirements are enforced by
CI, by code review, and by team culture. "I'll add tests later" is
not permitted.** Every production code change ships with tests in the
same PR. Every bug produces a regression test before the fix merges.
Every architecture rule has a test that fails when violated.

This is non-negotiable. The engine handles money-movement-adjacent ETL
work. Skipped tests cost real money and real trust.

---

## Philosophy

Tests on Streamline exist for four reasons, in order of importance:

1. **Prevent regressions of bugs we've seen before.** Every bug
   discovered (whether in Streamline, its predecessor, or during
   migration) becomes a test. If the refactor plan listed 14 flagged
   behaviors, Streamline's test suite has 14 tests that prevent their
   equivalents.

2. **Lock in the state machine.** The row and batch state models are
   the heart of the engine. Every transition (legal and illegal) is
   tested. New transitions require new tests before the transition is
   implemented.

3. **Verify contracts at layer boundaries.** Whenever a layer's
   interface is implemented — in-memory fake, Postgres, YAML, etc. —
   the same test suite runs against every implementation. If one
   implementation passes and another fails, the contract is not real.

4. **Verify observations are emitted correctly.** For every ETL
   condition (validation failure, drift, FK miss, transformer
   outcome, reconciliation mismatch), tests assert that the correct
   observation with the correct severity and code is recorded. See
   `OBSERVATIONS.md` for the full list of codes; every code has at
   least one test that verifies it fires when expected.

Tests exist to make the engine trustworthy to operate. They do not
exist to chase coverage numbers; coverage is a byproduct, not a goal.

---

## Test Types

Six categories, each with a specific purpose and location.

### 1. Unit tests (`Streamline.Core.Tests`, `Streamline.Domain.Tests`)

**Purpose:** verify pure logic in isolation. No I/O. No dependencies on
databases, files, or time.

**Covers:**
- `Streamline.Core` primitives and enums.
- `Streamline.Domain` aggregates, value types, domain services.
- The state machine: every legal transition, every illegal transition
  throws, reverse transitions (retry) work.
- Validation rules.
- `RegistryEntry` / `FileMapping` self-consistency checks.

**Speed target:** full suite runs in under 10 seconds.

**Conventions:**
- One test class per production class.
- Test names: `MethodName_Scenario_ExpectedBehavior`.
  Example: `TransitionRow_FromCommittedToPending_Throws`.
- Use `FluentAssertions`. `x.Should().Be(y)` not `Assert.Equal(y, x)`.
- `NSubstitute` for mocks, sparingly. Prefer real collaborators or
  in-memory fakes.

### 2. Use-case tests (`Streamline.Application.Tests`)

**Purpose:** verify that application handlers coordinate domain and
infrastructure correctly. Uses in-memory fakes for every infrastructure
concern.

**Covers:**
- End-to-end flow of `IngestBatch`, `ProcessBatch`, `RetryBatch`,
  `InspectBatch`.
- Both replication and transform paths.
- Happy path AND every failure path (partial validation failure,
  transform throws, destination write fails, concurrent process
  attempts).
- The bugs from the refactor plan's flagged-behaviors list have tests
  here.

**Speed target:** full suite runs in under 30 seconds.

**In-memory fakes live here**, not in `Infrastructure`. Fakes are test
doubles, not production code.

**Required fakes** (implement in
`Streamline.Application.Tests/Fakes/`):
- `InMemoryStagingRepository`
- `InMemoryDestinationAdapter`
- `InMemoryRegistryRepository`
- `InMemoryFileMappingRepository`
- `InMemoryLineageRepository`
- `InMemoryTransformerRegistry`
- `ScriptedTransformer` (deterministic outcomes for transform tests)
- `FakeFileReader` (yields a scripted record sequence)

### 3. Infrastructure integration tests (`Streamline.Infrastructure.Tests`, `Streamline.Registry.Postgres.Tests`)

**Purpose:** verify that concrete implementations match the contract.

**Covers:**
- Postgres repositories behave identically to in-memory fakes for the
  same inputs.
- Transaction boundaries, savepoints, deferred FKs work as expected.
- Advisory locks prevent concurrent access.
- `SELECT ... FOR UPDATE SKIP LOCKED` actually skips locked rows.
- Migrations run cleanly against an empty database AND against a
  populated database (upgrade path).

**Uses Testcontainers.** Each test class spins up a Postgres container
in `OneTimeSetUp`, tears down in `OneTimeTearDown`. Container startup
is ~5-10 seconds; tests within a class run fast (database state is
reset between tests via transaction rollback).

**Category attribute:** `[Category("Integration")]` so CI and local
runs can filter.

**Speed target:** full suite runs in under 3 minutes.

**Contract parity test.** The most valuable test in this category is
the one that runs the *same* test sequence against the in-memory fake
AND the Postgres implementation, asserting both produce identical
results. If they diverge, the abstraction is lying.

### 4. End-to-end integration tests (`Streamline.Integration.Tests`)

**Purpose:** verify the whole pipeline from file arrival to destination
write, across real Postgres, with real file readers.

**Covers:**
- Ingest a sample file → process → verify destination rows.
- Retry a batch after a failure → verify rows recover correctly.
- A file with partial validation failures → valid rows commit,
  invalid rows quarantine, counts reconcile.
- Schema drift blocks a file per the drift policy.
- Transform paths end-to-end with a test SQL function.
- Container handling: ingest a zip with mixed entry types.

**Uses Testcontainers** for Postgres. **Uses real file fixtures**
from `tests/fixtures/`.

**Speed target:** full suite runs in under 10 minutes. Individual tests
should not exceed 30 seconds.

### 5. Architecture tests (`Streamline.Architecture.Tests`)

**Purpose:** enforce layering rules at build time.

**Covers:**
- `Streamline.Core` depends on nothing.
- `Streamline.Domain` depends only on `Streamline.Core`.
- `Streamline.Application` depends only on `Domain` and `Core`.
- No direct references to `Npgsql`, `CsvHelper`, `ClosedXML`,
  `System.Text.Json` outside their designated wrapping projects.
- Every interface in `Streamline.Domain.Abstractions` has at least
  one implementation in the expected package.
- No public types in `Streamline.Core` depend on logging, DI, or
  persistence abstractions.

**Speed target:** full suite runs in under 5 seconds.

**Uses `NetArchTest.Rules`** or equivalent. Runs on every CI build.
Architecture violations fail CI — they are not lintable warnings, they
are build breakers.

### 6. Performance tests (`Streamline.Performance.Tests`)

**Purpose:** prevent performance regressions in the hot path.

**Covers:**
- 100K-row ingestion completes within the target duration.
- Memory footprint stays bounded as file size grows.
- Upsert throughput doesn't regress between releases.

**Runs nightly**, not per-PR. A regression fails the nightly build
and produces an issue.

**Uses `BenchmarkDotNet`**. Not a test framework — a measurement tool.
Don't assert exact timings; assert "within X% of baseline."

---

## Coverage Expectations

We don't enforce a coverage number. We enforce coverage *categories*:

| Category | Expectation |
|---|---|
| State machine | 100% transitions covered, legal + illegal |
| Domain services | Every method has at least one positive and one negative test |
| Application handlers | Happy path + every named failure mode tested |
| Infrastructure contracts | Same tests as the in-memory fake, running against concrete implementation |
| Architecture rules | Every rule has a test that fails when violated |
| Observations | Every observation code has at least one test asserting it fires when expected, with correct severity and context |
| Known bugs | Every bug discovered anywhere has a regression test |

Coverage tools (`coverlet.collector`) run in CI and produce a report,
but the number is informational, not a gate.

**What must have tests:**
- Every public method on an aggregate.
- Every branch in a domain service.
- Every interface implementation.
- Every bug (see "Regression discipline" below).
- Every work item labelled "Acceptance criteria" in the plan.

**What doesn't need its own tests:**
- Trivial getters/setters (covered implicitly).
- Generated code (EF migrations, record types' `Equals`, etc.).
- The Console host's DI wiring (integration tests cover this).
- Log messages (asserting exact strings is brittle).

---

## Regression Discipline

**Every bug that ships to any environment produces a test before it
ships to the next.**

When a bug is found:

1. Reproduce it with a failing test, in the appropriate layer.
2. Fix the bug. The test passes.
3. Commit the test and the fix together.
4. The commit message names the bug and references the issue/PR
   where it was discovered.

**Bugs discovered during migration (Phase 5-6) follow the same rule.**
If a bespoke job reveals a Streamline bug, the bug gets a test in
Streamline's suite before the migration continues. The test lives
forever — even after the bespoke job is retired.

---

## Test Fixtures

**Location:** `tests/fixtures/`

**Subfolders:**
- `files/delimited/` — pathological CSV and pipe-delimited files.
- `files/xlsx/` — edge-case Excel files (multi-sheet, hidden sheets,
  merged cells, formula errors, etc.).
- `files/json/` — JSON variants (array, NDJSON, nested, inconsistent
  shapes).
- `files/zip/` — zip pathologies (empty, single-entry, nested,
  corrupted, mixed-schema).
- `registry/` — sample YAML registry entries for use in tests.

**Each fixture file has a README** in its folder describing what makes
it pathological and which test uses it. Fixtures without a README are
orphans — delete them.

**Fixture collection happens during Phase 0** as a non-code
prerequisite. Real samples from bespoke jobs go into `files/*/real/`
subfolders; synthesized edge cases go into `files/*/synthetic/`.

**Never commit PII or production credentials.** If a real sample file
contains sensitive data, redact it before committing or derive a
synthetic equivalent.

---

## Running Tests

### Locally

```bash
# Everything
dotnet test

# Fast suite only (unit + use-case + architecture)
dotnet test --filter Category!=Integration

# Integration tests only (requires Docker)
dotnet test --filter Category=Integration

# Single project
dotnet test tests/Streamline.Domain.Tests

# Single test
dotnet test --filter FullyQualifiedName~RowStateMachineTests.TransitionRow_FromCommittedToPending_Throws
```

### In CI

Every PR runs:
- Unit + use-case + architecture tests on every supported .NET version.
- Integration tests on the primary .NET version only.
- Performance tests are nightly, not per-PR.

A failing test fails the PR. There is no "flaky test" exception;
flaky tests are fixed or removed, not retried. See the "Flakiness"
section below.

---

## Flakiness

**Flaky tests are bugs.** If a test passes sometimes and fails other
times, it's not testing what you think it is.

Common causes:
- Timing assumptions (`Thread.Sleep`, expecting exact timestamps).
- Shared state between tests (database not reset, static fields).
- Ordering assumptions (tests that pass only in a specific order).
- External dependencies (network calls, non-deterministic data).

**Procedure when a test flakes:**
1. Mark it `[Trait("Flaky", "true")]` so it doesn't block CI.
2. File an issue describing the flake, with logs.
3. Fix it within one sprint, or delete the test.
4. **Never add a retry loop to a flaky test.** Retries mask bugs.

---

## Test Data Conventions

- **Fixed seeds for randomness.** Any use of `Random` in a test must
  pass an explicit seed. No `new Random()` without a constant.
- **Fixed timestamps.** Tests that care about time use a
  `FakeTimeProvider` or `TimeProvider.System` with controlled values.
  Never `DateTime.UtcNow` in an assertion.
- **Stable ordering.** Tests that iterate collections must either sort
  first or assert on set equality, not list equality. Dictionary
  ordering is not guaranteed.
- **No hidden I/O.** A unit test that writes to disk, makes a network
  call, or touches the real clock is not a unit test. Promote it to
  integration or refactor.

---

## What Good Tests Look Like

A good test:
- Has a name that reads like a specification.
- Arranges exactly what it needs, no more.
- Makes exactly one assertion (or a cohesive set — "the result is this
  shape" is one assertion even if it checks multiple fields).
- Fails with a clear message that tells you what was wrong without
  looking at the test code.
- Is independent of every other test.

Example of a good test name:
```csharp
[Fact]
public async Task ProcessBatch_WhenTableHasMixedValidAndInvalidRows_CommitsValidAndQuarantinesInvalid()
```

Example of a poor test name:
```csharp
[Fact]
public async Task TestProcessBatch()  // What does this test? No idea.
```

---

## Definition of "Tested" for a Change

When you make a change, you've tested it if:

- [ ] A test exists that fails against the old code.
- [ ] That test passes against the new code.
- [ ] The test is in the appropriate layer (unit, use-case,
      infrastructure, or E2E).
- [ ] If the change crosses a layer boundary, tests exist at each
      layer involved.
- [ ] If the change is infrastructure-specific, the equivalent
      in-memory test also runs against the infrastructure
      implementation (contract parity).
- [ ] Edge cases and failure modes have their own tests, not just
      the happy path.

If any checkbox is unchecked, the change is not ready to merge.

---

## When Testing Is Genuinely Hard

Some things are hard to test. When you hit one:

- **DO NOT** skip the test and add a comment saying "hard to test."
- **DO** stop and ask. Hard-to-test code is usually poorly designed
  code, and a design conversation is cheaper than a bug later.

Common hard-to-test patterns and their usual fixes:
- Static dependencies → inject an interface.
- Time-dependent logic → inject `TimeProvider`.
- File-system access → inject `IFileReader` or equivalent, wrap with
  an in-memory implementation.
- Random data → inject a seeded `Random` or a deterministic generator.

If a class is hard to test because it's doing too much, split it. The
split will also make it easier to understand.
