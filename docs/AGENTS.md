# AGENTS.md — Streamline Development Agent Guide

**Read this at the start of every session before writing any code.**

This file tells you what Streamline is, where to find the authoritative
design, how the team wants work done, and what mistakes to avoid. It's
the only file you're required to read on every session. Everything else
is referenced from here.

---

## What Streamline Is

Streamline is a generic, configurable ETL engine. One codebase driven by
registry config, replacing tens of bespoke ETL jobs. Consumers point it
at source files and a database; the engine handles ingestion, staging,
validation, transformation (optional), destination writes, lineage, and
reconciliation.

It is NOT a transformation language, a data quality platform, a
scheduler, or a GUI. It's a library that runs via a thin console host.

See `streamline-plan.md` for the full design. This AGENTS file does not
duplicate the plan — it points at it.

---

## Required Reading Before Coding

Before touching any code on a new task, you must have read:

1. **`streamline-plan.md` — the full plan.** Specifically:
   - Section 3 (Scope) so you know what's in and out.
   - Section 5 (Architecture) — DDD layers, project structure, domain
     model.
   - Section 6 (Phases) — the current phase's work items and
     deliverables.
   - Section 11 (Decisions) — the non-negotiables.

2. **`TESTING.md` — testing requirements.** What tests are expected,
   how they're organized, what coverage means for this project.

3. **`WORKFLOW.md` — development rhythm.** How to pick up a task, when
   to stop and ask, how to structure commits, what "done" means.

4. **`PARKED.md` — deferred decisions.** Calls that surfaced in earlier
   sub-phases but weren't yet ready to make. Review at the start of
   every sub-phase whose name appears in **Resolve in** for any active
   entry. New deferrals get a new entry; resolutions get linked back
   to the resolving commit.

If the task touches a specific concern (transforms, file readers,
reconciliation, etc.), also skim the relevant subsection of the plan's
Section 5.3 (Domain Model).

---

## Project Structure (quick reference)

Full structure is in the plan's Section 5.2. Summary:

```
/src
  /Streamline.Core                 — no dependencies
  /Streamline.Domain               — depends only on Core
  /Streamline.Application          — use cases, orchestration
  /Streamline.Infrastructure       — Postgres staging/destination/lineage
  /Streamline.Registry.Postgres    — Postgres-backed registry
  /Streamline.Registry.Yaml        — YAML-backed registry
  /Streamline.Registry.Hybrid      — YAML source + Postgres cache
  /Streamline.Files.Delimited      — CsvHelper-based reader
  /Streamline.Files.Xlsx           — ClosedXML-based reader
  /Streamline.Files.Json           — System.Text.Json-based reader
  /Streamline.Files.Zip            — zip container dispatcher
  /Streamline.Console              — the reference host
/tests
  /Streamline.Core.Tests
  /Streamline.Domain.Tests
  /Streamline.Application.Tests        — uses in-memory infrastructure fakes
  /Streamline.Infrastructure.Tests     — Testcontainers-backed
  /Streamline.Registry.Postgres.Tests  — Testcontainers-backed
  /Streamline.Integration.Tests        — end-to-end via the Console host
  /Streamline.Architecture.Tests       — NetArchTest + csproj inspection
```

The plan's §5.2 is the source of truth for which test projects exist.
Per-format test projects (`Streamline.Files.*.Tests`) and the
non-Postgres registry test projects (`Streamline.Registry.Yaml.Tests`,
`Streamline.Registry.Hybrid.Tests`) are created in the phase that
first needs them, not up front. `Streamline.Performance.Tests` lands
when nightly performance gating begins.

**Dependency direction is strict.** Core has no dependencies. Domain
depends only on Core. Application depends on Domain + Core. Infrastructure
and the format-specific packages depend on Application + Domain + Core.
Console depends on all of them.

If you find yourself adding a reference that violates this direction,
stop. The architecture test will fail the build. Revisit the design; the
thing you want to do probably belongs in a different layer.

---

## Local Development

### Requirements

- **.NET 10 SDK** — install from
  <https://dotnet.microsoft.com/download/dotnet/10.0> (Microsoft) or
  via the platform package manager (e.g., `apt install dotnet-sdk-10.0`
  on Debian/Ubuntu). The repo is known to build against SDK
  **10.0.107** (or any later 10.0.x). The CI workflow pins
  `dotnet-version: '10.0.x'`, so any patch revision in the 10.0
  channel is fine.
- Docker (for Testcontainers integration tests, Phase 2 onward).
- A local Postgres is not required — tests spin their own up via
  Testcontainers; the Console reads connection strings from config.

### First time

```bash
git clone <repo>
cd streamline
dotnet restore
dotnet build
```

### Run tests

```bash
# All tests
dotnet test

# Just unit tests (no Docker needed)
dotnet test --filter Category!=Integration

# Integration tests (needs Docker for Testcontainers)
dotnet test --filter Category=Integration
```

Docker must be running for integration tests. Testcontainers spins up a
Postgres instance per test class; this takes ~10 seconds per class but
gives real database behavior.

### Run the console

```bash
dotnet run --project src/Streamline.Console -- --help
```

Typical commands during development:

```bash
dotnet run --project src/Streamline.Console -- ingest ./samples
dotnet run --project src/Streamline.Console -- process <batch-id>
dotnet run --project src/Streamline.Console -- inspect <batch-id>
```

---

## How We Work

See `WORKFLOW.md` for the full rhythm. Highlights:

- **Plan before code.** Before writing, state what you're going to do
  and which files you'll touch. Wait for confirmation if anything is
  non-obvious.
- **One concern per commit.** Commits are small, focused, and
  individually revertable.
- **Commit messages are authored by the human.** No "Co-authored-by"
  lines naming an AI tool. No "Generated with [tool]" footers. No
  attribution comments. The human who ran the agent is the sole
  commit author.
- **Testing is mandatory, not aspirational.** No production code change
  merges without a test. No "I'll add tests in a follow-up." See
  `TESTING.md` — its rules are strict and non-negotiable.
- **Stop when stuck.** If a task is expanding beyond its scope or
  revealing design gaps, stop and report. Don't power through.

---

## ETL-Specific Thinking

Streamline is an ETL engine. ETL has a specific set of failure modes
that the agent must internalize from day one. **Not every problem is a
fatal error; not every problem is safe to ignore.** The right response
depends on what failed, at what level, and what the config says.

Severity levels (see plan Section 5.3 "Observations" and the
`OBSERVATIONS.md` doc for the full model):

- **Info** — worth recording, no action needed. File took longer than
  baseline. A retry succeeded.
- **Warning** — worth attention but the batch proceeded. Schema drift
  with `warn` policy. Optional column missing.
- **Error** — something failed but was handled (quarantine, rollback).
  Row failed validation. Transformer returned partial failure. Batch
  may still succeed overall.
- **Critical** — prevents batch completion. File can't be read.
  Required registry entry missing. Commit failed.

**When writing ETL code, always ask:** what severity does this
condition represent? What observation code should I emit? What should
the config control about how this is handled?

Examples from real ETL:

- A file has 1000 rows and 3 fail validation. That's 3 `Error`
  observations (one per row, with code `VALIDATION_FAILED` or similar),
  the rows go to quarantine, the batch proceeds, the file completes
  with a `rows_invalid` count recorded.
- A file has schema drift — a new column the registry doesn't know
  about. That's a `Warning` observation (code `SCHEMA_DRIFT_NEW_COLUMN`)
  unless the drift policy says `block`, in which case it becomes
  `Critical` and the file is rejected during ingestion.
- A transformer runs and writes to `dim_broker`. That's an `Info`
  observation (code `TRANSFORMER_INVOKED`) with the outcome in the
  context. If the transformer returned `rows_failed > 0`, add an
  `Error` observation too.
- A zip contains an entry that doesn't match any file mapping. If
  policy is `warn`, that's a `Warning`. If policy is `fail`, it's
  `Critical`. If policy is `ignore`, it's still an `Info` observation
  — the engine noticed, it's just not acting on it.

The agent's job includes **emitting the right observations at the
right places**. Silent swallowing of ETL errors is the single biggest
anti-pattern in this codebase, and the one the predecessor project
suffered from most.

---

## Context Awareness and Suggesting Improvements

**Streamline is a greenfield build. The plan is detailed but not
exhaustive.** The agent is expected to surface improvements, gaps, and
risks as it encounters them, not silently work around them.

When you notice something, categorize it:

1. **The plan is wrong or incomplete for the task at hand.** Stop,
   state the gap, propose a resolution. Don't invent a solution
   silently; that creates undocumented decisions that haunt later
   phases.

2. **Something in the existing code looks off (e.g., during a
   migration).** File an observation (metaphorically — an issue or
   comment). Don't fix it in your current PR unless it directly blocks
   the task. Unrelated fixes make diffs harder to review and risk
   unintended side effects.

3. **You notice a pattern that could be a useful abstraction.** Don't
   extract it on first occurrence. Wait for the third instance, then
   propose the abstraction with concrete examples. Speculative
   abstractions die unloved.

4. **You notice a gap we haven't named yet.** Things like: "the plan
   doesn't say how failed transforms interact with retry when the
   batch has already committed some tables." These are exactly the
   gaps to surface. Propose the gap *as a question*, not an answer.
   The design conversation is where the answer comes from.

5. **You notice a performance issue, security concern, or
   observability gap.** Raise it. Don't fix silently. Don't ignore.
   These are exactly the things that compound if left unaddressed.

**Good examples of agent-initiated observations:**

- "I noticed `BulkInsertIncomingAsync` doesn't have a cancellation
  token check inside the row loop. For a 500K-row file, that means
  cancellation won't take effect until the batch finishes. Should I
  add a check every N rows?"
- "The plan assumes registry YAML files are loaded at startup. For a
  long-running Console process, that means registry changes require
  a restart. Should we support `streamline registry reload` for
  operators who want to update without restarting?"
- "I'm implementing `CombineAllMatchingSelector` and I notice that
  two sheets with the same column names but different data types
  (e.g., `amount` as number in sheet A, string in sheet B) would
  produce invalid rows. The plan doesn't cover this. Options: fail
  loudly, coerce to string, quarantine mismatched rows. Which is
  intended?"

**Bad examples (don't do these):**

- Silently handling an edge case the plan doesn't cover.
- Extracting a utility class because "it seemed cleaner" without
  discussion.
- Adding a new config option without updating `OBSERVATIONS.md` or
  the plan.
- "Fixing" an existing pattern in Streamline because you prefer a
  different style.
- Adding tooling (linters, analyzers, formatters) without a
  conversation about whether it belongs.

**The bar for surfacing is low; the bar for changing is high.** If in
doubt: surface, don't change.

---

## Decisions Already Made

See plan Section 11 for the full list. The short version — these are
settled and not open for re-litigation in PRs:

- Target framework: .NET 10.
- Pluggable storage backends; Postgres is first-party default.
- DDD layering is enforced by `NetArchTest` — violations fail the build.
- `Streamline.Console` is a project reference until Phase 7.
- Specialized libraries per format (CsvHelper, ClosedXML,
  System.Text.Json) — no generic multi-format library.
- Retry is first-class from day one.
- Structural validation only; no semantic data quality checks.
- Migration order: replication jobs before transform jobs.
- Transformers are pluggable (SQL function or C# class); transform is
  opt-in per registry entry.
- Row-level lineage is mandatory.
- Built-in row-count reconciliation is automatic and non-configurable
  in v1.
- Custom reconciliation is Phase 9 (post-1.0).
- Observations are a first-class domain concept with four severity
  levels. Every non-trivial event in the engine emits an observation
  with a stable code.
- Commit messages carry no tool-attribution footers.

If a task seems to require changing one of these, that's a design
review, not a code change. Raise it, don't patch around it.

---

## Common Mistakes to Avoid

These are patterns agents tend to fall into on ETL / DDD projects. Be
vigilant.

**1. Leaking infrastructure concerns into Domain.** If you find yourself
importing `Npgsql` or `ClosedXML` or `CsvHelper` into `Streamline.Domain`,
stop. Domain talks in contracts (`IStagingRepository`, `IFileReader`),
not in implementations. The architecture test will fail.

**2. Inventing state transitions.** The state machine is explicit in
`Streamline.Domain.Batches.RowStateMachine`. If your code wants to move
a row from `Committed` back to `Pending`, that's an illegal transition
(retry goes through `RolledBack → Pending`). Read the state machine
before assuming you know where a row goes next.

**3. Writing "it works on the happy path" code.** For every happy-path
change, think through: what if validation fails? what if the transform
throws? what if the destination write fails mid-batch? what if the
batch is retried? The state model is designed to handle these; your
code has to use it correctly.

**4. Over-abstracting early.** Resist the urge to build a plugin system
for something that has one implementation today. The plan has
deliberate opinions about what's pluggable (storage backends, file
readers, transformers) and what isn't. Don't expand the plugin surface
without a conversation.

**5. Silent failures.** ETL bugs are worst when they're silent. If a
condition is unexpected, fail loudly with a clear message — quarantine
with an error code, throw with context, log a structured warning.
Never `catch { /* ignore */ }`. Never return `null` where an empty
result would be misleading.

**6. Reading files twice.** The current (pre-Streamline) code reads
files twice during ingestion (once for headers, once for rows).
Streamline's `IFileReader` is single-pass by design. If you're adding
a reader and find yourself opening the file twice, you're doing it
wrong.

**7. Skipping lineage.** Every destination row must have a lineage
record. For engine-driven upserts this is automatic; for transform
writes the transform is responsible. If you're writing a transform and
don't see lineage population, the code is incomplete.

**8. Treating tests as optional.** No tests, no merge. See
`TESTING.md` for what's required.

---

## When to Ask vs When to Proceed

**Ask before coding when:**
- The task requires a decision not covered in the plan or this doc.
- You've discovered the task is larger or smaller than it looks.
- An open question in plan Section 12 is blocking.
- A migration would break consumer contracts (registry schema, config
  API, public types in `Streamline.Core`).
- Two reasonable approaches exist and the plan doesn't pick.
- You're about to add a new project, new NuGet dependency, or new
  public interface.
- You're about to modify `Streamline.Core` primitives (these are the
  most sensitive types; changes ripple through everything).

**Proceed without asking when:**
- The task is a straightforward implementation of a specified work
  item from the current phase.
- You're writing tests for existing behavior.
- You're fixing a bug that has a clear root cause and a minimal fix.
- You're refactoring within a single file or class for clarity, with
  no external behavior change.
- You're adding logging, documentation, or code comments.

**When in doubt, ask.** The cost of pausing to clarify is always
lower than the cost of an hour spent going in the wrong direction.

---

## Escalation

If you're stuck, the escalation order is:

1. Re-read the relevant section of the plan.
2. Search the existing codebase for similar patterns.
3. State your question concretely — "I need to decide between X and Y;
   the plan says Z, which I read as either X or Y; which is intended?"
4. Stop coding until you have an answer.

Do not fabricate a decision and move on. Every undocumented decision
is a future debugging session.

---

## Definition of Done (per task)

A task is done when all of the following are true:

- Code implements the stated work item, nothing more.
- Tests exist for the change and pass locally (unit + integration where
  applicable — see `TESTING.md`).
- Architecture test passes (`dotnet test` on `Streamline.Architecture.Tests`).
- No new compiler warnings introduced.
- Nullable reference annotations are accurate (no `!` forgiveness
  operator without comment explaining why).
- Commit message follows the convention in `WORKFLOW.md`.
- Changes documented where needed: XML doc comments on new public
  types, README updates if behavior changes, plan updates if the
  design evolved.

A task is NOT done when:

- "It compiles." Compilation isn't verification.
- "All tests pass" but no new tests were added for the change.
- "It works on my machine" but integration tests weren't run.
- "I'll add tests in a follow-up PR." Follow-up PRs rarely happen.

---

## Files You'll Reference Often

| File | Purpose |
|---|---|
| `streamline-plan.md` | Full design, phases, decisions |
| `TESTING.md` | Testing strategy and requirements (strict) |
| `WORKFLOW.md` | Work cycle, commits, PRs |
| `OBSERVATIONS.md` | Observation codes, severities, where each is emitted |
| `PARKED.md` | Deferred decisions awaiting their owning sub-phase |
| `AGENTS.md` | This file — session startup |
| `README.md` | Project overview, quick start |
| `CHANGELOG.md` | What shipped, when, what broke |

If any of these files says something that contradicts the plan, the
plan wins. Update the contradicting file in a PR, don't just drift.

---

## Last Note

This project is explicitly designed for predictable cycles. That
predictability comes from discipline: plan, implement, test, commit,
repeat. Most of the practices in `WORKFLOW.md` look mechanical. They
are. The mechanics are what make the cycle reliable.

If a task feels like it needs heroics, stop and ask. Heroics on an
internal ETL library are almost always a sign the approach is wrong,
not that you're pushing through something hard.
