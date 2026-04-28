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

## 4. Architectural tacit reasoning

The architecture has reasons. They're not all in the formal docs. A
successor who understands the *why* picks up the project at the same
standard; a successor who knows only the *what* is one short
deadline away from undoing decisions that took a sub-phase to make.

### DDD layering as the deliberate inverse of the predecessor's coupling

The strict Core → Domain → Application → Infrastructure → Console
layering is not arbitrary. The predecessor system had cross-layer
references everywhere: business logic reaching into ADO.NET,
domain types depending on serialization formats, infrastructure
calling back into application services. The result was a system
nobody could refactor without breaking something distant.

Streamline's layering is the deliberate inverse. NetArchTest enforces
the layer dependencies; csproj-XML inspection enforces project-level
boundaries that NetArchTest can't reach. A successor who finds
themselves wanting to "just add a quick reference from Domain to
Infrastructure" is recreating the predecessor's coupling — the
layering tests will catch it, but the right move is to design
through the contract, not around it.

### Observation taxonomy as the answer to silent failures

The predecessor system's failures were silent. Files that didn't
arrive, transformations that dropped rows, FK violations that fell
through cracks, schema drift that no one noticed for weeks. The
operational cost was real: data quality complaints from downstream
consumers, post-hoc reconciliation work, lost trust.

Streamline's observation model is the structured answer. Codes are
stable wire-protocol values (`UPPER_SNAKE`, `nameof()`-locked).
Severities map to operational meaning (Info / Warning / Error /
Critical). Every failure mode the predecessor had silently is now an
observation code with a documented emission point. The
`OBSERVATIONS.md` catalog is the contract.

The taxonomy isn't optional decoration; it's the engine's answer to
"how do operators know what happened?" When a successor finds
themselves implementing a new failure mode, the question is not "do
I need to emit an observation?" but "what observation code is this?"

### Per-batch FkResolver lifetime preventing PB-1

The `FkResolver` is constructed fresh per
`ProcessingOrchestrator.ExecuteAsync` call. This is deliberate. The
predecessor's PB-1: the FK cache outlived a single batch, so a
parent value added in batch N+1 wasn't visible to FkResolver because
it had been initialized in batch N. Children quarantined as FK
violations.

Streamline's defense: per-batch construction. Plus the lazy-per-entry
preload from 1h means the cache state is scoped even more tightly —
loaded just before each entry's table runs, against the
read-your-writes view that includes prior tables' pending writes.

A successor who finds themselves wanting to share `FkResolver`
across batches "for performance" is recreating PB-1. The
construction cost is negligible (a `Dictionary<>`); the savings
aren't worth the cache-leak risk.

### Contract-parity discipline as Phase 2's safety net

The in-memory fakes are designed to mirror Postgres exactly. Every
contract method's observable end-state must match between the fake
and the eventual Postgres implementation. The fakes' Q2 design
question (1g) was specifically about how faithful the destination
adapter should be: not "it's a fake, take shortcuts" but "observably
equivalent to Postgres for what the orchestrator does." Read-your-
writes within a transaction. Per-table savepoint rollback.
First-match-wins file-mapping resolution. Insertion-order tiebreaker
on observation timestamps.

This discipline lets Phase 2's contract-parity tests run the same
scenarios against both fake and Postgres. If Phase 2 ships and the
1h tests pass unchanged, Phase 2 is correct. A successor who relaxes
the fakes' faithfulness "because it's just a test fake" is breaking
Phase 2's safety net.

### Per-table savepoint isolation

`ProcessingOrchestrator` opens a savepoint per table. A failure
during one table's upsert rolls back only that savepoint and marks
that table's claimed rows as `RolledBack` (T4). Other tables in the
same batch continue. The outer transaction commits at the end if
any tables succeeded.

The reason: blast-radius containment. A schema-drift issue affecting
one table shouldn't abort an entire batch's worth of progress. The
predecessor system had no per-table isolation — a single transformer
failure could roll back hours of upstream work.

### Aggregate-as-policy-gate (not row store)

The `Batch` aggregate validates state transitions and emits domain
events. It does not hold rows. Row state is owned by
`IStagingRepository`; counts come from
`IStagingRepository.GetRowCountsAsync`. The aggregate does not cache
either.

Holding rows in the aggregate doesn't survive Phase 2: a million-row
batch can't fit in memory, and "the aggregate is the system of
record" stops being true the moment persistence is real. The
aggregate is small on purpose. A successor who finds themselves
wanting to add a `Rows` collection to the aggregate "for testing
convenience" is misunderstanding the policy-gate role; use
`ForTestingOnly_AllRows` on the staging fake instead.

### Throw-on-illegal state machines

`BatchStateMachine` and `RowStateMachine` throw
`IllegalStateTransitionException` when an illegal transition is
attempted. This is deliberately loud. Predecessor flag #6: silent
skips on illegal transitions masked orchestrator preload bugs and
produced data in inconsistent states.

A successor who finds themselves wanting to "soften" the state
machine ("just return false, the caller will handle it") is
recreating predecessor flag #6. The loud throw is the design.

### The "Phase 4 will absorb this" pattern

Multiple parked decisions across Phase 1 ended up at Phase 4: P-3,
P-6, P-7, P-8, P-10. This is not coincidence — Phase 4 was always
going to be where operational completeness lands. Phase 1's job was
deliberate scope discipline: build the verification surface so
Phase 2 is a Postgres implementation exercise, not a design
exercise.

When a successor hits something tempting that doesn't fit the
current phase cleanly, the question to ask is: "is this Phase 4's
job?" — and usually yes. Bounded retry policy? Phase 4. Operator
recovery commands? Phase 4. Reconciliation runner? Phase 4.
Stuck-row recovery? Phase 4. The pattern is real; the cluster
framing in `PARKED.md` makes it visible.

A successor who tries to absorb operational concerns into Phase 2
is over-scoping Phase 2. Resist the temptation.

---

## 5. If you find yourself wanting X, the answer is usually Y

These are shortcut signals — when the agent recognizes the want,
they recognize the answer. Each pattern emerged from Phase 1 work
where the want was tempting and the answer wasn't immediately
obvious.

### "I want to add a `Rows` collection to the `Batch` aggregate for testing convenience"

**Answer: use `ForTestingOnly_AllRows` on the staging fake instead.**

The aggregate is policy-gate, not row store. Holding rows in the
aggregate doesn't survive Phase 2. The fake's `ForTestingOnly_`
surface exposes audit-trail detail tests need without compromising
the production design.

### "I want this fake to be more flexible — computed outcomes, configurable behavior"

**Answer: simplest thing that works for v1; add flexibility when a
real test needs it.**

The 1g `ScriptedTransformer` design specifically rejected a
`Func<TransformReference, BatchContext, TransformOutcome>` shape in
favor of a simple list of pre-canned outcomes. The list works for
every test today; if a test ever needs computed outcomes, add the
overload then. Speculative flexibility is harder to reason about,
and "more flexible" usually translates to "more places for bugs to
hide."

### "I want to retrofit observation emission into a method"

**Answer: orchestrator emits, not the method.**

P-2's resolution: services stay sink-free. Repositories,
validators, drift detectors, transformers — none of them take an
`IObservationSink`. The orchestrator translates results / events
into observations and emits via `DomainEventPublisher` and the
per-batch sink. A successor who finds themselves wanting to add a
sink parameter to a service method is undoing P-2's resolution.

### "I want to silently absorb this analyzer warning"

**Answer: suppress with rationale at the smallest scope, never
globally.**

The CA1707 (UPPER_SNAKE identifiers), CA1720 (type-name enum
values), and CA1848 (LoggerMessage delegates) suppressions in
Phase 1 are all class-scoped with a multi-line `Justification`
explaining why the rule's premise doesn't apply. None are
project-wide or solution-wide. A successor who edits
`Directory.Build.props` to suppress an analyzer globally has
broken a design choice that took deliberate work.

### "I want to share state across two fakes"

**Answer: don't. Each fake owns its own state; cross-references
are by ID.**

Q3 of the 1g plan resolved this. Postgres tables relate by FK
identifiers, not by shared backing stores; the fakes mirror that.
A "shared dictionary" between
`InMemoryStagingRepository` and `InMemoryLineageRepository` would
make the fakes diverge from Postgres's actual behavior, which
would break the contract-parity safety net.

### "I want to relax the state machine to make recovery easier"

**Answer: don't. Add a recovery command in the operational
phase.**

P-7's resolution was specifically not to add `Ingesting → Failed`
as a legal state-machine transition. The state machine reflects
what *can* be observed reliably; recovery semantics are a
separate concern that lives in operator commands
(`MarkBatchFailedCommand` in Phase 4). Coupling state-machine
semantics to recovery semantics is the wrong direction.

### "I want to add a feature that probably belongs in Phase 4"

**Answer: park it. Cite the phase.**

Bounded retry policy? Phase 4. Operator recovery? Phase 4.
Reconciliation? Phase 4. Stuck-row cleanup? Phase 4. The pattern
is consistent. When in doubt, surface in the plan, propose
parking, let the user decide. Don't unilaterally pull Phase 4
work into the current phase.

### "I want to fight this tooling issue until I crack it"

**Answer: 30 minutes. Then fall back, park as a P-N, move on.**

The `FakeTimeProvider` deadlock cost more than the 30-minute
budget allows; the explicit-`IReadOnlyList<TimeSpan>`-backoff
workaround landed and P-5 was parked. The successor has
permission to choose pragmatism over heroics here.

---

## 6. Concrete context-awareness examples

Five examples from Phase 1 of context-awareness in practice. Each
shows the pattern: **noticed → surfaced → resolved.**

### FluentAssertions → AwesomeAssertions (Phase 0)

The agent noticed FluentAssertions's licensing terms during
package selection — commercial use restrictions that would have
been a real problem for a project intended for production use.
Surfaced before any test code referenced the package; user
confirmed the switch; AwesomeAssertions is what the project uses.

The catch saved a future "we have to migrate the entire test
suite away from a paid library" exercise. Cost at noticing: one
sentence in a planning round. Cost if missed until later: weeks.

### Bulk FK preload bug discovery (1h commit 6, fix at `fd59469`)

The agent wrote the 1h `MultiTableDependency` integration test
asserting that the broker_address row with `broker_id=1` commits
because the parent broker row exists. The test failed. Investigation
revealed `ProcessingOrchestrator` preloaded all FKs at batch start,
before any table was processed; in-batch parent-child references
saw an empty parent. Fix landed as a separate `feat(application)`
commit ahead of the test commit per WORKFLOW.md.

The test failure was the first sign of the design bug. A less
careful test (asserting "two rows committed" rather than "the
broker_address row commits because the parent FK is in the
destination") would have passed under the buggy implementation —
the test would have been a false positive, the bug would have
shipped.

### TRANSFORM_MODE_DEFERRED rename (1f-ii follow-up)

The 1f-ii Processing orchestrator emitted `TRANSFORMER_NOT_FOUND`
for transform-mode entries in v1. The user noticed during the
1f-ii close-out that this code reads as "we looked for a transformer
named X and couldn't find one" — which is the legitimate Phase 4
error for an unresolvable transformer reference, not the v1 case
of "transform mode isn't implemented yet."

The agent surfaced agreement with the user's catch, added
`TRANSFORM_MODE_DEFERRED` to `OBSERVATIONS.md` and `ObservationCodes`,
updated the orchestrator and tests, and reserved
`TRANSFORMER_NOT_FOUND` for Phase 4. Diagnostic clarity in Phase 4
production was the real cost; the small disruption now was worth it.

### `InMemoryFileReaderRegistry` gap (1g → 1h)

During 1g planning, the agent missed that `IngestionOrchestrator`
depends on `IFileReaderRegistry` and that no fake existed for it.
The 1g plan listed eight fakes; this should have been nine. The gap
surfaced during 1h commit 1 when integration tests needed to wire
the orchestrator and there was nothing to register file readers
with.

The agent surfaced the gap explicitly in the 1h plumbing commit:
"InMemoryFileReaderRegistry: filling a gap from 1g — the
orchestrator depends on this contract but no fake landed in 1g's
commits." The fix was minimal (~70 lines including tests). Honest
gap, honestly named.

The lesson: even careful planning leaves gaps. When one surfaces,
the right move is to name it, not to bury it inside an unrelated
commit.

### Phase 4 cluster framing (1i close-out)

The agent and user noticed that the Phase 4 backlog was accumulating
parked decisions silently. By the end of 1i, five parked items
targeted Phase 4. The user surfaced the framing question: are these
five items five independent additions, or are they one absorbed
sub-phase scope?

The agent agreed with the cluster framing and produced the PARKED.md
section that names Phase 4 explicitly as the operational-recovery
sub-phase scope. Original ~3-week estimate expands to ~5–6 weeks;
the estimate is now honest, not silently optimistic.

The pattern: when parked decisions accumulate against the same
phase, frame them as a cluster before that phase opens.

---

## 7. Rejected decisions, with reasoning

Eight load-bearing rejections from Phase 1. The successor must not
relitigate these without good reason; this section names the reason
each was made.

### Boolean parsing — broad tokens, not strict `bool.TryParse`

**Rejected:** strict `bool.TryParse` (accepts only "True" / "False"
case-insensitive, plus "true" / "false").
**Chosen:** broader token set including "yes" / "no" / "y" / "n" /
"1" / "0" plus the bool.TryParse defaults.
**Reason:** the predecessor's data uses these tokens. Strict parsing
would reject legitimate source data. Q11 of 1e (reversed during
the planning round).

### Cross-fake state coordination — independent fakes, not shared store

**Rejected:** a shared in-memory store that all fakes read/write
to via constructor injection.
**Chosen:** each fake owns its own state; cross-references are by
ID; the orchestrator passes valid IDs.
**Reason:** Postgres tables relate by FK identifiers, not by shared
backing stores. The fakes mirror that to keep the contract-parity
safety net intact. Q3 of 1g.

### State-machine placement — separate `StateMachine/` folder, not inline

**Rejected:** state-machine logic embedded in factory methods on
the aggregate.
**Chosen:** dedicated `StateMachine/` folder with `RowStateMachine`
and `BatchStateMachine` as static classes; the legal-transition
matrix is the single source of truth.
**Reason:** the state machine is the contract. Embedding it in the
aggregate scatters the matrix and makes "what's legal?" a question
that requires reading multiple methods. The separate static class
is the obvious answer when the question is asked. 1d planning round.

### `PerBatchScope` — static factory, not raw constructor

**Rejected:** `new PerBatchScope(state, sink, publisher)` with
callers wiring the three pieces themselves.
**Chosen:** static `PerBatchScope.CreateForBatch(innerSink, logger)`
that constructs and wires the three pieces internally.
**Reason:** the three pieces have non-trivial coupling
(`ResilientObservationSink` decorates `innerSink` with `state`;
`DomainEventPublisher` wraps the resilient sink). Exposing the raw
constructor would let callers build broken combinations. Q6 of 1f-ii.

### FK preload — lazy per-entry, not bulk-at-start

**Rejected:** `ProcessingOrchestrator.PreloadFksAsync` running once
at batch start before any table is processed.
**Chosen:** `PreloadFksForEntryAsync` running just before each
entry's table is processed; `FkResolver.IsLoaded` short-circuits
redundant loads.
**Reason:** the bulk-at-start shape didn't see in-batch parent-child
references. Surfaced during 1h's `MultiTableDependency` test.
`fd59469`.

### Meta-test scope — per-assembly, not centralized cross-assembly

**Rejected:** a single meta-test scanning all assemblies for
`[PreventsPredecessorBug]` via reflection-by-name.
**Chosen:** per-assembly meta-tests with attribute duplication
across `Domain.Tests` and `Application.Tests`.
**Reason:** centralization would require either circular project
references or a shared test-utilities project — both more cost
than the ~30-line attribute duplication. Q4 of 1i.

### `ScriptedTransformer` — list of outcomes, not `Func<>`

**Rejected:** constructor takes
`Func<TransformReference, BatchContext, TransformOutcome>` for
computed outcomes.
**Chosen:** constructor takes `IEnumerable<TransformOutcome>?`
plus an `Enqueue(TransformOutcome)` method for incremental setup.
**Reason:** tests need predictable outcomes, not computed ones.
The `Func<>` shape is more flexible but harder to reason about;
add it when a real test needs it. Q4 of 1g.

### Sub-phase split — 1f-i / 1f-ii, not mid-phase renumbering

**Rejected:** when the 1f sub-phase needed to split mid-stream
(at 5/11 commits), renumber subsequent sub-phases (1g → 1h, 1h →
1i, etc.).
**Chosen:** introduce 1f-i and 1f-ii as the sub-phase identifiers;
1g, 1h, 1i remain.
**Reason:** renumbering invalidates references in commit messages,
PARKED.md entries, and prior reports. The 1f-i/1f-ii split keeps
references stable and isolates the change to the affected
sub-phase only.

---

## 8. Debug gotchas

Seven debugs from Phase 1 that cost real time. Each one names the
symptom, the cause, and the fix. A successor hitting one of these
should recognize the symptom and apply the fix without re-paying
the original debug cost.

### `FakeTimeProvider` + xUnit v3 + NSubstitute deadlock

**Symptom:** `dotnet test` hangs indefinitely; no output, no
timeout, multiple stuck processes accumulate. Cannot Ctrl-C
cleanly.
**Cause:** `Task.Delay(TimeSpan, FakeTimeProvider, CancellationToken)`
plus xUnit v3's async dispatch plus NSubstitute's
`.Returns(callback)` interact in a way that produces an unresolvable
await. The `FakeTimeProvider` doesn't advance through the dispatcher,
so the `Task.Delay` never completes; the test never finishes; the
process never exits.
**Fix:** don't use `FakeTimeProvider` with `Task.Delay` in this stack.
For `ResilientObservationSink`, the workaround was passing backoff
delays as `IReadOnlyList<TimeSpan>`; tests construct with zero-delay
arrays so the suite runs in milliseconds. Production calls
`ResilientObservationSink.WithDefaults(...)` for the v1 50/200/800ms
schedule.
**Parked as:** P-5. Phase 2 introduces `TimeProvider` at the
registry-repository layer where the deadlock pattern doesn't apply.

### `[EnumeratorCancellation]` CS8424 on non-async-iterator wrappers

**Symptom:** compiler error CS8424: "The EnumeratorCancellationAttribute
applied to parameter 'cancellationToken' will have no effect."
**Cause:** the attribute only applies to async-iterator methods
(those with `yield return` directly in their body). A wrapper method
that delegates to an async-iterator and is not itself one will
trip the analyzer.
**Fix:** don't use the wrapper pattern. Either inline the iteration
into the method that needs `[EnumeratorCancellation]`, or write the
helper directly as `async IAsyncEnumerable<T>` with its own `yield
return`. Caught during 1g while writing the staging fake's test
helpers.

### CA1707 / CA1720 / CA1848 protocol-identifier suppression pattern

**Symptom:** analyzer warnings on identifier shapes that are
deliberately protocol-shaped (UPPER_SNAKE observation codes,
type-name enum values like `String` / `Integer` / `Decimal`,
LoggerMessage delegates on a non-hot-path).
**Cause:** the analyzers don't know the context. Their default
rules are correct for general code; they're wrong for
wire-protocol values, type-mapping enums, and decorator-pattern
loggers.
**Fix:** class-level `[SuppressMessage]` with multi-line
`Justification` text explaining why the rule's premise doesn't
apply. Suppressions are at the smallest scope that works (class,
not assembly); never project-wide. Examples: `ObservationCodes`
(CA1707), `ColumnTypeCode` (CA1720), `ResilientObservationSink`
and `InMemoryDestinationAdapter` (CA1848). Pattern established in
1b.

### csproj-XML + NetArchTest hybrid for empty-assembly architecture rules

**Symptom:** NetArchTest can't enforce rules on empty assemblies
or on the project-reference graph itself. Want to assert "the Core
project references no other Streamline project" but Core has no
types yet.
**Cause:** NetArchTest operates on loaded assemblies. An empty
assembly has nothing to check.
**Fix:** the architecture-test project parses csproj XML directly
to validate project references for empty/skeletal projects, and
uses NetArchTest for the loaded-types layering rules. Two
mechanisms, one purpose: keep the layering invariants enforced
even in projects that haven't accumulated types yet. Established
in Phase 0; pattern in `tests/Streamline.Architecture.Tests/`.

### `[PreventsPredecessorBug]` cross-assembly attribute duplication

**Symptom:** want to use `[PreventsPredecessorBug]` from
`Application.Tests` and `Domain.Tests`; the attribute is `internal`
in one assembly; the other can't see it.
**Cause:** the attribute lives in `Domain.Tests/Batches/StateMachine/`
where the original SM-* tests landed; making it `public` doesn't
help cross-assembly reference because `Application.Tests` doesn't
reference `Domain.Tests`.
**Fix:** duplicate the attribute as `internal` in
`Application.Tests/Regression/`. Two distinct attribute types from
the runtime's perspective, but identical name and shape; per-
assembly meta-tests scan their own copy. Avoids cross-assembly
project references and a shared test-utilities project. 1i commit 1.

### `xUnit.Record` name collision with `Streamline.Core.ValueTypes.Record`

**Symptom:** test files importing both `Xunit` and
`Streamline.Core.ValueTypes` see `Record` as ambiguous; the compiler
can't pick.
**Cause:** xUnit defines a `Record` static class for raising/asserting
exceptions; Streamline's domain has a `Record` value type.
**Fix:** file-local `using Record =
Streamline.Core.ValueTypes.Record;` alias at the top of any test
file that needs the domain `Record` and references xUnit.
Established in 1c-era tests; the pattern is in every fakes test
file that uses `Record`.

### CA2241 false positive on curly braces in `becauseArg` strings

**Symptom:** analyzer warning CA2241 ("Provide correct arguments
to formatting methods") on assertion `becauseArg` strings
containing `{}` sequences (e.g., "must see {1, 4} on this preload").
**Cause:** the analyzer interprets `{1, 4}` as a format placeholder
and complains that no corresponding argument exists. False
positive for assertion messages, which use the braces literally.
**Fix:** rephrase the message to avoid the brace syntax ("must see
broker_id=1 and broker_id=4" instead of "must see {1, 4}").
Caught during 1i commit 3 writing the PB-1 test.

---

## 9. Phase 1 history (compressed)

The 11 sub-phases (counting Phase 0 separately and 1f as 1f-i / 1f-ii)
in one paragraph each. This anchors the timeline references in
earlier sections.

### Phase 0 — solution scaffolding and operating principles

Solution structure (Core, Domain, Application, Infrastructure,
Console, plus test projects and skeleton readers/registries). xUnit
v3, AwesomeAssertions, NSubstitute, coverlet wired up. NetArchTest
+ csproj-XML hybrid for architecture enforcement. `WORKFLOW.md`,
`AGENTS.md`, `README.md`, the project plan. Central Package
Management. The discipline doc set established here is what every
sub-phase since has operated under.

### 1a — Domain enums and BatchStatus

Domain enums (`BatchStatus`, `RowStatus`, `ColumnTypeCode`,
`DriftPolicy`, `FkEnforcementMode`, `TransformKind`,
`TransformInvocation`). The asymmetry between batch and row state
machines was settled here.

### 1b — Core observation model

`Observation`, `ObservationSeverity`, `ObservationCodes` (43 codes
across 11 categories), `IObservationSink`. The catalog-as-wire-
protocol design and the `nameof()`-locked identifier pattern
established here.

### 1c — Domain abstractions and infrastructure contracts

Eight infrastructure abstractions (`IStagingRepository`,
`IDestinationAdapter`, `IRegistryRepository`,
`IFileMappingRepository`, `ILineageRepository`,
`IObservationRepository`, `ITransformerRegistry`, `IFileReader`).
P-2 (observation threading) parked here; resolved later in 1f-ii.

### 1d — State machines and the predecessor-bug pattern

`RowStateMachine` and `BatchStateMachine` with throw-on-illegal
semantics; the `Batch` aggregate as policy gate; the
`[PreventsPredecessorBug]` attribute pattern established with
SM-1a, SM-1b, SM-2, SM-3, SM-4, SM-5. P-3 (bounded retry policy)
parked here.

### 1e — Validators and parsers

`RowValidator`, `FkResolver`, `ColumnTypeParser`,
`RegistryValidator`. The Q11 reversed-during-planning boolean
parsing decision (broad tokens, not strict `bool.TryParse`). P-4
(decimal precision/scale) parked here.

### 1f-i — Resilient observation sink and `DomainEventPublisher`

`ResilientObservationSink` decorator (3 retries, 3-failure
threshold, severity-based critical fallback). `DomainEventPublisher`
for event-to-observation translation. P-1 (sink failure semantics)
resolved here. P-5 (TimeProvider injection) parked here after the
`FakeTimeProvider` deadlock.

### 1f-ii — Orchestrators and use-case handlers

`IngestionOrchestrator`, `ProcessingOrchestrator`, four handlers
(`IngestBatchHandler`, `ProcessBatchHandler`, `RetryBatchHandler`,
`InspectBatchHandler`), `PerBatchScope`. P-2 resolved here. P-6
(handler ↔ orchestrator observation flow asymmetry) parked here
after the `BATCH_RETRIED` merge fix. P-7 (stuck-in-Ingesting
recovery) parked here.

### 1g — In-memory fakes

Nine in-memory fakes covering every infrastructure contract.
`InMemoryStagingRepository`, `InMemoryDestinationAdapter`,
`InMemoryRegistryRepository`, `InMemoryFileMappingRepository`,
`InMemoryLineageRepository`, `InMemoryObservationRepository`,
`InMemoryTransformerRegistry`, `ScriptedTransformer`,
`FakeFileReader`. The contract-parity discipline (faithful enough
for Phase 2) established here. `InMemoryFileReaderRegistry` was
missed and surfaced in 1h.

### 1h — Use-case integration tests

Per-handler integration tests plus cross-cutting flow tests
exercising the full lifecycle against the 1g fakes. The
`MultiTableDependency` test surfaced the bulk-FK-preload bug
(fixed at `fd59469`). P-8 (stuck-in-Processing recovery) parked
here.

### 1i — Predecessor-bug regression tests (non-state-machine)

Six new `[PreventsPredecessorBug]` regression tests (PB-1, PB-4)
plus reframings (PB-2, PB-5, PB-6a, PB-6b, PB-8, PB-9). PARKED.md
restructured: P-5 lifted to Phase 2 first-tier, P-9 (file-hash
dedup) and P-10 (reconciliation gap) added, P-11 (handler-to-
persisted-batch-status bridging) formalized as the highest-risk
parked decision. Phase 4 absorbed cluster framing established.
Phase 1 closes with this commit.

### Predecessor-system context

Streamline replaces a system that processed ETL jobs through
manual SQL pipelines with bespoke per-job logic. Operational cost
was significant: silent failures, no observation taxonomy,
inconsistent retry semantics, no schema-drift handling, brittle FK
resolution that leaked across batches. Streamline's design choices
are deliberate inverses of the predecessor's failure modes, which
is why each parked decision and architectural choice in `HANDOVER.md`
links back to specific predecessor pain.

The 11 documented predecessor bugs (`[PreventsPredecessorBug]`
regression tests across Domain.Tests and Application.Tests) are the
project's direct connection to this history. They are load-bearing.

