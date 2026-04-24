# Synthetic delimited fixtures

Small, focused, deliberately-pathological CSV/TSV/pipe files written
to exercise specific edge cases in `Streamline.Files.Delimited`.

**Populated in Phase 3.** Empty today.

Intended coverage (each gets its own file, named for the pathology):

- Fixed skip-rows-before-header
- Find-header-by-pattern
- Trailing empty columns
- Rows shorter or longer than header
- Footer detection
- Mixed line endings (CRLF / LF / mixed)
- BOM handling (UTF-8, UTF-16LE)
- Encoding variants (UTF-8, Windows-1252)
- Quoted values with embedded delimiters
- Quoted values with embedded newlines
- Escaped quotes (doubled `""` and `\"`)
- Empty-string-vs-null distinction
- Tolerant vs strict row validation

Add one line here per fixture describing the pathology and the test
that references it.
