# HANDOVER.md — working knowledge for agents continuing Streamline

> **Living document.** Updated as patterns evolve. Read in priority order;
> the load-bearing parts (sections 1–3) are in the first ~200 lines.
>
> The four-doc handover stack is `ONBOARDING.md` (your first session),
> `HANDOVER.md` (this file — working knowledge), `STANDARDS.md` (the rules,
> indexed), and `PHASE-N-CLOSEOUT.md` (frozen artifacts of each phase). This
> document is the only living one of the four.

## How to read this document

> Brief preface: priority ordering rationale; what's load-bearing vs.
> reference; when to update this doc; how it differs from the authoritative
> docs.

---

## 1. The disciplines (the rhythm)

> The plan-confirm-implement-test-commit cadence and what each step looks
> like in practice. Plan structure (confirmations on user leans, additional
> Qs, push-backs with reasoning). The "stop and ask" vs. "stop and report"
> distinction. The 30-minute tooling-fight rule. Stop at sub-phase
> boundaries; the 1f mid-phase pause example. Reading user prompts twice
> before planning. Sub-phase reports in standing format (8 sections per
> WORKFLOW.md Appendix A). Parked decisions discipline. Contract-not-artifact
> assertion bar. Perturb-recover for load-bearing tests. Context-awareness
> as a default mode. Honest "Surprises during implementation" — design
> changes get separate `feat()` commits, never hidden in test commits.

## 2. How the user works

> Plans confirmed before code. Pushback rewarded; sometimes user accepts,
> sometimes user wins; both outcomes fine. Surfacing rewarded; silent
> decisions aren't. Parked decisions get explicit phase ownership. The user
> catches operational risks; the agent catches technical ones. Reports use
> the standing format. "Don't perform; be honest about what surprised
> you." Stop at sub-phase boundaries.

## 3. Specific warnings to the successor

> Phase 4 absorbed cluster (P-3, P-6, P-7, P-8, P-10) — plan against the
> full set, not piecemeal. P-11 is the highest-risk parked decision; Phase 2
> must address it as a first-tier design question. The "test passes but
> design is wrong" failure mode in integration tests; perturb-recover
> catches it. Operational risks dominate technical ones from Phase 2
> forward. The fakes' "no auto-advance batch status across handlers" gap
> is a Phase 2 first-tier design surface (P-11). `BatchStateMachine` has
> no `Ingesting → Failed` transition (P-7); recovery from a stuck Ingesting
> batch needs Phase 4's `MarkBatchFailedCommand`.

---

## 4. Architectural tacit reasoning

> DDD layering as deliberate inverse of predecessor's coupling. Observation
> taxonomy as the answer to predecessor's silent failures. Per-batch
> `FkResolver` lifetime preventing PB-1 cache leak. Contract-parity
> discipline (in-memory fakes mirror Postgres exactly) as the safety net for
> Phase 2. Per-table savepoint isolation. Aggregate-as-policy-gate (not row
> store). Throw-on-illegal state machines. **The "Phase 4 will absorb this"
> pattern as deliberate scope discipline.**

## 5. If you find yourself wanting X, the answer is usually Y

> Shortcut signals — when the successor recognizes the want, they recognize
> the answer. Adding a Rows collection to the Batch aggregate "for testing
> convenience" → use `ForTestingOnly_AllRows` on the staging fake instead.
> Making a fake more flexible (computed outcomes, configurable behavior) →
> simplest thing that works for v1; add flexibility when a real test needs
> it. Retrofitting observation emission into a method → orchestrator emits,
> not the method. Silently absorbing an analyzer warning → suppress with
> rationale at the smallest scope, never globally. Plus others surfaced
> over Phase 1.

## 6. Concrete context-awareness examples

> FluentAssertions → AwesomeAssertions (Phase 0 license issue surfaced
> before code committed). Bulk FK preload bug discovery (1h commit 6).
> TRANSFORM_MODE_DEFERRED rename (1f-ii follow-up). InMemoryFileReaderRegistry
> gap (1g missed it; 1h surfaced). Phase 4 cluster framing (1i close-out).
> Each shows the pattern: noticed → surfaced → resolved.

## 7. Rejected decisions, with reasoning

> Eight load-bearing rejections from Phase 1: boolean parsing strict vs.
> broad; cross-fake state coordination shared vs. independent; state-machine
> placement inline vs. separate folder; PerBatchScope raw constructor vs.
> static factory; bulk vs. lazy FK preload; centralized vs. per-assembly
> meta-test; ScriptedTransformer Func vs. list; mid-phase renumbering vs.
> 1f-i / 1f-ii split.

## 8. Debug gotchas

> Seven debugs that cost real time and shouldn't repeat: FakeTimeProvider
> deadlock with xUnit v3 + NSubstitute; `[EnumeratorCancellation]` CS8424
> on non-async-iterator wrappers; CA1707 / CA1720 / CA1848 protocol-
> identifier suppression pattern; csproj-XML + NetArchTest hybrid for
> empty-assembly architecture rules; `[PreventsPredecessorBug]` cross-
> assembly attribute duplication; `xUnit.Record` name collision with
> `Streamline.Core.ValueTypes.Record`; CA2241 false positive on curly
> braces in `becauseArg` strings.

## 9. Phase 1 history (compressed)

> The 11 sub-phases (1a–1i) with one-paragraph each. Predecessor-system
> context. Why Phase 0's deliberate setup mattered. Anchoring for the
> timeline references in earlier sections.
