# Streamline

A generic, configurable ETL engine for .NET 10. One codebase driven by
registry configuration, replacing tens of bespoke ETL jobs. Consumers
point it at source files and a database; the engine handles ingestion,
staging, structural validation, optional transformation, destination
writes, row-level lineage, and built-in row-count reconciliation.

## Starting point for contributors

Read **[`docs/AGENTS.md`](docs/AGENTS.md) first.** It defines how the
project is worked on: required reading before coding, layering rules,
commit conventions, and the mistakes the project is explicitly guarding
against.

## Documentation

| File | Purpose |
|---|---|
| [`docs/AGENTS.md`](docs/AGENTS.md) | Session-startup guide. Read on every task. |
| [`docs/streamline-plan.md`](docs/streamline-plan.md) | Full design: scope, architecture, phases, decisions, open questions. |
| [`docs/WORKFLOW.md`](docs/WORKFLOW.md) | Development rhythm: plan, implement, test, commit. |
| [`docs/TESTING.md`](docs/TESTING.md) | Testing requirements. Strict and enforced. |
| [`docs/OBSERVATIONS.md`](docs/OBSERVATIONS.md) | Observation taxonomy and stable code catalog. |

## Status

**Pre-Phase 0.** No code yet. Scaffolding follows in subsequent commits
per Phase 0 of the plan. Once scaffolding lands, this README will be
updated with build, test, and run instructions.
