# Streamline

A generic, configurable ETL engine for .NET 10. One codebase driven by
registry configuration, replacing tens of bespoke ETL jobs. Consumers
point it at source files and a database; the engine handles ingestion,
staging, structural validation, optional transformation, destination
writes, row-level lineage, and built-in row-count reconciliation.

## Starting point for contributors

**New agents landing on this repository for the first time:** read
**[`docs/ONBOARDING.md`](docs/ONBOARDING.md) first.** It sequences
the reading order, points at the load-bearing parts, and gets you
productive in 15 minutes.

**Returning contributors:** read **[`docs/AGENTS.md`](docs/AGENTS.md)
first.** It defines how the project is worked on: required reading
before coding, layering rules, commit conventions, and the mistakes
the project is explicitly guarding against.

## Documentation

The four handover docs (read in this order on first session):

| File | Purpose |
|---|---|
| [`docs/ONBOARDING.md`](docs/ONBOARDING.md) | First-session sequence. 15-minute read for new agents. |
| [`docs/HANDOVER.md`](docs/HANDOVER.md) | Working knowledge: disciplines, how the user works, warnings, architectural reasoning, examples, rejected decisions, debug gotchas. **Living document.** |
| [`docs/PHASE-1-CLOSEOUT.md`](docs/PHASE-1-CLOSEOUT.md) | Frozen artifact of Phase 1's close. What's built, what's parked, what's ready for Phase 2. |
| [`docs/STANDARDS.md`](docs/STANDARDS.md) | Flat index of every load-bearing standard. 5-minute scan; pointers for depth. |

The authoritative docs (referenced by the handover stack):

| File | Purpose |
|---|---|
| [`docs/AGENTS.md`](docs/AGENTS.md) | Session-startup guide. Read on every task. |
| [`docs/streamline-plan.md`](docs/streamline-plan.md) | Full design: scope, architecture, phases, decisions, open questions. |
| [`docs/WORKFLOW.md`](docs/WORKFLOW.md) | Development rhythm: plan, implement, test, commit. Includes the standing sub-phase report format. |
| [`docs/TESTING.md`](docs/TESTING.md) | Testing requirements. Strict and enforced. |
| [`docs/OBSERVATIONS.md`](docs/OBSERVATIONS.md) | Observation taxonomy and stable code catalog. |
| [`docs/PARKED.md`](docs/PARKED.md) | Deferred decisions awaiting their owning sub-phase. |

## Status

**End of Phase 1.** In-memory verification surface complete — domain
types, state machines, validators, observability, orchestrators,
handlers, fakes, integration tests, predecessor-bug regression
guards. 821 tests, 0 warnings, 9 active parked decisions. Phase 2
(Postgres infrastructure) is the next milestone; opens with P-5
(TimeProvider injection) and P-11 (handler ↔ persisted-batch-status
bridging) as first-tier design questions.

## Requirements

- **.NET 10 SDK** — install from
  <https://dotnet.microsoft.com/download/dotnet/10.0> or via your
  platform package manager (e.g., `apt install dotnet-sdk-10.0` on
  Debian/Ubuntu). The repo is known to build against SDK **10.0.107**
  (any later `10.0.x` is fine). CI pins `dotnet-version: '10.0.x'`.
- Docker — only needed once Phase 2 lands Testcontainers-backed
  integration tests. Not required today.

## Getting started

```bash
git clone <repo>
cd streamline
dotnet restore Streamline.slnx
dotnet build Streamline.slnx
```

## Running the tests

```bash
# All non-integration tests (fast; unit + use-case + architecture).
dotnet test Streamline.slnx --filter "Category!=Integration"

# Integration tests (Phase 2+; needs Docker for Testcontainers).
dotnet test Streamline.slnx --filter "Category=Integration"

# Single project
dotnet test tests/Streamline.Architecture.Tests
```

The architecture test (`tests/Streamline.Architecture.Tests`) enforces
the DDD dependency direction described in
[`docs/streamline-plan.md` §5.1](docs/streamline-plan.md). It fails the
build if any project takes on an illegal reference.

## Running the host

```bash
dotnet run --project src/Streamline.Console
```

Prints `Streamline v<version>` and exits. Real CLI verbs (`ingest`,
`process`, `retry`, `inspect`) land in Phase 2b.

## Project layout

```
src/
  Streamline.Core             Primitives, enums, value types (no deps)
  Streamline.Domain           Aggregates, state machine, repository contracts
  Streamline.Application      Use cases, orchestration
  Streamline.Infrastructure   Postgres staging / destination / lineage
  Streamline.Registry.Postgres  Postgres-backed registry + file mapping
  Streamline.Registry.Yaml      YAML-backed registry + file mapping
  Streamline.Registry.Hybrid    YAML source + Postgres cache
  Streamline.Files.Delimited    CsvHelper-based reader
  Streamline.Files.Xlsx         ClosedXML-based reader
  Streamline.Files.Json         System.Text.Json-based reader
  Streamline.Files.Zip          Container dispatcher
  Streamline.Console            Host (CLI + DI composition)

tests/
  Streamline.Core.Tests
  Streamline.Domain.Tests
  Streamline.Application.Tests        (in-memory fakes live here)
  Streamline.Infrastructure.Tests     (Testcontainers-backed)
  Streamline.Registry.Postgres.Tests  (Testcontainers-backed)
  Streamline.Integration.Tests        (end-to-end)
  Streamline.Architecture.Tests       (NetArchTest + csproj inspection)
```

Dependency direction is strict and enforced by the architecture test:
Core → (nothing), Domain → Core, Application → Domain + Core,
Infrastructure / Registry.* / Files.* → Application + Domain + Core,
Console → everything above. See
[`docs/streamline-plan.md` §5.1](docs/streamline-plan.md) for detail.

## Build configuration

- `Directory.Build.props` centralises target framework (net10.0),
  language version (latest), nullable reference types, analyzer
  settings (`latest-recommended`), and `TreatWarningsAsErrors`.
- `Directory.Packages.props` centralises every package version via
  Central Package Management. Package references in project files
  never carry versions.
- `tests/Directory.Build.props` adds the shared test-framework
  references (xUnit v3, AwesomeAssertions, NSubstitute,
  coverlet.collector) so test project files stay minimal.
