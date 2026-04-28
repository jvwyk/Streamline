# Streamline documentation index

This directory holds the project's documentation. Two stacks: the
**handover docs** (read on first session) and the **authoritative
docs** (consulted while working).

## The four handover docs

Read these on first session, in this order:

| File | Genre | Read when |
|---|---|---|
| `ONBOARDING.md` | First-session sequence | First, before anything else. 15-minute read. |
| `HANDOVER.md` | Living working-knowledge document | Second. Sections 1–3 are load-bearing. |
| `PHASE-1-CLOSEOUT.md` | Frozen artifact of Phase 1's close | Third. What's built, what's parked, what's ready for Phase 2. |
| `STANDARDS.md` | Flat index of every load-bearing standard | Fourth. Reference; consult when you want to confirm "is this how things are done here?" |

Future phase close-outs (`PHASE-2-CLOSEOUT.md`, etc.) get added to
this list as each phase closes; each is a frozen artifact dated to its
phase. `HANDOVER.md` is the only living doc among the four.

## The authoritative docs

These are the durable references the handover docs point at:

| File | Domain |
|---|---|
| `AGENTS.md` | Session-startup guide; required reading on every task. |
| `streamline-plan.md` | Project plan: scope, architecture, phases, decisions. |
| `WORKFLOW.md` | Development rhythm; includes the standing sub-phase report format (Appendix A). |
| `TESTING.md` | Testing requirements and category conventions. |
| `OBSERVATIONS.md` | Observation taxonomy and the stable code catalog. |
| `PARKED.md` | Deferred decisions ledger; consulted constantly during work. |

## Which doc do I consult for which question?

| Question | Doc |
|---|---|
| First time on the repo — where do I start? | `ONBOARDING.md` |
| What disciplines produced the project's quality? | `HANDOVER.md` §1 |
| How does the user work with the agent? | `HANDOVER.md` §2 |
| What's dangerous to know before I start? | `HANDOVER.md` §3 |
| Why was X designed the way it was? | `HANDOVER.md` §4; the predecessor-bug guards in tests |
| What's been built, and what's not? | `PHASE-1-CLOSEOUT.md` (most recent) + `streamline-plan.md` for scope |
| What's deferred and to where? | `PARKED.md` |
| Is X how things are done here? | `STANDARDS.md` (the entry's pointer takes you to the authoritative source) |
| Standing report format / commit format? | `WORKFLOW.md` |
| What observation code applies to this case? | `OBSERVATIONS.md` |
| Test discipline for this scenario? | `TESTING.md` |

## When to update which doc

- **`HANDOVER.md`** updates when a new pattern emerges that future
  agents would benefit from inheriting. Don't update it for one-off
  events; it's working knowledge, not a changelog.
- **`PARKED.md`** updates whenever a parked decision lands, gets
  resolved, or has its phase ownership shift. Status field changes
  link back to the resolving commit.
- **`streamline-plan.md`** updates when a sub-phase resolves a question
  that was open in the plan. Historical decisions are preserved; don't
  rewrite history.
- **`PHASE-N-CLOSEOUT.md`** is **frozen** at its phase's close and not
  edited after that point. New phases get new close-outs.
- **`AGENTS.md`** updates when the operating principles change (rare).
- **`OBSERVATIONS.md`** updates when a new observation code is added,
  which is a deliberate design step (codes are wire-protocol stable).
- **`TESTING.md`** and **`WORKFLOW.md`** update when the discipline
  evolves; both have been updated mid-project (e.g., WORKFLOW.md
  Appendix A on the standing report format landed in 1f-i).
- **`STANDARDS.md`** updates when a new load-bearing standard emerges.
  The doc is a pointer index, not a rules copy; updates add an entry
  with rule + rationale + pointer.
