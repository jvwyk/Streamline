# Streamline — Project Plan

A ground-up build of a generic, configurable ETL engine to replace 10+
bespoke jobs with a single codebase driven by registry configuration.

---

## 1. Vision

One engine. Many configs. Each existing bespoke ETL job becomes a
registry entry plus a file-mapping pattern. Schema changes become config
changes, not code changes. New sources become new registry entries, not
new jobs.

The engine is built once, packaged later, and consumed internally via
`Streamline.Console` during development. Packaging as a versioned
internal NuGet happens only after the engine is stable and at least one
real job has migrated successfully.

## 2. Problem Statement

The current state across the organisation:

- 10+ ETL jobs, each written differently, each with its own file reading
  logic, skip-row handling, staging table, and transform logic.
- Each source schema change requires code changes in the affected job,
  potentially a staging table modification, and transform adjustments.
- New sources require new jobs from scratch. There is no shared engine,
  no shared observability, no shared state model.
- All the headaches of ETL — drift, encoding, partial failures, retry,
  FK resolution order, quarantine semantics — have been solved N times,
  inconsistently.

The current in-flight project proved the direction (registry +
file-mapping + staging model) but remains coupled to one database and
one destination schema, limiting reuse.

Streamline is the clean-sheet version, taking everything learned from
the in-flight project, without the accumulated coupling.

## 3. Scope

### In scope

- Config-driven ingestion from files into a staging model.
- Config-driven processing from staging into destination tables.
- Structural validation: types, required, length, range, pattern, FK.
- Drift detection with configurable policy (block / warn / ignore).
- FK enforcement with configurable policy per reference.
- Batch-level observability: what ran, what succeeded, what was
  quarantined, what was rolled back, what can be retried.
- Retry semantics: a failed batch can be resumed without reprocessing
  already-committed rows.
- Multiple destination schemas within PostgreSQL (e.g., different master
  schemas for different domains).
- Multiple databases for staging and destination (different connection
  strings per job).
- **Replication pipelines** (file-to-destination mirroring with no
  reshaping). The engine handles validation, staging, and upsert
  directly. This is the default and primary path — most registry
  entries use it.
- **Transform pipelines** (source data reshaped into master / dim / fact
  tables). The engine handles validation and staging, then invokes a
  registered transform that owns the domain logic. Transforms are
  pluggable: SQL functions or C# classes, selected per registry entry.
  The engine has no opinion about what a transform does.
- **Row-level lineage** from source file → staging row → destination
  row. Every destination row produced by the engine or a transform is
  traceable back to its originating source row.
- **Built-in row-count reconciliation** at batch completion. Every
  batch produces a summary of rows in, rows staged, rows transformed,
  rows destined, rows quarantined. Mismatches flag the batch
  automatically. No configuration required — the engine knows these
  counts and reports on them.
- **Pluggable storage backends** chosen by the consumer at DI
  registration. The engine defines `IRegistryRepository`,
  `IFileMappingRepository`, `IStagingRepository`, and
  `IDestinationAdapter` as contracts; consumers select the
  implementation at startup. First-party implementations shipped in
  v1: Postgres-backed for all four, YAML-backed for registry and
  file-mapping, and a YAML-source-with-Postgres-cache hybrid. Consumers
  can implement the contracts against other backends (SQL Server, flat
  files, in-memory); those implementations are the consumer's
  responsibility, not the core team's.

### Deferred to post-1.0 (Phase 9)

- **Custom reconciliation checks.** A registry-declared hook for
  consumer-supplied SQL or C# checks (e.g., "sum of `trade_amount` in
  source equals sum in destination"). Built-in row-count reconciliation
  ships in 1.0; custom checks wait for Phase 9 after the engine is
  proven in production on multiple real jobs.

### Out of scope (v1)

- Non-PostgreSQL destinations. The engine is PostgreSQL-only in v1. A
  destination abstraction exists in the domain layer but has a single
  concrete implementation; abstraction-over-DB-engines is deferred until
  there is a real second engine.
- Streaming / CDC sources. Batch-oriented only.
- Data quality beyond structural validation (outlier detection,
  statistical profiling, cross-batch trend analysis). Structural rules
  only; semantic quality belongs in downstream tools.
- Transformation logic *inside the engine*. Transforms are executed by
  pluggable transformers (SQL function or C# class) owned by the
  domain. The engine orchestrates invocation and records outcomes; it
  does not express transform logic. No expression language,
  no joins-in-config, no transform DSL.
- Scheduling. Cron / orchestrator concerns are the consumer's problem.
- GUI. Registry is managed via SQL or YAML; no admin UI.
- Multi-tenant staging. One staging schema per database.

### Explicit non-goals

- Not a library-as-product. External consumers are not a concern.
- Not a plugin marketplace. Extension points are added as real needs
  arise, not speculatively.
- Not a replacement for SSIS, Airflow, or dbt. Different shape, different
  problem.

## 4. Success Criteria

Streamline is considered successful when:

1. At least 5 of the 10 bespoke jobs are retired and running on Streamline
   in production, with side-by-side validation showing equivalent output.
2. Adding a new source file type requires only a new registry entry and
   a file-mapping entry — no C# code changes (unless the entry declares
   a C# transform, in which case the transform class is the only code
   added).
3. A schema change on an existing source requires only a registry entry
   update (possibly versioned via `valid_from` / `valid_to`).
4. A failed batch can be retried without manual SQL intervention.
5. Any destination row can be traced back to its source file and row
   number in one query.
6. A new engineer can run the full test suite locally against a
   Postgres container within 10 minutes of cloning.

---

## 5. Architecture

### 5.1 Layering (DDD)

Strict dependency direction: outer layers depend inward; inner layers
know nothing of outer layers.

```
┌─────────────────────────────────────────────────────────┐
│  Streamline.Console  (host)                             │
│  CLI, DI composition, appsettings, entry point          │
└────────────────────┬────────────────────────────────────┘
                     │ depends on
┌────────────────────▼────────────────────────────────────┐
│  Streamline.Infrastructure                              │
│  Postgres adapters, file readers (Cinchoo),             │
│  migrations, connection factories, advisory locks       │
└────────────────────┬────────────────────────────────────┘
                     │ implements interfaces from
┌────────────────────▼────────────────────────────────────┐
│  Streamline.Application                                 │
│  Use cases: IngestBatch, ProcessBatch, RetryBatch       │
│  Orchestration, transactional boundaries                │
└────────────────────┬────────────────────────────────────┘
                     │ uses
┌────────────────────▼────────────────────────────────────┐
│  Streamline.Domain                                      │
│  Aggregates (Batch, IngestionSession)                   │
│  Domain services, state machine, domain events          │
│  Repository interfaces (DIP)                            │
└────────────────────┬────────────────────────────────────┘
                     │ uses
┌────────────────────▼────────────────────────────────────┐
│  Streamline.Core                                        │
│  Value types, enums, result types, primitive contracts  │
│  No dependencies on anything                            │
└─────────────────────────────────────────────────────────┘
```

**Dependency rules (enforced in CI via an architecture test):**

- `Core` depends on nothing (not even `Microsoft.Extensions.*`).
- `Domain` depends only on `Core`.
- `Application` depends on `Domain` and `Core`.
- `Infrastructure` depends on `Application`, `Domain`, and `Core`.
- `Console` depends on all of the above but contains no business logic —
  only DI wiring, argument parsing, and logging configuration.
- No layer imports `Npgsql`, `Cinchoo`, or any concrete library outside
  `Infrastructure`.

Architecture tests fail the build if these rules are violated. This is
non-negotiable — layering discipline erodes fast without a guardrail.
The implementation combines csproj-XML inspection (declaration-level
rules) with `NetArchTest` (type-level rules); see the Phase 0 entry
in §6 for why both layers exist.

### 5.2 Solution Structure

`dotnet new sln` produces `Streamline.slnx` (the new XML solution
format, default in current .NET 10 SDKs); the legacy `.sln` filename
is retained only by older tooling.

```
/Streamline.slnx
  /src
    /Streamline.Core
      Enums/
        RowStatus.cs              (Pending, Processing, Committed, RolledBack, Quarantined)
        BatchStatus.cs
        FkEnforcementMode.cs      (Always, WhenParentPopulated, Never)
        DriftPolicy.cs            (Block, Warn, Ignore)
        ColumnTypeCode.cs         (String, Integer, BigInt, Decimal, Date, Timestamp, Boolean, Uuid)
        TransformKind.cs          (SqlFunction, CSharp)
        TransformInvocation.cs    (PerBatch — PerRow deferred)
      ValueTypes/
        Record.cs                 (immutable key→value map + SourceFileName + SourceRowIndex)
        ColumnDefinition.cs
        SchemaDefinition.cs
        FileMapping.cs
        ValidationError.cs
        FkReference.cs            (parent table + parent column + FkEnforcementMode)
      Results/
        IngestionResult.cs
        FileIngestionOutcome.cs   (per-file aggregate inside IngestionResult)
        ProcessingResult.cs
        TableProcessingOutcome.cs (per-table aggregate inside ProcessingResult)
        ValidationResult.cs
        UpsertOutcome.cs

    /Streamline.Domain
      Batches/
        Batch.cs                  (aggregate root; enforces state transitions)
        BatchId.cs
        BatchContext.cs           (passed to transformers; batch ID, file IDs, timings)
        StateTransition.cs
        DomainEvents/
          BatchStartedEvent.cs
          RowQuarantinedEvent.cs
          TransformInvokedEvent.cs
          ReconciliationCompletedEvent.cs
          BatchCompletedEvent.cs
      Registry/
        RegistryEntry.cs
        RegistryValidator.cs      (domain service: validates a registry entry is self-consistent)
      Validation/
        ColumnTypeParser.cs       (static; ColumnTypeCode → .NET typed value coercion)
        RowValidator.cs           (domain service: validates a Record against a SchemaDefinition)
        FkResolver.cs             (domain service: checks row FK values against loaded cache)
      Transforms/
        TransformReference.cs     (registry-declared pointer: kind, reference, destination)
        TransformOutcome.cs
        ITransformer.cs           (consumers implement this for C# transforms)
      Lineage/
        RowLineage.cs             (source incoming_id → destination table + PK values)
      Reconciliation/
        ReconciliationOutcome.cs
        BuiltinRowCountCheck.cs   (domain service; compares staging counts to transform outcomes)
      StateMachine/
        RowStateMachine.cs        (enforces legal transitions)
      Abstractions/
        IRegistryRepository.cs        (read/write schema registry entries)
        IFileMappingRepository.cs     (read/write file→table mappings)
        IStagingRepository.cs
        IDestinationAdapter.cs
        IFileReader.cs                (for leaf files: CSV, xlsx, JSON, XML)
        IFileDispatcher.cs            (for containers: zip, tar, future formats)
        IFileReaderRegistry.cs        (resolves readers and dispatchers by format)
        ITransformerRegistry.cs       (dispatches to SQL or C# transformer based on reference.Kind)
        ILineageRepository.cs
        // Storage backend for registry, file mapping, staging, and destination
        // is chosen by the consumer at DI registration. The Domain defines
        // contracts; the consumer picks one of the first-party implementations
        // (Postgres, YAML, hybrid) or provides their own.

    /Streamline.Application
      UseCases/
        IngestBatch/
          IngestBatchCommand.cs
          IngestBatchHandler.cs
        ProcessBatch/
          ProcessBatchCommand.cs
          ProcessBatchHandler.cs
        RetryBatch/
          RetryBatchCommand.cs
          RetryBatchHandler.cs
        InspectBatch/
          InspectBatchQuery.cs
          InspectBatchHandler.cs
      Services/
        IngestionOrchestrator.cs
        ProcessingOrchestrator.cs
        SchemaDriftDetector.cs
      Transactions/
        IUnitOfWork.cs

    /Streamline.Infrastructure
      Postgres/
        StagingRepository.cs
        DestinationAdapter.cs
        LineageRepository.cs
        ConnectionFactory.cs
        AdvisoryLock.cs
        Migrations/
          001_CreateStreamlineSchema.sql
          002_CreateStagingTables.sql
          003_CreateTransformLog.sql
          004_CreateLineageTable.sql
          005_CreateReconciliationLog.sql
          ...
      Transformers/
        TransformerRegistry.cs         (dispatches based on TransformReference.Kind)
        SqlFunctionTransformer.cs      (invokes registered Postgres function)
        CSharpTransformer.cs           (resolves consumer ITransformer implementations via DI)
      Reconciliation/
        BuiltinReconciliationRunner.cs (runs row-count checks at batch completion)
      Files/
        FileReaderRegistry.cs          (resolves IFileReader / IFileDispatcher by format)
        FileHasher.cs
      Logging/
        StreamlineLoggerExtensions.cs

    /Streamline.Registry.Postgres         (Postgres-backed registry + file mapping)
      PostgresRegistryRepository.cs       (implements IRegistryRepository)
      PostgresFileMappingRepository.cs    (implements IFileMappingRepository)
      Migrations/
        001_CreateRegistryTables.sql      (schema_registry, file_mapping)
      PostgresRegistryModule.cs           (DI extension: services.AddPostgresRegistry(...))

    /Streamline.Registry.Yaml             (YAML-backed registry + file mapping)
      YamlRegistryRepository.cs
      YamlFileMappingRepository.cs
      YamlRegistryLoader.cs               (reads YAML files, validates schema)
      YamlRegistryModule.cs               (DI extension: services.AddYamlRegistry(...))

    /Streamline.Registry.Hybrid           (YAML source, Postgres cache)
      HybridRegistryRepository.cs         (wraps YamlRegistryLoader + PostgresRegistryRepository)
      HybridRegistryModule.cs             (DI extension: services.AddHybridRegistry(...))

    /Streamline.Files.Delimited             (wraps CsvHelper)
      DelimitedFileReader.cs                (implements IFileReader)
      DelimitedConfig.cs

    /Streamline.Files.Xlsx                  (wraps ClosedXML)
      XlsxFileReader.cs                     (implements IFileReader)
      XlsxConfig.cs
      SheetSelectors/
        SheetSelector.cs                    (abstract base)
        ByNameSelector.cs
        ByNamePatternSelector.cs
        ByIndexSelector.cs
        FirstVisibleSelector.cs
        NamedRangeSelector.cs
        CombineAllMatchingSelector.cs       (multi-sheet combine mode)
        MatchSchemaSelector.cs              (schema-discovery mode)
        SheetSelectorResolver.cs

    /Streamline.Files.Xls                   (wraps ExcelDataReader; added when first .xls job migrates)
      XlsFileReader.cs
      XlsConfig.cs

    /Streamline.Files.Json                  (wraps System.Text.Json)
      JsonFileReader.cs                     (implements IFileReader)
      JsonConfig.cs

    /Streamline.Files.Xml                   (wraps System.Xml; added when first XML job migrates)
      XmlFileReader.cs
      XmlConfig.cs

    /Streamline.Files.Zip                   (container dispatcher)
      ZipFileDispatcher.cs                  (implements IFileDispatcher)
      ZipConfig.cs
      ZipDispatchModes/
        RouteMode.cs
        CombineMode.cs
        PickMode.cs
        EntrySelector.cs

    /Streamline.Console
      Program.cs
      Commands/
        IngestCommand.cs
        ProcessCommand.cs
        RetryCommand.cs
        RunCommand.cs
        InspectCommand.cs
      Composition/
        ServiceRegistration.cs
      appsettings.json
      appsettings.Development.json

  /tests
    /Streamline.Core.Tests
    /Streamline.Domain.Tests
    /Streamline.Application.Tests          (uses in-memory infrastructure fakes)
    /Streamline.Infrastructure.Tests       (testcontainers for Postgres)
    /Streamline.Integration.Tests          (end-to-end via Console)
    /Streamline.Architecture.Tests         (NetArchTest — enforces layering)
```

### 5.3 Domain Model — Key Concepts

**RegistryEntry.** One per logical table. Describes source-column →
target-column mappings (replication case) or source-column validation
rules plus a transform reference (transform case). Includes type info,
required flags, FK references, PK columns, load-order dependencies,
drift policy, FK enforcement policy. Stored in the staging database
(or bootstrapped from YAML files for version control).

**Two pipeline modes, selected by registry entry shape:**

- **Replication mode.** Registry entry has `column_mappings` with
  `target` fields. The engine validates, stages, and upserts directly
  to the destination table. No transform. This is the default.
- **Transform mode.** Registry entry has `source_columns` (validation
  only — no `target` field) and a `transform` section. The engine
  validates, stages, then invokes the registered transformer, which
  owns the write to the destination. The engine records the outcome
  but has no opinion about what the transform does.

A single engine binary handles both modes; which one runs for a given
registry entry is determined by whether that entry declares a
`transform`.

**FileMapping.** One or more per RegistryEntry. Associates a filename
regex pattern with a target table and reader format (delimited, json,
xml, parquet, etc.). Includes reader-specific config (delimiter, skip
rows, encoding, header handling).

**Batch.** An aggregate representing one end-to-end run. Owns the state
transitions of its rows. A Batch is created during ingestion and
finalized after processing. All state changes to rows go through the
Batch aggregate.

**Record.** An immutable key→value map representing a single source row.
Produced by file readers, consumed by validators and destinations.

**ColumnTypeParser.** Static parser that coerces raw string values to
.NET typed values per `ColumnTypeCode`. Lives in
`Streamline.Domain.Validation`, not `Streamline.Core`, because parsing
is behaviour rather than vocabulary — Core stays flat. Invoked by
`RowValidator` for the `INVALID_TYPE` check. Uses
`InvariantCulture`, no whitespace tolerance, no thousand separators
in decimals; `Date` parses to `DateOnly`, `Timestamp` parses to
`DateTimeOffset` (never naked `DateTime`); boolean accepts the
common ETL serialisations (`true/false`, `1/0`, `y/n`, `yes/no`)
case-insensitively.

**TransformReference.** A registry-declared pointer to a transformer:
`kind` (`sql_function` or `csharp`), `reference` (the function name or
the fully-qualified C# type name), and a `destination_table`. Resolved
at runtime by `ITransformerRegistry`.

**ITransformer.** The contract consumers implement for C# transforms.
Receives a `BatchContext` (batch ID, file IDs, access to staging data
if needed) and returns a `TransformOutcome` (counts, duration, errors).
SQL function transforms don't implement this — they're called through
`SqlFunctionTransformer`, which passes `batch_id` as a parameter to the
function and reads its returned outcome.

**RowLineage.** The link between a staging `incoming_id` and the
destination row(s) it produced. Populated by the engine for
engine-driven upserts (replication mode). Populated by transforms
themselves (via convention: each transform must emit lineage records
alongside its destination writes) in transform mode. Stored in
`staging.row_lineage`.

**Row states** (see `Streamline.Core.Enums.RowStatus`):

```
T1:  (none) → Pending        via staging insert during ingestion
T2:  Pending → Processing    row claimed for processing (exclusive lock)
T3:  Processing → Committed  upsert succeeded (replication) OR transform
                             invoked successfully (transform), transaction
                             committed
T4:  Processing → RolledBack savepoint rolled back or transform failed
                             (retryable)
T5:  Pending    → Quarantined validation or DB error (autocommit, survives
                             rollback)
T6:  Processing → Quarantined validation or DB error during processing
T7:  RolledBack → Pending    via RetryBatchCommand (re-queues for processing)
T8:  Quarantined → Pending   via manual resolution (re-queues after fix)
```

Transform outcomes are also tracked at the (batch, table) level in
`staging.transform_log`, separate from row state. A transform failure
transitions its rows to `RolledBack` (retryable) rather than
`Quarantined` — because the failure is usually a batch-level issue
(missing mapping row, function bug) rather than a per-row data issue.

Illegal transitions (e.g., `Committed → anything`) throw. The state
machine is enforced in the Domain layer and validated at every call
site.

**Destination adapter contract.** Defined in Domain as an interface. The
adapter is responsible for: beginning a transaction, creating nested
scopes (savepoints), upserting records, querying existing values for FK
resolution, committing, rolling back. The PostgreSQL implementation in
Infrastructure provides these. The interface is shaped by what the
Domain needs, not by what PostgreSQL happens to offer.

**File readers and dispatchers.** Two abstractions for reading source
data:

- `IFileReader` handles *leaf files* — one file produces one stream of
  records. Implementations: `DelimitedFileReader` (CsvHelper),
  `XlsxFileReader` (ClosedXML), `XlsFileReader` (ExcelDataReader),
  `JsonFileReader` (System.Text.Json), `XmlFileReader` (System.Xml). Each
  is a separate project wrapping one specialized library, chosen for
  best-in-class handling of that format's edge cases. Generic
  multi-format libraries (Cinchoo, etc.) are deliberately not used —
  specialized libraries give better control over format-specific
  pathologies (BOM handling, quote variations, Excel date epochs,
  formula errors, XML namespaces, etc.).

- `IFileDispatcher` handles *containers* — one file produces multiple
  ingestion units, each routed to its own reader. Implementations:
  `ZipFileDispatcher`. The engine asks the file reader registry whether
  a file is a container; if yes, dispatches; if no, reads directly.
  Dispatchers can recurse (a zip containing a zip) up to a configured
  depth cap (default 1).

**Reader configuration is rich and format-specific.** The `FileMapping`
carries a reader-specific config block interpreted by the relevant
reader. This covers the wide range of real-world formatting
pathologies: skip rows, header detection by pattern, trailing empty
columns, mixed encodings, quote escaping variants, Excel sheet
selection, zip entry routing, etc. The engine doesn't know what these
options mean; the reader does.

**Excel sheet selection strategies** (`XlsxConfig.SheetSelector`) are
first-class and pluggable:
- `ByName` — exact sheet name match.
- `ByNamePattern` — regex match, configurable behavior on multiple matches.
- `ByIndex` — zero-indexed position.
- `FirstVisible` — first non-hidden sheet.
- `NamedRange` — a defined named range within the workbook.
- `CombineAllMatching` — multi-sheet combine (one workbook → one logical
  dataset, rows from all matching sheets, optionally tagged with source
  sheet name).
- `MatchSchema` — schema-discovery mode (engine reads each sheet's
  headers, picks the one that matches the registry entry's declared
  columns).

**Zip dispatcher modes** (`ZipConfig.Mode`):
- `Route` — unpack zip, run each entry through normal file-mapping
  resolution. Different entries land in different target tables. This is
  the most common zip pattern.
- `Combine` — unpack zip, treat all matching entries as rows for one
  target table. Mirrors Excel's `CombineAllMatching`.
- `Pick` — select one entry from the zip by name/pattern, ignore the
  rest.

**Reconciliation (built-in, v1).** At batch completion, the engine
computes row-count reconciliation automatically:

- Rows read from source files
- Rows staged to `staging.incoming`
- Rows validated
- Rows transformed (per table, from `staging.transform_log`)
- Rows committed to destination tables
- Rows quarantined

Mismatches between expected and actual counts flag the batch with a
reconciliation warning (or failure, depending on severity). No
configuration needed — this always runs. Custom reconciliation checks
are deferred to Phase 9.

**Observations (first-class issue model).** Beyond logs and exceptions,
Streamline records *observations* — structured events that note
something the engine noticed during a batch. Observations are not
failures by themselves; they are data about what happened. The engine,
the agent working on it, and the operator all rely on observations
being consistent and queryable.

Severity levels:

- `Info` — something happened worth recording but no attention needed.
  Example: a file took longer than baseline to read; a transformer
  was invoked; a retry succeeded on first re-attempt.
- `Warning` — something non-ideal happened but the batch proceeded.
  Example: schema drift detected with policy `warn`; FK cache was
  stale and refreshed; an optional column was missing; a zip entry
  was skipped per `on_unmatched_entry: ignore`.
- `Error` — something failed but was handled (usually via quarantine
  or rollback). The batch may still have succeeded overall if enough
  rows / tables processed cleanly. Example: row failed validation and
  was quarantined; transformer returned `rows_failed > 0`; savepoint
  was rolled back.
- `Critical` — something failed that prevents batch completion.
  Example: file couldn't be read; required registry entry missing;
  deferred transaction failed at commit; advisory lock held by another
  process.

Observations are stored in `staging.observation_log` with:
- `observation_id`, `batch_id`, `file_log_id` (nullable),
  `incoming_id` (nullable), `table_name` (nullable).
- `severity` — one of the four above.
- `code` — a stable string identifier (`SCHEMA_DRIFT_NEW_COLUMN`,
  `FK_CACHE_EMPTY`, `TRANSFORMER_PARTIAL_FAILURE`, etc.).
- `message` — human-readable description.
- `context` — JSONB payload with structured fields relevant to the
  observation (e.g., `{"new_columns": ["X", "Y"]}` for drift).
- `raised_at` — timestamp.

**The engine treats observations as a parallel stream to logs, not a
replacement for them.** Logs are for developers debugging; observations
are for operators understanding what happened in a batch. Every
observation is also logged (at the corresponding level), but every log
line is not an observation.

The `--inspect <batch>` command surfaces observations grouped by
severity. Observations also feed future alerting integrations without
the engine needing an alerting framework.

**Existing ETL error codes from the predecessor project** (MISSING_REQUIRED,
INVALID_TYPE, INVALID_FORMAT, FK_VIOLATION, PK_VIOLATION,
VALIDATION_FAILED, UNKNOWN) are preserved as observation codes for
quarantine events, with additional codes added as new conditions are
observed. The full code list lives in `Streamline.Core.Observations.ObservationCodes`
and is documented in `OBSERVATIONS.md`.

### 5.4 The Console as Direct Consumer

`Streamline.Console` references `Streamline.Infrastructure` as a project
reference, not a package reference. This gives us the benefits of
library-style separation (clean layering, testability, clear consumer
surface) without the overhead of actually publishing packages during
development.

When Streamline stabilizes (after Phase 6), the packaging phase extracts
`Streamline.Core`, `Streamline.Domain`, `Streamline.Application`, and
`Streamline.Infrastructure` as internal NuGet packages. `Streamline.Console`
then switches from project references to package references, becoming
a true external consumer. This is the validation that the library
really is consumable — if the Console can live on package references
without the engine source, any other consumer can too.

---

## 6. Phases

Nine phases, each ending in a working, demonstrable artifact. No phase
depends on a later phase being partially done.

### Phase 0 — Foundation (1 week)

Goal: scaffolding in place, CI green, zero business logic yet.

- Target framework: **.NET 10** across all projects. LangVersion
  `latest`. Nullable reference types enabled solution-wide.
- Create solution and all src projects with correct dependency
  directions.
- Create all test projects referencing their corresponding src
  projects plus test frameworks (xUnit v3, AwesomeAssertions,
  NSubstitute, coverlet.collector). AwesomeAssertions is the
  Apache-2.0 community fork of FluentAssertions 7.x; FluentAssertions
  8.x adopted a paid commercial licence, so we standardise on the
  free fork. The `.Should()` API is identical.
- Add `Streamline.Architecture.Tests` enforcing dependency direction
  via two complementary mechanisms: (1) parsing each src project's
  `csproj` XML for declared `ProjectReference` entries against an
  allow-list per project, and (2) `NetArchTest` type-level rules.
  The csproj layer catches declaration-level violations on empty
  scaffolds (where no types exist yet, so the compiler drops unused
  references and `NetArchTest` alone passes trivially). The
  `NetArchTest` layer activates as types land in later phases. Failures
  collect every violation across all checks before asserting, so the
  failure message names every offender at once.
- Set up CI pipeline: restore, build, test, on every PR. No deploy
  stage yet. CI targets .NET 10 only.
- Configure `.editorconfig`, nullable reference types on, analyzer rules.
- Configure `Directory.Build.props` at the repo root to centralize
  target framework, language version, analyzer settings, and common
  package versions.
- `README.md` at repo root with "how to run locally" instructions
  (will be updated throughout).
- Minimal `Program.cs` in Console that prints "Streamline vX.Y.Z" and
  exits — enough to verify the solution composes.

**Deliverable:** Green CI, empty-but-correct solution targeting .NET
10. Architecture tests pass trivially because nothing has been built
yet.

### Phase 1 — Domain and In-Memory Infrastructure (3 weeks)

Goal: the state machine is correct. The happy path and every failure
path run against fakes, exhaustively tested.

- Implement `Streamline.Core` primitives (enums, value types, results).
- Implement `Streamline.Core.Observations`:
  - `Observation` value type with severity, code, message, context.
  - `ObservationSeverity` enum (`Info`, `Warning`, `Error`, `Critical`).
  - `ObservationCodes` — static class listing every stable code the
    engine can emit, starting with the predecessor project's set
    (MISSING_REQUIRED, INVALID_TYPE, INVALID_FORMAT, FK_VIOLATION,
    PK_VIOLATION, VALIDATION_FAILED, UNKNOWN) plus Streamline-specific
    additions (SCHEMA_DRIFT_NEW_COLUMN, SCHEMA_DRIFT_MISSING_REQUIRED,
    FK_CACHE_EMPTY, TRANSFORMER_PARTIAL_FAILURE, RECONCILIATION_MISMATCH,
    ZIP_ENTRY_UNMATCHED, etc. — see `OBSERVATIONS.md` for the full
    list).
  - `IObservationSink` interface — the contract for recording
    observations during a batch.
- Implement `Streamline.Domain`:
  - `Batch` aggregate with state machine enforcement.
  - `RegistryValidator`, `RowValidator`, `FkResolver` as domain services.
  - `TransformReference`, `TransformOutcome`, `ITransformer` contracts.
  - `RowLineage` value type.
  - `IStagingRepository`, `IDestinationAdapter`, `IFileReader`,
    `IFileDispatcher`, `IRegistryRepository`, `IFileMappingRepository`,
    `ITransformerRegistry`, `ILineageRepository`,
    `IObservationRepository` interfaces.
- Implement `Streamline.Application` use case handlers. Handlers route
  replication vs transform based on registry entry shape but have no
  knowledge of concrete persistence or concrete transformers.
- Implement **in-memory** fakes for every repository and adapter:
  `InMemoryStagingRepository`, `InMemoryDestinationAdapter`,
  `InMemoryRegistryRepository`, `InMemoryFileMappingRepository`,
  `InMemoryTransformerRegistry`, `InMemoryLineageRepository`,
  `FakeFileReader`. A `ScriptedTransformer` test fake deterministically
  produces configurable outcomes so transform paths can be tested
  without real domain logic. These live in `Streamline.Application.Tests`.
- Exhaustive unit tests for the state machine: every transition, every
  illegal transition, every partial-failure scenario. If the refactor
  plan's "flagged behaviors" listed 14 bugs, every one of them has a
  test here that prevents its equivalent in Streamline.
- Transform-specific tests: transform succeeds, transform throws,
  transform returns partial success, transform writes lineage correctly,
  transform failure triggers RolledBack (not Quarantined).

**Deliverable:** `Streamline.Application.Tests` runs end-to-end use
cases against in-memory fakes — for both replication and transform
paths. State machine is proven correct in isolation.

### Phase 2 — Postgres Infrastructure (2-3 weeks)

Goal: real Postgres adapters that pass the same tests as the in-memory
fakes. Split across two packages:

- `Streamline.Infrastructure` holds staging, destination, and lineage
  implementations (plus transformer invocation and built-in
  reconciliation).
- `Streamline.Registry.Postgres` holds the Postgres-backed registry
  and file-mapping implementations. Separate package because registry
  storage is independently pluggable — a consumer can use Postgres for
  staging but YAML for registry.

Work items:

- `PostgresStagingRepository` implementing `IStagingRepository`.
- `PostgresDestinationAdapter` implementing `IDestinationAdapter` with
  savepoint-per-table, deferred transactions, and checksum-based upsert.
- `PostgresLineageRepository` implementing `ILineageRepository`.
- `PostgresRegistryRepository` implementing `IRegistryRepository` (in
  `Streamline.Registry.Postgres`).
- `PostgresFileMappingRepository` implementing `IFileMappingRepository`
  (in `Streamline.Registry.Postgres`).
- Schema migrations split across the two packages:
  - `Streamline.Infrastructure/Migrations/`: `batch_log`, `file_log`,
    `incoming`, `quarantine`, `processing_log`, `transform_log`,
    `row_lineage`, `reconciliation_log`, `observation_log`.
  - `Streamline.Registry.Postgres/Migrations/`: `schema_registry`,
    `file_mapping`.
  - A migration runner composes both sets and applies them in order
    when the consumer has registered both packages.
- Use a migration library (FluentMigrator or a lightweight hand-rolled
  runner). Both packages follow the same convention.
- `Streamline.Infrastructure.Tests` and
  `Streamline.Registry.Postgres.Tests` each use Testcontainers to spin
  up a real Postgres. The *same* test cases that passed against
  in-memory fakes in Phase 1 now run against Postgres for each
  interface independently.
- Advisory lock implementation for preventing concurrent processing
  on the same batch.
- `SELECT ... FOR UPDATE SKIP LOCKED` on pending row fetch for
  concurrency safety.

**Deliverable:** All Phase 1 tests plus equivalent integration suites
pass against real Postgres, across both Infrastructure and
Registry.Postgres. The engine works end-to-end in Postgres mode except
it has no way to read files yet.

### Phase 2b — Configuration API and YAML Registry (1-2 weeks)

Goal: consumers can configure Streamline at DI registration with their
choice of backends, and YAML-backed registry is available as a
first-party alternative to Postgres.

Configuration API (lives in `Streamline.Application` extension
methods, implementations resolved from whichever package is
referenced):

```csharp
services.AddStreamline(options =>
{
    options.UseRegistry(r => r.FromYaml("./config/registry"));
    // or: r.FromPostgres(connectionString);
    // or: r.FromHybrid(yamlPath: "./config/registry", dbConnection: connectionString);

    options.UseFileMapping(m => m.FromYaml("./config/mappings"));
    // same three choices

    options.UseStaging(s => s.OnPostgres(connectionString));

    options.UseDestination(d => d.OnPostgres(connectionString));

    options.AddFileReaders(r =>
    {
        r.Delimited();
        r.Xlsx();
        r.Json();
    });

    options.AddDispatchers(d =>
    {
        d.Zip();
    });

    options.AddTransformers(t =>
    {
        t.SqlFunction();
        t.CSharp(assembly: typeof(Program).Assembly);  // discovers ITransformer implementations
    });
});
```

Work items:

- `AddStreamline(Action<StreamlineOptions>)` extension method and the
  fluent builder types.
- Startup validation: if the consumer registers a YAML registry but no
  Postgres staging (or vice versa), that's valid; if the consumer
  forgets to register *any* registry, startup fails with a clear
  message.
- `Streamline.Registry.Yaml` package:
  - `YamlRegistryRepository` implementing `IRegistryRepository`.
  - `YamlFileMappingRepository` implementing `IFileMappingRepository`.
  - File-watcher mode (optional): detect changes to YAML files and
    reload at runtime. v1 default is load-once-at-startup; watcher is
    behind a config flag.
  - YAML schema validation: reject malformed entries at load time
    with line/column error messages, not at first use.
- `Streamline.Registry.Hybrid` package:
  - `HybridRegistryRepository` composing YAML source + Postgres cache.
  - Sync strategy: load YAML, diff against DB, apply changes. Drift
    between YAML and DB (someone edited the DB directly) is reported
    as a warning (or failure, configurable).
- CLI command `streamline registry sync` to force a YAML→DB sync
  without starting a batch.
- Documentation: side-by-side comparison of the three registry modes,
  when to use which.

**Deliverable:** Consumers can wire Streamline with any of the three
registry modes (Postgres, YAML, hybrid). Configuration API has
end-to-end tests covering each valid combination. At least one
integration test exercises the full pipeline with YAML-backed
registry + Postgres staging + Postgres destination, proving the
backends compose cleanly.

### Phase 3 — Leaf File Readers (2-3 weeks)

Goal: read real files across the formats needed for migration. One
specialized library per format, each wrapped behind `IFileReader`.

**Library choices** (one per format, each best-in-class for that
format's edge cases — no generic multi-format library):

- `Streamline.Files.Delimited` wraps **CsvHelper** for CSV, TSV, and
  pipe-delimited files. Handles RFC 4180 plus extensive
  non-compliant variants.
- `Streamline.Files.Xlsx` wraps **ClosedXML** for modern Excel (.xlsx).
  OpenXML-based, no Excel install required.
- `Streamline.Files.Json` wraps **System.Text.Json** for JSON (object
  arrays and NDJSON). `Utf8JsonReader` for streaming.
- `Streamline.Files.Xls` wraps **ExcelDataReader** — deferred until the
  first legacy .xls job migrates.
- `Streamline.Files.Xml` wraps **System.Xml** / `XmlReader` — deferred
  until the first XML job migrates.

Each reader:
- Implements `IFileReader` with a typed, format-specific config.
- Accepts both file paths and streams (so it can be invoked by zip
  dispatchers on in-memory entries).
- Is single-pass where possible (headers + rows in one stream).
- Surfaces schema drift as structured `DriftReport` output.
- Has its own focused test suite covering format-specific
  pathologies.

**Format-specific concerns to cover in Phase 3:**

*Delimited:* fixed skip-rows-before-header; find-header-by-pattern;
trailing empty columns; rows shorter or longer than header; footer
detection; mixed line endings; BOM handling; encoding variants
(UTF-8, UTF-16LE, Windows-1252); quoted values with embedded
delimiters and newlines; escaped quotes; empty-string-vs-null
distinction; tolerant-vs-strict row validation.

*Xlsx:* sheet selection strategies (see Domain model section) —
`ByName`, `ByNamePattern`, `ByIndex`, `FirstVisible`, `NamedRange`,
`CombineAllMatching`, `MatchSchema`. Header detection at arbitrary
rows. Skip-columns-before-data. Last-data-column detection. Formula
values vs expressions. Formula errors (`#N/A`, `#REF!`, `#DIV/0!`) —
quarantine, null, or keep-as-string per config. Date epoch
ambiguity (1900 vs 1904). Merged cells in headers. Hidden sheets.

*Json:* object arrays, NDJSON, and nested-array-in-object. Flattening
of nested objects (dot-path column names). Missing-vs-null field
distinction. Inconsistent shapes across records.

*All readers:* stream-based input for dispatcher compatibility;
graceful per-row error recovery; row-index preservation for lineage;
structured error output, not exceptions.

**Deliverable:** `Streamline.Console ingest <directory>` reads CSV,
xlsx, and JSON files from real jobs and stages them in Postgres.
End-to-end happy path works across multiple formats. A test fixture
library of real-world pathological files (gathered from the 10 bespoke
jobs) exercises each reader.

### Phase 3b — Container Dispatchers (1 week)

Goal: handle archives and other containers that bundle multiple
ingestion units into one file. First and only container in v1: zip.

- `IFileDispatcher` interface in `Streamline.Domain.Abstractions`:
  takes a file, yields `IngestionUnit` stream (content stream + entry
  name + suggested mapping + metadata).
- `FileReaderRegistry` extended to detect containers vs leaf files and
  dispatch appropriately. A registry entry's `file_format` determines
  which registry member handles the file.
- `Streamline.Files.Zip` package with `ZipFileDispatcher` implementing
  three dispatch modes:
  - **Route**: open zip, run each entry through Streamline's
    file-mapping resolution as if it had arrived as a separate file.
    Different entries land in different target tables.
    Configuration: `on_unmatched_entry` (ignore / warn / fail),
    `entry_filter` (optional glob).
  - **Combine**: unpack zip, treat all matching entries as rows for one
    target table, optionally tagged with source entry name. Inner
    reader config is inlined (the dispatcher knows the entries are all
    the same format).
  - **Pick**: select one entry by name or pattern, ignore the rest.
- Entry-level file hashing: zip hash tracks zip arrival; entry hashes
  track per-entry duplicates across zips. Both stored in
  `staging.file_log`.
- Stream-based processing: entries read via `ZipArchive.Entry.Open()`,
  streamed directly to readers without extracting to disk.
- Nesting depth cap (default 1, configurable up to 3). Beyond cap →
  fail with clear error.
- Explicit decisions: encrypted zips fail in v1 (no password handling);
  corrupted zips fail the whole file in v1 (no partial processing).
- Test fixture library of pathological zips: empty, single-entry,
  nested, corrupted, mixed-schema, entry-name collisions with existing
  staged files.

**Deliverable:** `Streamline.Console ingest <directory>` handles
directories containing zips. Route mode routes entries to the right
readers automatically. Combine mode produces single-table ingests from
multi-file zips.

### Phase 4 — Operational Features (3 weeks)

Goal: the engine is production-ready, not just functionally complete.
This is the phase where Streamline goes from "loads data" to "runs in
production."

- Drift policy enforcement: `Block`, `Warn`, `Ignore` per
  new-columns / missing-required / missing-optional dimensions. `Block`
  rejects the file during ingestion, before rows are staged.
- FK enforcement modes: `Always`, `WhenParentPopulated`, `Never` per
  FK reference in registry.
- Retry: `RetryBatchCommand` transitions `RolledBack` rows back to
  `Pending` and re-runs `ProcessBatchHandler` for the batch.
- Backfill runbook equivalent baked into a `--reconcile <batch>`
  command: detection queries, dry-run reports, apply inside a
  transaction, verification.
- **Pluggable transformers**:
  - `ITransformerRegistry` with two implementations:
    `SqlFunctionTransformer` (calls registered Postgres functions) and
    `CSharpTransformer` (resolves consumer-registered `ITransformer`
    implementations via DI by fully-qualified type name).
  - Transformer invocation integrated into the processing pipeline:
    after staging + validation, if the registry entry declares a
    transform, the engine invokes it via the registry and records the
    outcome to `staging.transform_log`.
  - Transform failure semantics: a transformer that throws fails the
    batch (rows → `RolledBack`, retryable). A transformer that returns
    partial success (e.g., `RowsFailed > 0`) is surfaced in the outcome
    but does not automatically fail the batch — that's the consumer's
    decision, expressed via the outcome contract.
  - SQL function contract: function takes `p_batch_id TEXT` and returns
    a typed record `(rows_inserted BIGINT, rows_updated BIGINT,
    rows_skipped BIGINT, rows_failed BIGINT, error_message TEXT)`.
  - C# contract: `ITransformer.ExecuteAsync(BatchContext, CancellationToken)`
    returns `TransformOutcome`.
- **Row-level lineage**:
  - `ILineageRepository` with `PostgresLineageRepository`
    implementation.
  - Engine-driven upserts (replication mode) populate lineage
    automatically as part of the upsert path.
  - Transform-driven writes populate lineage by convention: the
    transform must insert into `staging.row_lineage` alongside its
    destination writes. The SQL function contract documents this;
    C# transformers receive a lineage writer in `BatchContext`.
  - A `--lineage <destination_pk>` command traces any destination row
    back to its source file and row index.
- **Built-in row-count reconciliation**:
  - Runs automatically at batch completion.
  - Compares: rows read from files, rows staged, rows validated,
    rows reported by transforms (from `transform_log`), rows in
    `row_lineage`, rows quarantined.
  - Mismatches flag the batch with a reconciliation warning and are
    recorded in `batch_log`.
  - Surfaced in the `--inspect` command output.
- Structured logging: every state transition, every upsert outcome,
  every transform invocation, every quarantine emits a structured
  event with correlation IDs (batch ID, file ID, row ID, transform
  reference).
- OpenTelemetry tracing: spans for ingestion, processing, per-table,
  per-file, per-transform. Off by default, opt-in via config.
- `--inspect <batch>` command: shows batch summary, per-table
  breakdown, transform outcomes, reconciliation result, quarantine
  summary, file outcomes.

**Deliverable:** Streamline handles partial failures gracefully,
supports retry without manual SQL, invokes registered transforms (SQL
or C#), tracks row-level lineage, reconciles row counts automatically,
and produces observable output that an operator can reason about.

### Phase 5 — First Job Migration (2 weeks)

Goal: prove Streamline against real production data by migrating one
bespoke job.

Pick the **simplest replication job** of the 10 existing jobs.
Criteria: file-to-destination mirroring (no transform), single source
file, no FK dependencies, limited volume, low criticality. The goal of
this phase is to surface assumptions Streamline made that don't hold
in reality — not to exercise every feature at once. Transform
pipelines come later, on a proven engine.

- Write the registry entry and file mapping for this job.
- Run Streamline against a copy of the job's input files in a
  non-production schema.
- Compare output row-for-row against the existing job's output in
  production. Use the lineage data to cross-check.
- Document every discrepancy. Each one is either:
  - a Streamline bug (fix it),
  - a correct behavior difference (the bespoke job had a bug or quirk
    Streamline won't replicate — confirm with stakeholders),
  - a registry gap (need a new config option to express the job's
    behavior).
- Cut over once equivalence is proven for one full batch cycle.
  Keep the old job available for 30 days as rollback.

**Deliverable:** One retired bespoke job (replication case). A list of
gaps found (feeds back into engine improvements for Phase 6).

### Phase 6 — Remaining Job Migrations (8-12 weeks, paced)

Goal: retire the remaining 9 jobs, one or two per week.

Order them by increasing complexity and by pipeline type. Migrate at
least two additional replication jobs before attempting the first
transform job — transform failures on an unproven engine are hard to
diagnose. Jobs with FK dependencies come after jobs without. Jobs with
unusual file formats come after standard delimited files. Jobs with
custom transform logic come in the middle-to-late part of the phase,
not at the end (leaving the hardest transforms for last is tempting
but produces schedule risk).

Rough ordering heuristic:
1. 2-3 more replication jobs (fast, exercise the engine).
2. First transform job — pick a simple one (one destination table,
   few mapping joins). This is the real test of the transformer
   integration.
3. Remaining replication jobs and transform jobs mixed in order of
   complexity.

Each migration produces: one retired job, one proven registry entry
(and, for transform jobs, a registered transformer), and potentially
one engine improvement (new column type, new drift policy option, new
reader format). The engine grows more capable with each migration, not
less focused.

Halt migration if three consecutive jobs require engine changes. That's
a sign Streamline is under-designed for the real workload and the
remaining jobs will each require more work than anticipated. Pause,
review, decide whether to continue or fork.

**Deliverable:** 9 additional retired jobs. One engine. One codebase.
One observability story. The operational problem is solved.

### Phase 7 — Extract as Internal NuGet Packages (1 week)

Goal: formalize the library boundary that the solution structure has
been respecting all along.

- Configure each of `Streamline.Core`, `Streamline.Domain`,
  `Streamline.Application`, `Streamline.Infrastructure` to produce a
  NuGet package.
- Internal feed: GitHub Packages, Azure Artifacts, or internal Nexus —
  whichever is already in use.
- CI publishes packages on tagged commits (`v1.0.0`, `v1.0.1`, etc.).
- `Streamline.Console` switches from project references to package
  references. If the Console still works, the packages are real.
- Document versioning policy: stay in `1.x` while the registry schema
  is stable, bump major for breaking registry changes, bump minor for
  new features, bump patch for bugfixes. Honest semver — if a change
  would break a consumer, it's not a patch.
- `CHANGELOG.md` maintained per release.

**Deliverable:** Versioned internal packages. Any team can consume
Streamline by referencing the NuGet feed. External consumers are not
supported (no public docs, no support commitments) but are also not
blocked.

### Phase 8 — Stabilization and 1.0 Release (2 weeks)

Goal: a second team could adopt this without direct hand-holding.
Streamline reaches 1.0.

- Reference documentation for the registry schema: every column,
  every valid value, every constraint, both replication and transform
  variants.
- Operations runbook: how to start a batch, how to retry, how to
  reconcile, how to inspect quarantine, how to resolve quarantine,
  how to trace a destination row back to source.
- Transformer authoring guide: how to write a SQL function transformer
  (contract, return shape, lineage responsibility), how to write a C#
  transformer (implement `ITransformer`, register via DI, use
  `BatchContext`).
- Troubleshooting guide: the 10 most common issues and their fixes.
- Sample registry configs (anonymized versions of migrated jobs) as
  reference for new jobs — including at least two transform examples
  (one SQL, one C#).
- Performance characterization: typical throughput, memory footprint,
  known limits.
- 1.0 release tag on the packages from Phase 7.

**Deliverable:** Streamline is 1.0. Documented. Stable. Done.

### Phase 9 — Custom Reconciliation Framework (post-1.0, 2 weeks)

Goal: extend reconciliation beyond row-count checks to consumer-defined
checks, without turning Streamline into a data quality platform.

This phase is explicitly **post-1.0**. It is planned but not required
for Streamline to be useful. Schedule only after the engine has run
multiple real jobs in production for at least a month and the team has
concrete examples of reconciliation checks they actually need.

- Extend the registry schema to support a `reconciliations:` section
  per entry, declaring check references (SQL function or C# class).
- `IReconciliationCheck` interface with `SqlFunctionReconciliationCheck`
  and `CSharpReconciliationCheck` implementations, following the same
  pluggable pattern as transformers.
- Check invocation integrated into batch completion, after built-in
  row-count reconciliation. Checks receive `BatchContext` and return a
  `ReconciliationOutcome` (pass/fail, message, measured values).
- Per-check `on_failure` policy: `block` (batch is marked failed,
  retryable) or `warn` (batch succeeds, warning logged and surfaced in
  `--inspect`).
- Outcomes recorded in `staging.reconciliation_log`, queryable and
  trend-able over time.
- Documentation + at least one real consumer-defined reconciliation
  check to prove the pattern.

**Explicitly not included in Phase 9**: statistical checks, cross-batch
trend analysis, tolerance thresholds in config, automatic alerting.
Those remain out of scope for Streamline. Custom checks can implement
any logic they want — the engine provides the invocation plumbing, not
the check logic.

**Deliverable:** Streamline 1.1 with custom reconciliation support. The
engine now covers row-count accountability (built-in) and
domain-specific integrity checks (via custom hook), without owning any
check logic itself.

---

## 7. Timeline Summary

| Phase | Duration | Cumulative |
|---|---|---|
| 0. Foundation | 1 week | 1 week |
| 1. Domain + in-memory | 3 weeks | 4 weeks |
| 2. Postgres infrastructure + Postgres registry | 2-3 weeks | 7 weeks |
| 2b. Configuration API + YAML registry | 1-2 weeks | 9 weeks |
| 3. Leaf file readers (CSV + xlsx + JSON) | 2-3 weeks | 12 weeks |
| 3b. Container dispatchers (zip) | 1 week | 13 weeks |
| 4. Operational features (transformers, lineage, built-in reconciliation) | 3 weeks | 16 weeks |
| 5. First replication-job migration | 2 weeks | 18 weeks |
| 6. Remaining migrations | 8-12 weeks | 26-30 weeks |
| 7. Package extraction | 1 week | 27-31 weeks |
| 8. Stabilization + 1.0 release | 2 weeks | 29-33 weeks |
| *(1.0 ships here)* | | |
| 9. Custom reconciliation framework (post-1.0) | 2 weeks | 31-35 weeks |

**Path to 1.0: roughly 7-8 months of focused work for one engineer +
agent.** Realistic calendar time with normal interruptions is closer to
10-12 months. Phase 6 is where estimates leak the most; the first few
migrations are faster than the last few because the last few are the
hardest jobs by design. Format additions discovered during Phase 6 (a
new reader for an unexpected format) can add a week each.

Phase 9 is explicitly post-1.0 and should be scheduled based on real
operational need, not default sequencing. If custom reconciliation
never becomes necessary (row-count reconciliation covers the real use
cases), Phase 9 may never run. That's a successful outcome, not a
failed plan.

---

## 8. Non-Functional Requirements

### 8.1 Performance

- Ingest 100K rows per minute on commodity hardware. Not a hard bound;
  the hot path is COPY into staging, which Postgres handles easily.
- Memory footprint independent of file size (streaming throughout).
- Process 100K rows per minute end-to-end (ingest + validate + upsert)
  on single-threaded happy path. Concurrent processing of independent
  batches if enabled.
- No O(N²) algorithms anywhere in the hot path. Validation per row is
  O(columns); upsert is batched.

### 8.2 Observability

- Every state transition produces a structured log event with batch ID,
  file ID, row ID (where applicable), timestamp, and the transition.
- Metrics: rows ingested, rows quarantined, rows committed, rows rolled
  back, processing duration per table, per-batch totals. Exposed via
  OpenTelemetry.
- Logs are JSON-structured by default. Text format available for local
  development.

### 8.3 Testing Strategy

| Layer | Test type | Runs against |
|---|---|---|
| Core | Unit | Pure .NET |
| Domain | Unit | Fakes / stubs |
| Application | Use-case | In-memory infrastructure fakes |
| Infrastructure | Integration | Testcontainers (Postgres) |
| Integration | End-to-end | Testcontainers + real file fixtures |
| Architecture | Structural | NetArchTest rules |

CI runs all layers on every PR. Integration tests run on a schedule if
per-PR runtime is too slow.

### 8.4 Security

- No secrets in source. All connection strings, credentials via
  `appsettings` + user secrets locally, environment variables in
  deployment.
- SQL injection: all Postgres access uses parameterized queries via
  Npgsql. No dynamic SQL concatenation in the hot path. The one
  exception is the dynamic upsert SQL generation, which uses identifier
  quoting (`NpgsqlParameterCollection` for values; `"schema"."table"`
  quoting for identifiers) and validates identifier names against a
  safe-character regex.
- Registry data is trusted input (owned by the same team that owns the
  engine). Source file content is untrusted — validated per row, never
  concatenated into SQL.

### 8.5 Deployment

- `Streamline.Console` produces a self-contained .NET executable
  (targeting .NET 10).
- Deployment is "copy the binary plus appsettings, point at a
  database." No runtime dependencies on .NET being installed.
- Migrations run on startup if the database is behind. Controlled by
  a config flag so production deployments can run migrations manually.

---

## 9. Risks and Mitigations

**Risk: Phase 6 migrations reveal fundamental engine assumptions are
wrong.** The 10th job turns out to need something the architecture
can't express without a rewrite. **Mitigation:** halt-on-three-engine-
changes rule. Migrate in order of increasing complexity so the
expensive surprises surface earlier. Accept that some jobs may not
fit and stay bespoke — not every ETL job needs to run on Streamline.

**Risk: State machine has edge cases that only surface under real
concurrent load.** Two `--process` calls racing, a transaction timing
out during commit, an advisory lock held by a crashed process.
**Mitigation:** Phase 1 test suite exhaustively covers state
transitions; Phase 2 adds real Postgres-level concurrency tests using
multiple Testcontainer-based worker processes. Budget an extra week
of polish between Phase 2 and Phase 5 for concurrency issues found
in integration.

**Risk: DDD layering becomes cargo-cult.** Classes get split across
layers to satisfy the rule rather than because the split makes sense.
`Streamline.Domain` ends up with thin anaemic types that are just
DTOs with extra steps. **Mitigation:** the domain layer owns the state
machine and the validation rules. Those are genuinely rich domain
logic. If a type has no behavior and is just data, it belongs in Core
or as a DTO in Application, not Domain. Review layer contents quarterly
and push types down if they don't justify Domain placement.

**Risk: A reader library becomes a bottleneck or gets abandoned.**
**Mitigation:** Each reader library (CsvHelper, ClosedXML, System.Text.Json,
etc.) is wrapped behind `IFileReader` in its own per-format project.
Replacement is a single-project swap against the same interface. No
reader library is depended on outside its wrapping project.

**Risk: The registry schema needs frequent breaking changes during
Phase 6.** Jobs migrated early have registry entries written against
an old schema; each breaking change requires updating all prior
entries. **Mitigation:** stay in `0.x` versioning throughout Phase 6.
Registry schema migrations run automatically on startup. Each new
migration updates all existing entries to the new shape where possible;
irresolvable cases fail the migration loudly so operators can fix them
manually.

**Risk: Time estimate is optimistic.** Everyone's is. **Mitigation:**
each phase has a working deliverable. If time runs out mid-project,
the engine works for whatever has been migrated so far; remaining
bespoke jobs stay bespoke. The refactor plan for the current system
remains a viable fallback if Streamline runs out of runway.

---

## 10. What Changes from the Previous Plan

This plan is ground-up. The previous plan was about refactoring the
in-flight project. Key differences:

- No Phase 0 "steal from yourself" — this is a clean build, though the
  design work (state model, registry schema, staging model, runbook
  patterns) from the refactor plan is directly reusable.
- No cutover phase for the existing in-flight project. That project
  either gets retired (replaced by Streamline) or continues for its
  current single use case while Streamline takes over new jobs.
- Plugin architecture is explicitly deferred. No `ISourcePlugin`
  interface, no plugin registry. Concrete readers behind `IFileReader`,
  which is a much narrower contract.
- No destination abstraction beyond `IDestinationAdapter` with a single
  Postgres implementation. Multi-database support is a v2 concern, not
  v1.
- Package extraction is late (Phase 7), after value is proven.

## 11. Decisions Already Baked In

These are settled. Revisiting them changes the plan materially; if
something here turns out wrong, we replan, we don't drift.

1. **Pluggable storage backends, Postgres as first-party default.** The
   engine defines `IRegistryRepository`, `IFileMappingRepository`,
   `IStagingRepository`, and `IDestinationAdapter` as contracts chosen
   by the consumer at DI registration. v1 ships first-party
   implementations: Postgres for all four, plus YAML and hybrid
   (YAML-source-with-Postgres-cache) for registry and file mapping.
   Other backends (SQL Server, flat files, etc.) are supported via the
   interfaces but are the consumer's responsibility to implement and
   maintain. The engine treats them as unsupported unless adopted
   first-party.
2. **DDD layering enforced by architecture tests.** Not a style
   preference; a build-breaking rule.
3. **`Streamline.Console` is a project reference until Phase 7.** No
   early package publication.
4. **One specialized library per format, each wrapped behind
   `IFileReader` or `IFileDispatcher`.** Initial set: CsvHelper
   (delimited), ClosedXML (.xlsx), System.Text.Json (JSON). Added on
   demand during Phase 6: ExcelDataReader (.xls), System.Xml (XML),
   Parquet.Net (Parquet), etc. Each reader is its own project and its
   own NuGet package at Phase 7. Generic multi-format libraries (Cinchoo
   or equivalents) are **not** used — specialized libraries give better
   control over format-specific pathologies.
5. **Retry is first-class.** The state machine supports
   `RolledBack → Pending` from day one, not as a later addition.
6. **Structural validation only.** No expression language, no computed
   columns, no semantic quality checks.
7. **Migration order: simplest to most complex, replication before
   transform.** Jobs are picked by the engine team, not the job teams,
   based on engine-readiness.
8. **Pluggable transformers from day one, two initial kinds: SQL
   function and C# class.** The engine orchestrates; the consumer owns
   transform logic. No third kind added speculatively; a new kind is
   only introduced when a real job needs it.
9. **Transform is opt-in per registry entry.** Replication pipelines
   (no transform) are the default and primary path. The presence of a
   `transform:` section in the registry entry is what switches modes.
10. **Row-level lineage is mandatory.** Every destination row produced
    by the engine or a transform must have a corresponding lineage
    record. The engine enforces this for its own writes and documents
    the convention for transform-driven writes.
11. **Built-in row-count reconciliation is automatic and
    non-configurable in v1.** It always runs at batch completion. If
    the consumer doesn't want it, they ignore the outcome — it doesn't
    block anything unless counts genuinely don't match.
12. **Custom reconciliation is post-1.0 (Phase 9), not v1.** Pluggable
    check framework is deferred until the engine is proven in
    production on multiple jobs.

## 12. Open Questions

These need answers before Phase 1 starts.

1. **Multi-database staging.** Can two jobs share one staging database
   safely if they have different destination databases? Current
   assumption: yes, the `batch_id` namespacing is sufficient. Confirm
   before Phase 2.
2. **Migration framework choice.** FluentMigrator vs hand-rolled vs
   EF Core migrations. Default: FluentMigrator for its simplicity.
   Note: Phase 2 splits migrations across two packages
   (`Streamline.Infrastructure` and `Streamline.Registry.Postgres`); the
   framework needs to support composable migration sources.
3. **CLI framework choice.** `System.CommandLine` vs `Spectre.Console`
   vs hand-rolled. Default: `System.CommandLine` (official Microsoft
   library, even if its API is still stabilizing).
4. **Logging sink.** Serilog vs `Microsoft.Extensions.Logging` default
   providers. Default: Serilog with structured JSON output.
5. **YAML library.** `YamlDotNet` is the de facto default. Confirm
   before Phase 2b.

---

## 13. Appendix A — Example Registry Entry: Replication Case

The most common case. Source file maps directly to destination table;
the engine performs the upsert. No transform.

```yaml
# schema_registry/broker.yaml
table_name: broker
target_schema: intembeko
valid_from: 2024-01-01
valid_to: null
is_active: true
primary_key_columns: [broker_code, rule_effective_date]
depends_on: []
drift_policy:
  new_columns: warn
  missing_required_columns: block
  missing_optional_columns: warn
column_mappings:
  BrokerCode:
    target: broker_code
    type: string
    max_length: 12
    required: true
  RuleEffectiveDate:
    target: rule_effective_date
    type: date
    required: true
  LatestRuleIndicator:
    target: latest_rule_indicator
    type: string
    max_length: 1
    pattern: ^[YN]$
  BrokerName:
    target: broker_name
    type: string
    max_length: 200
  BrokerTypeCode:
    target: broker_type_code
    type: string
    max_length: 2
```

## 13b. Appendix A' — Example Registry Entry: Transform Case (SQL)

Source file is reshaped into a master table by a registered SQL
function. The engine validates, stages, and invokes the function;
the function owns the write to the destination.

```yaml
# schema_registry/broker_provider_a.yaml
table_name: broker_provider_a_raw
valid_from: 2024-01-01
is_active: true
drift_policy:
  new_columns: warn
  missing_required_columns: block
  missing_optional_columns: warn
source_columns:                   # validated only; no target mapping
  BrokerCode:
    source_type: string
    max_length: 12
    required: true
  ProviderTypeCode:
    source_type: string
    required: true
  EffectiveDate:
    source_type: date
    required: true
  BrokerName:
    source_type: string
    max_length: 200
transform:
  kind: sql_function
  reference: domain.transform_broker_from_provider_a_v3
  destination_table: intembeko.dim_broker
  invocation: per_batch
```

## 13c. Appendix A'' — Example Registry Entry: Transform Case (C#)

Same shape, but the transform is a C# class implementing `ITransformer`,
resolved via DI by fully-qualified type name.

```yaml
# schema_registry/broker_provider_b.yaml
table_name: broker_provider_b_raw
valid_from: 2024-01-01
is_active: true
source_columns:
  BrokerRef:
    source_type: string
    required: true
  RegionCode:
    source_type: string
    max_length: 3
    required: true
  # ...
transform:
  kind: csharp
  reference: YourDomain.Transforms.BrokerProviderBTransformer, YourDomain.Transforms
  destination_table: intembeko.dim_broker
  invocation: per_batch
```

## 14. Appendix B — Example File Mappings

Four examples showing the shape of format-specific config.

### B.1 Delimited (CSV / pipe-delimited)

```yaml
# file_mapping/broker.yaml
file_pattern: Broker[0-9]+\.txt
target_table: broker
file_format: delimited
file_date_extractor: trailing_timestamp   # regex (\d{14}|\d{8}) before extension
delimited_config:
  delimiter: "|"
  quote_character: '"'
  escape_character: '"'
  encoding: UTF-8
  has_header: true
  header_row_strategy: fixed              # or: find_by_pattern
  skip_rows_before_header: 0
  trailing_empty_columns: trim            # or: keep
  row_validation: tolerant                # or: strict
  empty_string_handling: null             # empty string → null
  null_markers: ["", "\\N", "NULL"]
```

### B.2 Excel (.xlsx) with sheet selection

```yaml
# file_mapping/monthly_brokers.yaml
file_pattern: Monthly_Brokers_[0-9]+\.xlsx
target_table: broker
file_format: xlsx
xlsx_config:
  sheet_selector:
    kind: by_name                         # or: by_name_pattern, by_index,
                                          # first_visible, named_range,
                                          # combine_all_matching, match_schema
    name: "Data"
  header_row: 5                           # 1-indexed, after skipping preamble
  data_starts_at_column: 2                # skip the first (index) column
  last_data_column: auto                  # or: explicit letter "Z"
  footer_detection:
    kind: trailing_totals                 # drop rows after a marker
    marker_pattern: "^Total"
  formula_errors: quarantine              # or: null, keep_string
  date_epoch: auto                        # or: 1900, 1904
```

### B.3 Excel with schema-discovery (multi-sheet workbook, unknown sheet name)

```yaml
# file_mapping/bank_export.yaml
file_pattern: BankExport_.*\.xlsx
target_table: broker_bank
file_format: xlsx
xlsx_config:
  sheet_selector:
    kind: match_schema
    registry_entry: broker_bank           # engine picks the sheet whose
                                          # headers match this registry entry
    on_no_match: fail
    on_multiple_matches: fail
  header_row: 1
```

### B.4 Zip container in route mode

```yaml
# file_mapping/daily_bundle.yaml
file_pattern: DailyData_[0-9]+\.zip
file_format: zip
zip_config:
  mode: route                             # each entry → its own file mapping
  on_unmatched_entry: warn                # or: ignore, fail
  entry_filter: "*.txt"                   # optional — only matching entries
  on_empty_zip: fail
  nesting_depth_cap: 1
```

### B.5 Zip container in combine mode

```yaml
# file_mapping/weekly_brokers.yaml
file_pattern: WeeklyBrokers_[0-9]+\.zip
target_table: broker
file_format: zip
zip_config:
  mode: combine
  entry_pattern: "^Broker_[0-9]+\\.txt$"
  entry_order: by_name                    # or: by_name_pattern_group, as_stored
  on_column_mismatch: fail                # all entries must have same headers
  add_source_entry_column: true
  source_entry_column: source_entry
  inner_reader: delimited
  delimited_config:                       # applied to every matching entry
    delimiter: "|"
    has_header: true
    encoding: UTF-8
```

## 15. Appendix C — Key Domain Interfaces (sketch, not final)

```csharp
// Streamline.Domain.Abstractions

public interface IRegistryRepository
{
    Task<RegistryEntry?> GetActiveAsync(string tableName, CancellationToken ct);
    Task<IReadOnlyList<RegistryEntry>> GetAllActiveAsync(CancellationToken ct);
    Task UpsertAsync(RegistryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<string>> GetLoadOrderAsync(CancellationToken ct);
    // First-party implementations (v1): Postgres, YAML, Hybrid.
    // Consumer-implemented backends are permitted but unsupported.
}

public interface IFileMappingRepository
{
    Task<FileMapping?> ResolveAsync(string fileName, CancellationToken ct);
    Task<IReadOnlyList<FileMapping>> GetAllActiveAsync(CancellationToken ct);
    Task UpsertAsync(FileMapping mapping, CancellationToken ct);
    // Separate from IRegistryRepository so registry and file-mapping
    // storage can be chosen independently at registration time.
}

public interface IStagingRepository
{
    Task<BatchId> StartBatchAsync(string source, CancellationToken ct);
    Task BulkInsertIncomingAsync(BatchId batch, IAsyncEnumerable<Record> rows, string targetTable, CancellationToken ct);
    IAsyncEnumerable<StagedRow> GetPendingRowsAsync(BatchId batch, string targetTable, int limit, CancellationToken ct);
    Task TransitionAsync(long rowId, RowStatus from, RowStatus to, CancellationToken ct);
    Task QuarantineAsync(long rowId, string errorCode, string errorMessage, CancellationToken ct);
    Task ResetForRetryAsync(BatchId batch, CancellationToken ct);
    // ...
}

public interface IDestinationAdapter
{
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct);
}

public interface ITransactionScope : IAsyncDisposable
{
    Task<INestedScope> BeginNestedScopeAsync(string name, CancellationToken ct);
    Task<UpsertOutcome> UpsertAsync(string schema, string table, SchemaDefinition schema, IReadOnlyList<Record> rows, CancellationToken ct);
    Task<IReadOnlyCollection<string>> GetDistinctColumnValuesAsync(string schema, string table, string column, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

public interface INestedScope : IAsyncDisposable
{
    Task ReleaseAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

public interface IFileReader
{
    bool CanRead(FileMapping mapping, string filePath);
    Task<IReadOnlyList<string>> GetHeadersAsync(FileMapping mapping, string filePath, CancellationToken ct);
    IAsyncEnumerable<Record> ReadRowsAsync(FileMapping mapping, string filePath, CancellationToken ct);

    // Stream overloads — invoked by dispatchers on in-memory entries
    // (e.g., a zip dispatcher streaming each entry without extracting to disk).
    Task<IReadOnlyList<string>> GetHeadersAsync(FileMapping mapping, Stream content, string logicalName, CancellationToken ct);
    IAsyncEnumerable<Record> ReadRowsAsync(FileMapping mapping, Stream content, string logicalName, CancellationToken ct);
}

public record IngestionUnit(
    Stream Content,
    string EntryName,
    FileMapping? SuggestedMapping,  // null in route mode → engine resolves fresh
    IReadOnlyDictionary<string, object> Metadata);

public interface IFileDispatcher
{
    bool CanDispatch(FileMapping mapping, string filePath);
    IAsyncEnumerable<IngestionUnit> DispatchAsync(FileMapping mapping, string filePath, CancellationToken ct);
}

// Streamline.Domain.Transforms

public record TransformReference(
    string Kind,                    // "sql_function" or "csharp"
    string Reference,               // function name or FQ type name
    string DestinationTable,
    string Invocation);             // "per_batch" (v1) — per_row deferred

public record TransformOutcome(
    long RowsInserted,
    long RowsUpdated,
    long RowsSkipped,
    long RowsFailed,
    TimeSpan Duration,
    string? ErrorMessage);

public record BatchContext(
    BatchId BatchId,
    IReadOnlyList<long> FileLogIds,
    string SourceSchema,            // staging
    ILineageWriter Lineage);        // passed so C# transformers can record lineage

public interface ITransformer  // consumers implement this for C# transforms
{
    Task<TransformOutcome> ExecuteAsync(BatchContext context, CancellationToken ct);
}

public interface ITransformerRegistry
{
    // Dispatches to SqlFunctionTransformer or CSharpTransformer based on reference.Kind
    Task<TransformOutcome> InvokeAsync(TransformReference reference, BatchContext context, CancellationToken ct);
}

// Streamline.Domain.Lineage

public record RowLineage(
    long IncomingId,
    string DestinationSchema,
    string DestinationTable,
    IReadOnlyList<object> DestinationPkValues);

public interface ILineageRepository
{
    Task RecordAsync(IEnumerable<RowLineage> lineages, CancellationToken ct);
    IAsyncEnumerable<RowLineage> GetForSourceRowAsync(long incomingId, CancellationToken ct);
    IAsyncEnumerable<RowLineage> GetForDestinationAsync(string schema, string table, IReadOnlyList<object> pkValues, CancellationToken ct);
}

public interface ILineageWriter  // passed to C# transformers via BatchContext
{
    Task RecordAsync(IEnumerable<RowLineage> lineages, CancellationToken ct);
}

// Streamline.Domain.Reconciliation

public record ReconciliationOutcome(
    string CheckName,
    bool Passed,
    string? Message,
    IReadOnlyDictionary<string, decimal> MeasuredValues);

// Phase 9 only — not part of v1
public interface IReconciliationCheck
{
    Task<ReconciliationOutcome> RunAsync(BatchContext context, CancellationToken ct);
}
```

These are shapes, not final signatures. Phase 1 will refine the core
interfaces (Staging, Destination, FileReader, Transformer, Lineage) as
the state machine is implemented and the actual usage patterns emerge.
`IReconciliationCheck` is included for reference but not implemented
until Phase 9.
