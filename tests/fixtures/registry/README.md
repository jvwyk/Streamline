# Sample registry YAML fixtures

Example YAML registry entries used by tests. Covers replication and
transform modes, drift and FK policy variations, and valid-from /
valid-to versioning.

**Populated in Phase 2b** (when the YAML registry lands). Empty today.

Examples intended:

- Minimal replication entry (one source, one destination, no FK).
- Replication entry with FK references under each enforcement mode.
- Replication entry with all drift-policy variations.
- Versioned entry with `valid_from` / `valid_to`.
- Transform entry with a SQL-function transformer reference.
- Transform entry with a C#-class transformer reference.

Anonymised/synthesised versions of migrated real jobs land in
`docs/` (per plan §8) as reference samples — not here. The fixtures
in this folder are purpose-built for tests, not documentation.

Add one line per file describing what it exercises and which test
references it.
