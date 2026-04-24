# Synthetic zip fixtures

Deliberately-pathological zip archives for the container dispatcher.

**Populated in Phase 3b.** Empty today.

Intended coverage:

- Empty zip (no entries)
- Single-entry zip
- Multi-entry zip routed to different target tables (Route mode)
- Multi-entry zip combined into one table (Combine mode)
- Multi-entry zip with a pick pattern (Pick mode)
- Nested zip (within depth cap)
- Nested zip (exceeding depth cap)
- Corrupted zip
- Entry with mixed schema against the registry
- Entry-name collision with an existing staged file
- Encrypted zip (expected to fail per v1 policy)

Add one line here per fixture describing the pathology and the test
that references it.
