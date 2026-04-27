# STANDARDS.md — the rules, indexed

> Flat index of every load-bearing standard. Each entry: rule (one
> sentence), rationale (one sentence), authoritative source (file
> pointer). Scannable in 5 minutes. If you want depth, follow the
> pointer.

## Index

> Anchor list of every entry by category. A reader looking for a
> specific standard should find it here in 30 seconds and follow the
> link to the entry below.

## 1. Code organization

> Layering rule (Core → Domain → Application → Infrastructure → Console).
> Project structure. File placement. Pointers to plan §5.2 and the
> Architecture.Tests project.

## 2. Testing

> Test categories (unit, integration, regression, architecture). Coverage
> expectations. Per-class fixture conventions. Contract-parity bar.
> Perturb-recover discipline. The `ForTestingOnly_` pattern. Pointers to
> TESTING.md and `tests/` exemplar files.

## 3. Commits and PRs

> Conventional commit format. One logical change per commit. Design
> fixes get separate `feat()` commits, never hidden inside test commits.
> Sub-phase commit numbering. Pointers to WORKFLOW.md.

## 4. Architecture enforcement

> NetArchTest layering rules. csproj-XML inspection for empty assemblies.
> When to add a new architecture rule. Pointers to the Architecture.Tests
> project.

## 5. Documentation

> When to update PARKED.md. When to update the project plan. When a new
> doc is warranted. Cross-reference discipline (don't duplicate; point
> to the authoritative source). Pointers to WORKFLOW.md and the four
> handover docs.

## 6. Observability

> Observation severity assignment rules. Emission discipline (orchestrators
> emit, services don't). The catalog as wire-protocol — codes are stable.
> The publisher pattern (`DomainEventPublisher`). Pointers to
> OBSERVATIONS.md.

## 7. Process

> Plan-confirm-implement-test-commit cadence. Parked-decisions tracking.
> Context-awareness as default mode. Standing report format. Stop at
> sub-phase boundaries. The 30-minute tooling-fight rule. Pointers to
> WORKFLOW.md and HANDOVER.md.

## 8. Tooling

> Analyzer baseline. Suppression at smallest scope with rationale, never
> globally. Central Package Management. Test-runner risk patterns
> (`IAsyncEnumerable`, cancellation, ordering). Pointers to specific
> source files where each pattern is exemplified.
