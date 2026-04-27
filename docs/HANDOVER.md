# HANDOVER.md — working knowledge for agents continuing Streamline

> **Living document.** Updated as patterns evolve. Read in priority order;
> the load-bearing parts (sections 1–3) are in the first ~200 lines.
>
> The four-doc handover stack is `ONBOARDING.md` (your first session),
> `HANDOVER.md` (this file — working knowledge), `STANDARDS.md` (the rules,
> indexed), and `PHASE-N-CLOSEOUT.md` (frozen artifacts of each phase).
> This is the only living one of the four.

## How to read this document

This is the doc one agent writes to the next. It's the working knowledge
that produces Phase 1's quality, not the formal rules that the
authoritative docs already capture. Authoritative homes are
`AGENTS.md` (operating principles), `WORKFLOW.md` (rhythm and report
format), `TESTING.md` (test discipline), `OBSERVATIONS.md` (catalog and
emission rules), `PARKED.md` (deferred decisions), `streamline-plan.md`
(project plan). When this doc says "the rule is X," it's a summary; the
authoritative source is wherever the link points.

The priority ordering — disciplines → how the user works → specific
warnings → architectural reasoning → shortcut signals → examples →
rejected decisions → debug gotchas → history — is what a successor
needs in roughly that sequence. The load-bearing parts (1–3) come first
because they shape every session; the reference material (7–9) is
where you go when you hit something specific.

Update this doc when a new pattern emerges that future agents would
benefit from inheriting. Don't update it for one-off events; this is
working knowledge, not a changelog.

---

## 1. The disciplines (the rhythm)

### The plan-confirm-implement-test-commit cadence

Every sub-phase opens with a user prompt. The agent reads the prompt
twice (see "Read prompts twice" below), produces a plan, surfaces
design questions, waits for the user's confirmation, then implements
per the rhythm. The cadence has five phases:

1. **Plan.** Read the user's prompt twice. Identify the explicit ask
   (what gets built) and the framing (how it should be built — the
   discipline expectations). Produce a plan that confirms the user's
   leans, surfaces additional design questions, pushes back on items
   you disagree with (with reasoning), and proposes a commit plan.
2. **Confirm.** The user reviews. They confirm, push back, accept your
   push-backs, or surface things you missed. Iterate until both sides
   are aligned.
3. **Implement.** Write production code. Test the changes locally
   (build, test, verify warnings stay at zero). Use existing patterns;
   don't invent new ones unless the existing patterns don't fit.
4. **Test.** Tests are required for every production change. The bar
   is contract-not-artifact (see below): each test asserts at least
   one contract behavior the docs document. Perturb-recover load-
   bearing tests by deliberately breaking the production code, running
   the test, verifying it fails with a useful assertion message, then
   restoring.
5. **Commit.** One logical change per commit. Conventional commit
   format. Sub-phase commit numbering ("Sub-phase X commit N of M") so
   the report at the end can reference them by number. Design fixes
   that surface during testing land as separate `feat()` commits, not
   hidden inside the test commit that surfaced them.

The cadence is the same for every sub-phase. A successor who follows
it produces Phase 1-quality work; a successor who skips steps produces
work that has to be rewritten.

### Plan structure

Plans have a consistent structure that the user can review quickly:

1. **Confirmations on user leans.** The user usually surfaces design
   questions with their own preferences ("My lean: option (b)").
   The plan confirms each one explicitly ("Q1 — confirmed. Reasoning:
   ...") even when the agent agrees. Silent agreement is harder to
   review than explicit confirmation.
2. **Additional questions.** Things the user didn't surface but that
   matter. Numbered sequentially (Q5, Q6, ...) and surfaced with the
   agent's lean and reasoning.
3. **Push-backs.** Items the agent disagrees with the user on, with
   reasoning. The push-back is welcomed; sometimes the user accepts,
   sometimes the user wins; both outcomes are fine. What matters is
   that the disagreement is surfaced rather than silently absorbed
   (in either direction).
4. **Commit plan.** Numbered list of commits with one-sentence
   descriptions. "N commits within the user's M-commit budget" if a
   budget was specified.
5. **"Standing by" signal.** Explicit statement that the agent waits
   for confirmation before implementing.

### The "stop and ask" vs. "stop and report" distinction

These are operationally different and the successor needs to know
which is which:

- **Stop and ask.** You've hit a design question that needs the user's
  input before you can proceed correctly. Write the question with
  context, your lean, and what's blocking. The user replies; you
  continue. Common during planning rounds; rare during implementation
  (if you're hitting design questions mid-implementation, the plan was
  underspecified — surface that).
- **Stop and report.** You've completed a sub-phase boundary (or a
  substantial unit of work mid-phase, like the 1f split). Produce a
  report using the standing format. The user reviews; the next prompt
  opens (or doesn't, if the user has follow-ups). The report ends the
  current work and waits for the next start.

Stop-and-ask interrupts the user mid-plan; stop-and-report ends the
sub-phase. They look similar (both pause and wait for the user) but
the user reads them differently. Use the right one.

### The 30-minute tooling-fight rule

Surfaced during 1f-i with `FakeTimeProvider`, restated in 1f-ii and 1g
prompts. **If a tooling issue costs more than 30 minutes of debug
time, fall back to a workaround and surface as a parked decision.**

The successor has explicit permission to work around tooling rather
than fight it. Examples from Phase 1:

- `FakeTimeProvider` + xUnit v3 + NSubstitute hung `dotnet test`
  indefinitely. After multiple stuck processes accumulated, the agent
  fell back to an explicit `IReadOnlyList<TimeSpan>` backoff schedule
  and parked the `TimeProvider` design as P-5.
- The `[EnumeratorCancellation]` CS8424 wrapper trap (see §8) cost
  one debug cycle but hit the 30-minute rule's spirit — workaround
  was inlining the iterator rather than wrapping.

The rule prevents sessions from being eaten by tool fights. A parked
decision with workaround beats an in-progress sub-phase that doesn't
land for two days.

### Stop at sub-phase boundaries

The agent stops at sub-phase boundaries even when it could keep going.
The 1f sub-phase split mid-stream at 5/11 commits because conversation
length and tooling issues warranted a pause; running into a fresh
sub-phase with a tired session produces worse work. The successor has
explicit permission to pause mid-sub-phase if conditions warrant —
surface the pause to the user with reasoning, propose how to resume
(e.g., "split as 1f-i / 1f-ii, no renumbering of subsequent sub-
phases"), and stop.

### Read user prompts twice

When the user sends a prompt, read it twice before planning. The first
read picks up the explicit ask; the second read catches the framing,
the warnings, the "specifically check..." asks that are easy to miss
on first pass. Several Phase 1 prompts had embedded asks the agent
could have missed without careful reading — for example, the 1g
prompt asked specifically whether `IngestionOrchestrator` had hash-
dedup logic (which led to PB-3 / P-9 being correctly parked rather
than tested with a fake implementation).

The cost of reading twice is 30 seconds. The cost of missing an
embedded ask is a sub-phase that ships incomplete and has to be
revisited.

### Sub-phase reports use the standing format

Per `WORKFLOW.md` Appendix A, every sub-phase report has eight
mandatory sections in fixed order: Commits, Build state, What landed,
Design decisions resolved, Active parked decisions, Surprises during
implementation, Surface for next sub-phases, Next. Empty sections are
explicit ("Surprises: none") rather than omitted. The fixed structure
means the user can find what they're looking for in 30 seconds; the
agent can't bury or skip a section.

The "Surprises" section is the load-bearing one. Be honest. Design
changes that surfaced during testing get clearly-labeled `feat()`
commits, not hidden inside test commits. Mixed concerns in a commit
are a workflow violation.

### Parked-decisions discipline

When a decision surfaces that can't be made yet (insufficient
information, future-phase work, deferred for YAGNI), it goes in
`PARKED.md` with:

- **Stable ID** (`P-N`).
- **Surfaced in** (which sub-phase, which commit hash).
- **Resolve in** (which sub-phase or phase owns the call).
- **Question** (the actual decision).
- **Context / leanings** (what's known so far).
- **Status** (`parked`, `in progress`, `resolved → <pointer>`).

When 5+ items target the same phase, frame them as a cluster (the
Phase 4 absorbed cluster is the canonical example) so the resolving
phase plans against the full set, not piecemeal.

### Contract-not-artifact assertion bar

Every test asserts at least one contract behavior the docs document,
not just an internal-state artifact. "The destination contains 100
rows" is artifact ("the implementation happened to produce 100 rows
for this input"); "every committed row matches a committed parent
under FK enforcement" is contract behavior. If a test only asserts
artifacts, the implementation can change shape (different number of
rows for the same input) and the test silently rots into a non-test.

When in doubt: write the assertion as a sentence about what the docs
say should happen. If the sentence references the contract (the
state machine, the observation catalog, an interface contract), the
assertion is contract-shaped.

### Perturb-recover for load-bearing tests

Three to four times per sub-phase, deliberately break the production
code in a way the test should catch. Run the test. Verify it fails
with a useful assertion message — not just "AssertionException," but
something a future contributor breaking the same contract would read
and understand. Restore the production code; verify the test passes
again.

This is a manual quality gate. It catches the "test passes but design
is wrong" failure mode where the test happens to pass for the wrong
reason. A test that fails with a vague message under perturbation has
the right intent but the wrong assertion shape — refine the assertion
before moving on. The 1h `MultiTableDependency` test was perturb-
recovered: the assertion message now explicitly explains the contract
violation, so a future contributor breaking FK validation sees the
diagnostic without reading the test source.

### Context-awareness as default mode

Notice things that aren't part of the immediate task and surface them.
This is the discipline that made Phase 1's quality. Examples:

- Spotting a license issue in a dependency before it's committed.
- Noticing that an interface name doesn't match the actual behavior.
- Realizing that a fake from a prior sub-phase was never built (the
  `InMemoryFileReaderRegistry` gap surfaced from 1g into 1h).
- Recognizing that a test passing might be coincidental (the
  bulk-vs-lazy FK preload bug discovery).

The pattern is **noticed → surfaced → resolved**. Don't silently
absorb adjacent issues. Surface them with one line ("I noticed X;
should we Y?"); if the user confirms, address them in a clearly-
labeled commit; if the user defers, park them.

Context-awareness costs little when active and a lot when absent.
Phase 1's design quality came from this discipline operating as
default mode, not as exception.

### Honest "Surprises" section

The end-of-sub-phase report's Surprises section is the discipline
test. Be honest. The successor reads sub-phase reports to learn the
project; whitewashing surprises means future agents repeat them.
Examples of how to write surprises (from Phase 1):

- "The bulk FK preload was wrong for in-batch parent-child references.
  This is the substantive design issue 1h surfaced. Fix landed as a
  separate `feat()` commit ahead of the test commit per WORKFLOW.md."
- "`InMemoryFileReaderRegistry` was missing from 1g. I missed it
  during 1g planning. Surfaced when 1h's tests needed to wire the
  orchestrator. Honest gap; the fix was minimal."

Don't perform; be honest about what surprised you. Phase 1's reports
named several agent mistakes explicitly — that's the bar.

---

## 2. How the user works

Understanding the user's working pattern is half the relationship.
The other half is understanding how the agent contributes to it.

### Plans are confirmed before code lands

The user reads the plan, reviews the design questions, and explicitly
confirms (or pushes back) before any production code is written. This
is non-negotiable. Sub-phases that skip the confirmation step ship
incomplete because the agent's assumptions about the user's intent
will be wrong somewhere — surfacing those assumptions in the plan and
getting confirmation is what prevents that.

The user's confirmation is also the agent's permission to start
implementing. Until the confirmation lands, the agent is in plan-mode,
not implement-mode. If the user asks a clarifying question instead of
confirming, that's still plan-mode — keep iterating until both sides
are aligned.

### Push-back is welcomed

The user surfaces design questions with their own preferences. The
agent's job is not to rubber-stamp them. When the agent disagrees,
push back with reasoning. Sometimes the user accepts the push-back;
sometimes the user wins; both outcomes are fine. What's not fine is
silent agreement.

Phase 1 examples of pushing back:

- 1f-ii Q4 (centralized cross-assembly meta-test scan). User leaned
  centralized; agent pushed back with reasoning (cross-assembly
  wiring vs. simple per-assembly meta-tests); user accepted the
  push-back.
- 1g Q4 (ScriptedTransformer shape). User surfaced two options; agent
  confirmed the simpler one with reasoning; user accepted.
- 1h Q4 (meta-test scope). Same pattern.

What "good push-back" looks like: explicit reasoning, named lean,
willingness to be overridden. "I lean (b) because X. If you'd rather
(a), I'll do (a) — but I want the disagreement on the record." That's
the shape.

### Surfacing is rewarded; silent decisions aren't

Decisions made silently produce work the user has to reverse-engineer
later. Decisions surfaced in the plan or in a sub-phase report
produce work the user can review and accept (or push back on). The
user catches operational risks; the agent catches technical ones.
Each side surfaces what they catch.

The cost of surfacing is one paragraph; the cost of a silent decision
is the user discovering the assumption later and having to relitigate
it.

### Parked decisions get explicit phase ownership

When a decision is parked, it has a `Resolve in` field that names a
specific sub-phase or phase. Vague phase ownership ("resolve later")
produces parked decisions that linger forever. Specific ownership
("Phase 2 first-tier", "Phase 4 cluster", "when X surfaces in
Phase 6") forces the resolving phase to plan against the parked item.

When 5+ items target the same phase, frame them as a cluster (Phase 4
operational-recovery cluster is the canonical example) so the
resolving phase plans against the full set, not piecemeal.

### Reports use the standing format

Sub-phase reports use the eight-section standing format from
`WORKFLOW.md` Appendix A. The user expects every section, in order,
even if a section is empty ("Surprises: none"). This isn't bureaucracy
— the standing format means the user can scan the report fast and
know they haven't missed anything.

Don't improvise on the format. If a sub-phase produced something the
format doesn't capture, the right move is usually to fold it into the
existing sections, not to add a new one. (Adding a section to the
standing format is a discussion to have explicitly with the user, not
a unilateral move.)

### Don't perform; be honest about what surprised you

The user has called this out explicitly. The Surprises section of a
sub-phase report is the place to name agent mistakes, debugging that
took longer than expected, gaps in the agent's earlier planning, and
discoveries that changed the design. Whitewashing this section
produces reports that look polished but teach the successor nothing.

Two examples of honest surprises from Phase 1:

- "I missed [the InMemoryFileReaderRegistry] during 1g planning;
  surfaced when 1h's integration tests needed to wire the
  orchestrator."
- "The bulk FK preload was wrong for in-batch parent-child references.
  This is the substantive design issue 1h surfaced."

Both name the agent's miss, the cost, and the resolution. That's the
bar.

### Stop at sub-phase boundaries

The user owns when the next sub-phase opens. The agent owns finishing
the current one cleanly. Running into the next sub-phase's work
"because there's time" produces sub-phases that aren't well-scoped
and reports that don't end cleanly.

The exception is mid-phase pauses (the 1f split). Those are surfaced
to the user explicitly, not unilateral.

---

## 3. Specific warnings to the successor

These are the warnings a successor needs before they start working —
the dangers most likely to bite if they're not anticipated.

### P-11 is the highest-risk parked decision

**P-11 (handler-to-persisted-batch-status bridging) is the single
highest-risk parked item in the active list.** Phase 1 handlers
operate on the in-memory `Batch` aggregate; the persisted
`BatchSnapshot` is read at handler entry but never written back as
the aggregate transitions. Tests bridge this with
`ForTestingOnly_SetBatchStatus`. Production has no such helper.

If Phase 2 ships without bridging, in-memory aggregate state and
persisted `batch_log` status diverge silently. Audit trails break.
Recovery scripts get confused. Phase 4 retrofitting requires
reverse-engineering "what should the persisted state have been" for
batches that ran during the gap.

Phase 2 design **must open with this as a first-tier question**, not
an implementation detail. The Phase 1 lean: orchestrator emits a
status-update call per aggregate lifecycle method (`Start`,
`MarkIngested`, `BeginProcessing`, `Complete`, `Fail`). Phase 2
designs the contract.

### The Phase 4 absorbed cluster — plan against the full set

P-3, P-6, P-7, P-8, and P-10 form the Phase 4 operational-recovery
cluster. Phase 4's original ~3-week scope (transformers, lineage,
built-in reconciliation) expands to ~5–6 weeks once the cluster
absorbs. **When Phase 4 design opens, plan against the full set —
not piecemeal.** The cluster framing in `PARKED.md` exists for this
reason.

A successor who treats the five items as five independent additions
to Phase 4's backlog will under-estimate Phase 4 and miss design
opportunities (e.g., `MarkBatchFailedCommand` for P-7 and
`ResetClaimedAsync` for P-8 may share infrastructure).

### "Test passes but design is wrong" is a real failure mode

Integration tests can pass for the wrong reason: the contract is
correct AND the contract is consistently misimplemented across
layers. The 1h bulk-FK-preload bug almost shipped because a less-
carefully-written test would have asserted "two rows committed"
(an artifact) rather than "the broker_address row commits because
the parent FK is in the destination" (a contract).

Two practices catch this:

1. **Contract-not-artifact assertion bar** (§1).
2. **Perturb-recover** for load-bearing tests (§1).

Both cost time. Both prevent shipping bugs that look correct.

### Operational risks dominate technical risks from Phase 2 forward

Phase 1 was technical-risk-heavy: design the layering, the contracts,
the state machines, the observation model. Phase 2 onward is
operational-risk-heavy: persisted state corrupting, retries that
loop forever, observations that get lost in pipeline failures.

The user catches operational risks. The agent catches technical
ones. From Phase 2 onward, the agent should expect the user to flag
operational concerns the agent would have missed. Don't be defensive
when this happens — the user has more context on the operational
shape than the agent does.

### The fakes' "no auto-advance batch status" gap

Across Phase 1's 1g and 1h, every cross-handler flow test had to call
`InMemoryStagingRepository.ForTestingOnly_SetBatchStatus` between
handlers because the fakes don't bridge the in-memory aggregate's
view to the persisted snapshot. **This is a fakes-only gap; production
needs P-11's resolution.** A successor who copies the
`ForTestingOnly_` pattern into production code has misunderstood the
fakes' role as test scaffolding.

### `BatchStateMachine` has no `Ingesting → Failed` transition

Locked in 1d. A batch that throws during ingestion stays in
`Ingesting` forever; `IngestBatchHandler` does not transition to
`Failed` because the state machine would reject it. This is correct
behavior for the state machine (it reflects what *can* be observed
reliably) but a real operational gap: stuck-in-Ingesting batches have
no command-level recovery in Phase 1.

P-7 captures this. Phase 4 introduces `MarkBatchFailedCommand` with
a `BATCH_FAILED_BY_OPERATOR` audit observation. Until then, an
operator hitting a stuck-Ingesting batch has only manual `batch_log`
SQL — opaque, not auditable. **Phase 2 should not relax the state
machine to fix this**; that couples state-machine semantics to
recovery semantics, which is the wrong direction. Wait for Phase 4.

### Don't reintroduce `TimeProvider` on `ResilientObservationSink`

P-5 lifts `TimeProvider` to first-tier Phase 2, but specifically at
the registry-repository layer, not the sink. The sink's lockup
history (see §8) makes `TimeProvider` there expensive to retrofit.
The current explicit-`IReadOnlyList<TimeSpan>`-backoff shape works.
Don't replace it without a real consumer driving the change.

### Predecessor-bug guards: respect the attribute discipline

The 11 `[PreventsPredecessorBug]` regression tests are the project's
direct connection to the predecessor system's failures. Each one is
load-bearing. When a successor finds themselves wanting to remove or
modify one of these tests, the right move is almost always "don't" —
the bug is real, the predecessor lost real money to it, and the
attribute documents the historical context. If a future change makes
one of these tests structurally invalid (e.g., the contract changes
in a way that obsoletes the test), surface the change explicitly with
a clear before/after of the contract.

The per-assembly meta-tests
(`RowStateMachineRegressionTests.All_regression_tests_carry_a_predecessor_bug_attribute`
and the `CrossComponentRegressionTests` mirror) enforce that every
test in those classes carries the attribute. A new regression test
without the attribute fails the meta-test immediately.

---
