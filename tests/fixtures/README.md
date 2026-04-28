# Test Fixtures

Input files used by tests. See [`docs/TESTING.md`](../../docs/TESTING.md)
section "Test Fixtures" for the rules.

## Layout

```
fixtures/
  files/
    delimited/   CSV, TSV, pipe-delimited
    xlsx/        Modern Excel
    json/        Object arrays and NDJSON
    zip/         Containers
  registry/      Sample YAML registry entries
```

Each leaf folder splits into `real/` and `synthetic/`:

- **`real/`** — samples from actual bespoke jobs we're migrating.
  Must be redacted or synthesised-equivalent if they contain PII or
  production credentials.
- **`synthetic/`** — edge cases written for testing. Small, focused,
  pathological by design.

## Rules

- Every fixture lives under a folder with a README explaining what
  makes it pathological and which test uses it. Fixtures without a
  README are orphans — delete them.
- **Never commit PII or production credentials.** If a real sample
  contains sensitive data, redact it before committing or derive a
  synthetic equivalent.
- Fixture file names should describe the pathology, not the source
  (`header_with_skip_rows_3.csv`, not `broker_2024_07.csv`).

## Population schedule

| Folder | Phase |
|---|---|
| `files/delimited/` | Phase 3 |
| `files/xlsx/` | Phase 3 |
| `files/json/` | Phase 3 |
| `files/zip/` | Phase 3b |
| `registry/` | Phase 2b |

Phase 0 creates the skeleton so the folders are discoverable from day
one — empty folders rot less when they have a README naming their
future contents.
