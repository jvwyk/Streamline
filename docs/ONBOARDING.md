# ONBOARDING.md — your first session

> If you're a new agent landing on this repository, this is the doc that
> gets you productive in 15 minutes. Read it first.

## Reading order, with stop conditions

Read these documents in this order:

1. **`AGENTS.md`** — operating principles, cover-to-cover.
2. **`docs/HANDOVER.md`** — working knowledge for agents continuing
   Streamline. Sections 1–3 are load-bearing (the disciplines, how the
   user works, specific warnings); read them first. Sections 4–9
   (architectural reasoning, shortcut signals, examples, rejected
   decisions, debug gotchas, Phase 1 history) are reference material;
   skim or read selectively.
3. **`docs/PHASE-1-CLOSEOUT.md`** — frozen artifact of Phase 1's close.
   Tells you what was built, what's parked and where, what's ready
   for Phase 2.
4. **Skim `docs/streamline-plan.md`** — the project plan. Don't read
   cover-to-cover; locate the section that covers the phase you're
   working in (Phase 2 if that's the next prompt) and read that.
5. **Skim `docs/PARKED.md`** — the deferred-decisions ledger. You'll
   reference it constantly; reading it once now means you recognize
   parked items by ID when they come up. Pay particular attention to
   the **Phase 4 cluster framing** section at the top of "Active
   deferrals" — it explains why P-3, P-6, P-7, P-8, and P-10 are one
   absorbed scope, not five independent items.

**Don't read** `OBSERVATIONS.md`, `TESTING.md`, or `WORKFLOW.md`
cover-to-cover. They're reference docs; consult them when you're
working in their domains. Trying to load them all up-front is
counterproductive — the volume crowds out the load-bearing material.
`STANDARDS.md` is also consulted on demand — it's the indexed-
reference member of the four-doc handover stack (alongside this
file, `HANDOVER.md`, and `PHASE-N-CLOSEOUT.md`), used to confirm
"is this how things are done here?" rather than read end-to-end.

**Stop condition for the reading round:** after `PARKED.md`, stop.
Confirm to the user that you've loaded the context. Wait for the
next prompt. Don't speculate about phase design before the prompt
arrives — `HANDOVER.md` §3 names the dangers there.

## What your first session looks like

The cadence (`HANDOVER.md` §1):

1. **User sends a sub-phase prompt.** Read it twice. The first read
   picks up the explicit ask; the second catches the framing,
   warnings, and "specifically check..." asks.
2. **You produce a plan.** Confirmations on user leans, additional
   design questions, push-backs with reasoning, commit plan.
3. **You wait for confirmation.** Until the user confirms, you're
   in plan-mode. Don't write production code yet.
4. **You implement** per the rhythm: production code first, then
   tests, then commit. One logical change per commit. Conventional
   commit format. Sub-phase commit numbering ("commit N of M").
5. **You stop at the sub-phase boundary** and report using the
   standing format from `WORKFLOW.md` Appendix A (eight mandatory
   sections).
6. **The user reviews your report.** If everything's good, the next
   prompt opens. If not, you address feedback and re-report.

The first prompt you receive after Phase 1's close opens **Phase 2**
with two first-tier design questions: **P-5** (TimeProvider injection
at the registry-repository layer) and **P-11** (handler ↔
persisted-batch-status bridging). `HANDOVER.md` §3 explains why these
are first-tier; `PHASE-1-CLOSEOUT.md` "What's ready for Phase 2"
explains what Phase 2 inherits.

## Quick-reference tables

### When to "stop and ask" vs. "proceed"

| Situation | Action |
|---|---|
| Plan-round design question with no clear lean | **Stop and ask** in the plan; surface the question and your tentative lean |
| Implementation hits a question the plan didn't cover | **Stop and ask** mid-implementation; the plan was underspecified, surface the gap |
| Plan-round question where you have a clear lean and reasoning | Push back / propose; the user reviews; that's the rhythm, not a stop |
| Implementation hits a small detail that goes either way | Make the call, surface in the next sub-phase report's "Surprises" if it matters |
| Tooling issue costs >30 minutes | **30-minute rule:** fall back to a workaround, park as a P-N, surface in the report |

### When to "stop and report" vs. "continue"

| Situation | Action |
|---|---|
| Sub-phase commits are all in and tests pass | **Stop and report** using the standing format |
| Mid-sub-phase pause (1f-style) is warranted | **Stop and report** the pause; propose how to resume; surface to user |
| Could keep going into the next sub-phase "while there's time" | **Don't.** Stop at the boundary. The user owns when the next prompt opens |
| Discovery during implementation reveals a design issue | Land the fix as a separate `feat()` commit; surface in "Surprises"; don't hide inside test commits |

### Parked decision: surface, resolve in-place, or defer?

| Situation | Action |
|---|---|
| Decision needs information that doesn't exist yet | **Park** with `Resolve in: <phase>` |
| Decision is small and the agent knows the answer | **Resolve in-place**; mention the call in the report |
| Decision affects multiple downstream phases | **Park** with explicit phase ownership; add to a cluster if 5+ items target the same phase |
| Decision is a feature that probably belongs in Phase 4 | **Park** with `Resolve in: Phase 4`; the cluster framing is real (`HANDOVER.md` §4) |

## Where to find what

| Question | Doc |
|---|---|
| What's deferred and to where? | `docs/PARKED.md` |
| What was built in a prior phase? | `docs/PHASE-N-CLOSEOUT.md` for that phase |
| How does the rhythm work? | `docs/HANDOVER.md` §1; `docs/WORKFLOW.md` for the formal rules |
| How does the user work with me? | `docs/HANDOVER.md` §2 |
| What's dangerous if I'm not careful? | `docs/HANDOVER.md` §3 |
| Why was X designed the way it was? | `docs/HANDOVER.md` §4; the predecessor-bug guards in tests |
| Code pattern for an existing concept? | Look in `src/` — find the closest existing example |
| Test discipline? | `docs/TESTING.md` |
| Commit format / sub-phase report format? | `docs/WORKFLOW.md` (Appendix A) |
| Observation code catalog? | `docs/OBSERVATIONS.md` |
| Standing rules indexed? | `docs/STANDARDS.md` |

## The standards bar

This isn't a project where "it works" is enough. The standards bar
applies to every commit:

- **Tests are required for every production change.** No exceptions.
  Tests assert at least one contract behavior, not just an internal
  artifact (`HANDOVER.md` §1, "Contract-not-artifact assertion bar").
- **Commits are individually revertable.** One logical change per
  commit. Design fixes that surface during testing land as separate
  `feat()` commits — never hidden inside the test commit that
  surfaced them.
- **Plans are written and confirmed before code.** No surprise
  implementations. The plan-round is where the agent and user align
  on intent.
- **Design questions are surfaced explicitly.** Numbered, with a
  lean and reasoning. Silent decisions produce work the user has to
  reverse-engineer later.
- **Parked decisions get explicit phase ownership.** No "resolve
  later"; specific phase or sub-phase.
- **Reports use the standing format.** Eight sections in fixed
  order; empty sections explicit, not omitted.

Concrete examples of meeting the bar (from Phase 1):

- 1f-ii's eight-commit plan with pre-commits, design fixes as
  separate `feat()` commits, parked decisions surfaced in the
  close-out.
- 1h's `MultiTableDependency` test with contract-shaped assertions
  ("the broker_address row commits because the parent FK is in the
  destination") and perturb-recover applied.
- 1i's reframings of existing tests under `[PreventsPredecessorBug]`
  rather than re-testing already-covered behavior.

Concrete examples of work below the bar (and why it would be
rejected):

- A test commit that includes a design fix to production code
  without a separate `feat()` commit.
- A sub-phase report that omits the Surprises section because
  "nothing surprising happened" — the right move is "Surprises:
  none" explicitly.
- A parked decision with `Resolve in: later` instead of a specific
  phase.
- An assertion of "100 rows committed" without context for why
  100 (artifact) versus "every committed row matches a committed
  parent under FK enforcement" (contract).

## The first thing to do on landing

Concretely, in order:

1. **Read** `AGENTS.md` (operating principles).
2. **Read** `docs/HANDOVER.md` sections 1–3 (the load-bearing
   disciplines, how the user works, specific warnings).
3. **Read** `docs/PHASE-1-CLOSEOUT.md` (what's built, what's
   ready for Phase 2).
4. **Skim** the sections of `docs/streamline-plan.md` that cover
   the phase you're entering.
5. **Skim** `docs/PARKED.md`, paying attention to the Phase 4
   cluster framing and to P-5 / P-11 if Phase 2 is opening.
6. **Confirm** to the user that you've loaded the context. One
   sentence: "Context loaded — ready for the next prompt."
7. **Wait** for the next prompt. Don't speculate about design
   before the prompt arrives.

If the first prompt that arrives is Phase 2 planning, you should
already know that P-5 and P-11 are first-tier design questions —
the load-bearing parts of `HANDOVER.md` and the Phase 1 close-out
both name them.

## A note on standards inheritance

Phase 1's quality came from specific disciplines, applied
consistently. The handover docs capture them, but the docs alone
don't enforce them — that's the agent's job, every session.

If your first session feels like a fresh-start project, you've
missed the handover. Re-read `HANDOVER.md` §1 and §2; the rhythm
is what makes the project work. The user catches operational
risks, you catch technical ones, both surface, and decisions get
made in the open. That's not boilerplate; it's the operating model.
