# Synthetic xlsx fixtures

Deliberately-pathological Excel workbooks for sheet-selector and
reader edge cases in `Streamline.Files.Xlsx`.

**Populated in Phase 3.** Empty today.

Intended coverage:

- Sheet selection: `ByName`, `ByNamePattern`, `ByIndex`,
  `FirstVisible`, `NamedRange`, `CombineAllMatching`, `MatchSchema`.
- Header detection at arbitrary rows.
- Skip-columns-before-data.
- Last-data-column detection.
- Formula values vs expressions.
- Formula errors (`#N/A`, `#REF!`, `#DIV/0!`).
- Date epoch ambiguity (1900 vs 1904).
- Merged cells in headers.
- Hidden sheets.

Add one line here per fixture describing the pathology and the test
that references it.
