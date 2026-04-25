# WORKFLOW.md — Streamline Development Rhythm

**This document defines the cycle: plan, implement, test, commit.**
Following it makes development predictable. Skipping it makes
development expensive.

---

## The Cycle

Every task follows the same rhythm. No exceptions, even for "quick
fixes."

```
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│    1. Read task        2. Plan           3. Confirm          │
│         ↓                  ↓                  ↓              │
│    4. Implement        5. Test           6. Commit           │
│         ↓                  ↓                  ↓              │
│    7. Self-review      8. PR            9. Address review    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

Steps 1-3 are the planning phase. Skipping them is the single biggest
cause of wasted cycles. The plan step takes 10 minutes and saves
hours.

---

## Step 1: Read the Task

A task comes in as a prompt, an issue, or a plan work item. Before
doing anything else:

- Read it fully. Twice if it's complex.
- Read the relevant section of `streamline-plan.md`.
- Read any linked issues, PRs, or prior discussion.
- Identify the phase this task belongs to.
- Identify the layer this task affects (Core, Domain, Application,
  Infrastructure, Console, a Files.* package, etc.).

If any of these are unclear, ask before going further. Don't guess.

---

## Step 2: Plan

Write a plan. Out loud. In the PR or issue comment, or at the top of
the work session. The plan has four parts:

**1. What you're going to do.** One sentence.
"Add `CombineAllMatchingSelector` implementation for xlsx reader."

**2. Files you'll touch.** A list. Include new files.
```
- src/Streamline.Files.Xlsx/SheetSelectors/CombineAllMatchingSelector.cs  (new)
- src/Streamline.Files.Xlsx/XlsxFileReader.cs  (extend selector dispatch)
- tests/Streamline.Files.Xlsx.Tests/CombineAllMatchingSelectorTests.cs  (new)
- tests/fixtures/files/xlsx/synthetic/multi_sheet_same_schema.xlsx  (new fixture)
```

**3. Test strategy.** What tests you'll add, what they'll cover.
"Unit tests for the selector resolution logic (5 cases: all match,
none match, mixed, column mismatch with fail policy, column mismatch
with union policy). One end-to-end test reading a real multi-sheet
xlsx fixture."

**4. Unknowns or judgment calls.** Things you'd flag for review.
"The `on_column_mismatch: union` policy needs to decide what column
order to use when sheets disagree. I'm going to use the first sheet's
order; flag if that's wrong."

---

## Step 3: Confirm (when needed)

Some plans don't need confirmation. Implementing a stated work item in
the current phase, with no surprises, is fine to proceed.

**Plans that need confirmation before proceeding:**

- You're adding a new public interface or modifying an existing one.
- You're adding a new project, new NuGet reference, or new external
  dependency.
- You're modifying anything in `Streamline.Core`.
- You identified a judgment call in the plan's section 4.
- You're changing the registry schema or state model.
- The scope of the task looks different (larger or smaller) than the
  task description implied.
- The task touches more than 5 files, or creates more than 3 new
  files.
- You're modifying migrations (data changes, not just schema
  additions).

**When confirmation is needed, state what you want and wait.** Don't
post the plan and immediately start coding. The pause is the point.

---

## Step 4: Implement

Write the code. The approach:

- **Write the test first when you can.** For pure logic (Domain,
  Core), the test-first cycle catches design issues cheapest.
- **For wiring changes (Application, Infrastructure), implement
  alongside tests.** Strict TDD in these layers is often cumbersome;
  what matters is that tests exist when the commit lands, not the
  order they were written in.
- **Stay inside the task's scope.** If you notice a bug, code smell,
  or missing test unrelated to your task, make a note and file it
  separately. Don't expand the current task.
- **One concern per commit.** More on this in Step 6.

**Don't:**
- Refactor unrelated code "while you're in there." It muddies the
  diff and risks unrelated breakage.
- Introduce new patterns without a conversation. Consistency is
  cheaper than cleverness.
- Comment out code instead of deleting it. Git remembers.
- Write TODOs without an owner or deadline. TODOs live forever; issues
  get closed.

---

## Step 5: Test

Run the tests. Locally. Before pushing anything.

```bash
# Fast suite first
dotnet test --filter Category!=Integration

# If the change touches infrastructure
dotnet test --filter Category=Integration
```

**Every test relevant to your change must pass locally.** Not "most of
them." Not "probably." All of them. If you can't run integration tests
locally (no Docker, etc.), say so in the PR so reviewers know to look
more carefully at CI.

**Verify your new tests actually test your change:**

1. Write the test.
2. Run it — it should fail (or would fail without your change).
3. Write or apply your change.
4. Run it again — it should pass.

If a test passes before you made the change and after, it isn't
testing your change.

---

## Step 6: Commit

Commits are **small, focused, and individually revertable**. Each
commit is one logical concern.

### Commit Message Format

```
<type>(<scope>): <short description>

<optional longer description>

<optional footer: issue references, breaking changes>
```

**No tool attribution.** Commit messages must not contain
"Co-authored-by" lines naming an AI tool, "Generated with" footers,
"🤖" markers, or any other attribution to the agent that produced the
code. The human operator is the author of every commit. This is a
hard rule — enforced in CI by a commit-message linter.

This applies to:
- Commit messages
- PR descriptions
- Commit trailers
- Any auto-generated footers from tooling

If your tooling adds these by default, configure it to stop.

**Types:**
- `feat` — new feature or capability.
- `fix` — bug fix.
- `refactor` — code change that doesn't alter behavior.
- `test` — adding or changing tests only.
- `docs` — documentation only.
- `chore` — build, CI, tooling, non-code changes.
- `migration` — database migration.

**Scopes** are the affected area. Use the project name or concept:
- `core`, `domain`, `application`, `infra`
- `registry`, `staging`, `destination`, `lineage`
- `files-xlsx`, `files-json`, `files-zip`
- `transform`, `reconciliation`, `state-machine`
- `ci`, `build`, `deps`

**Good examples:**

```
feat(files-xlsx): add CombineAllMatchingSelector for multi-sheet workbooks

Implements the sheet-combining strategy described in plan Section 5.3.
All matching sheets contribute rows to a single logical dataset, tagged
with source sheet name when configured.

Tests cover: all-match, none-match, mixed-match, column-mismatch-fail,
column-mismatch-union.

Closes #123
```

```
fix(state-machine): prevent Committed → Pending transition

The state machine previously allowed Committed → Pending via
ResetForRetryAsync. This could orphan destination data. Retry now
goes through RolledBack → Pending only.

Regression test added.

Fixes #456
```

**Bad examples:**

```
Update stuff                          # Too vague
WIP                                    # Not committable
Fix bug                                # Which bug?
Address PR feedback                    # Squash into the original commit
Merge branch 'main' into feature-xyz  # Avoid merge commits; rebase
```

### One Concern Per Commit

A commit does one thing. If the message needs "and" in it, consider
splitting:

- `feat(files-xlsx): add sheet selector and fix off-by-one in header detection`
  → Should be two commits.

- `refactor(domain): rename RowStatus enum values and update tests`
  → One commit is fine; the tests have to change *with* the rename,
  they're part of the same concern.

### When to Squash

Before opening a PR, squash WIP commits into logical units. Three
commits for three different things: keep. Ten commits that together
build one feature: squash to one.

**During review**, address feedback in new commits (don't force-push
over them — reviewers need to see what changed). Squash those into the
original commits only after approval, before merge.

---

## Step 7: Self-Review

Before opening the PR, review your own diff as if you were someone
else.

**Checklist:**

- [ ] Every change is intentional. No accidental formatting changes,
      stray debug code, commented-out blocks, or leftover TODOs.
- [ ] Tests cover the change. The definition-of-tested checklist in
      `TESTING.md` is satisfied.
- [ ] No new compiler warnings.
- [ ] Nullable annotations are accurate. No `!` without a comment.
- [ ] Public types have XML doc comments.
- [ ] Commit messages follow the convention.
- [ ] The diff matches the plan you wrote in Step 2. If not,
      acknowledge it in the PR description ("while implementing I
      discovered X, which changed the approach to Y").
- [ ] You've run the architecture tests. They pass.
- [ ] You've run the full test suite. It passes.

If any checkbox is unchecked, go back to Step 4 or 5.

---

## Step 8: Pull Request

### PR Description Template

```markdown
## What

One sentence: what this PR does.

## Why

Context: the phase/task/issue this addresses. Link to relevant plan
section.

## How

Brief explanation of the approach. Call out anything non-obvious.

## Tests

What's covered and how. Reference specific test files.

## Open questions / judgment calls

Things you'd like the reviewer to specifically weigh in on.

## Out of scope

Things you noticed but deliberately didn't address in this PR.
Link to follow-up issues if filed.
```

### PR Size

**Small is always better.** A PR under 300 lines of diff gets reviewed
in 30 minutes. A PR over 1000 lines gets skimmed or ignored.

If a task would produce a large PR:
- Can it be split? Usually yes. Scaffolding first, implementation
  second, tests for the scaffolding alongside implementation.
- If genuinely atomic, open the PR, call out the size in the
  description, and pre-warn the reviewer.

### Draft PRs

Open a draft PR early when the scope is substantial or the approach
needs validation. A draft with a plan and one commit gets feedback
before you've written 500 lines in the wrong direction.

---

## Step 9: Addressing Review

- Respond to every comment, even "nit" ones. Acknowledged with a 👍
  or a one-line reply is fine.
- Push fixes as new commits during review, not force-pushes.
- If you disagree with a review comment, say so. Don't silently ignore
  it.
- Mark conversations resolved only after the change is made or
  explicitly agreed to defer.
- Once approved, squash the fixup commits if appropriate, then merge.

---

## Stop Conditions

**Stop and report back, don't push through, when:**

- The task is taking more than 2x the expected time. The expectation
  was wrong; revisit scope.
- You've discovered the task affects more layers than the plan
  described.
- You find an existing bug that would affect your change. File it
  separately; don't fix it in this PR unless trivial.
- A test fails in a way you don't understand. Debugging an unclear
  failure in your current work is how bugs get shipped.
- The architecture test fails. Don't refactor around it; fix the
  design.
- You want to skip a step in `TESTING.md` or this workflow because
  the change is "trivial." Trivial changes cause the most regressions
  precisely because they skip review.

"Stopping" means: commit what you have (as WIP if needed), write down
what you've found, and raise it — in the PR, in an issue, or in a
sync with the team lead. Waiting for input is cheaper than coding in
the wrong direction.

---

## Cadence Expectations

For a typical task in Phase 1-4:

| Task size | Expected cycle time (plan to merge) |
|---|---|
| Small (1-2 files, <100 lines) | Same day |
| Medium (3-5 files, 100-300 lines) | 1-2 days |
| Large (6+ files, 300+ lines) | 2-4 days; should probably be split |
| Extra-large (>1000 lines) | Stop. Split it. |

During Phase 6 migrations, cycles are longer because each migration
includes production data comparison and cutover coordination. Typical
migration task: 3-5 days from registry entry to cutover.

---

## What Makes This Predictable

The cycle works because every step has a clear input and a clear
output. No step is "figure it out as you go."

| Step | Input | Output |
|---|---|---|
| Read task | Task description | Understanding |
| Plan | Understanding | Written plan |
| Confirm | Plan | Go or revise |
| Implement | Confirmed plan | Code + tests |
| Test | Code + tests | All tests pass locally |
| Commit | Tested code | Clean, focused commits |
| Self-review | Commits | Ready-to-open PR |
| PR | PR | Reviewer feedback |
| Address review | Feedback | Merged PR |

If you find yourself between steps with no clear next action, that's
the signal to re-read the plan from Step 2. Usually the answer is
already there.

---

## The Payoff

This rhythm looks like overhead. It isn't. The alternatives cost more:

- **Skipping the plan step** means you write code you throw away when
  you realize halfway through that you misunderstood the task.
- **Skipping tests** means bugs ship, then you debug them in
  production with an audience.
- **Loose commits** mean bisecting a regression takes hours instead of
  seconds.
- **Oversized PRs** mean reviewers approve without really reading,
  which means defects slip in.

Every step of the cycle is there because its absence has cost
something, somewhere, on a prior project. The cycle is defensive. It's
supposed to be slightly annoying. That's how you know it's working.

---

## Appendix A — Sub-phase report format

Every sub-phase closes with a report to the user. Reports use the
structure below. Same headings, same order, every time. Sections
are mandatory; an empty section ("Surprises during implementation:
none") is signal that nothing unusual happened, which is
information itself.

```markdown
## Sub-phase X complete

### Commits

| Commit | What |
|---|---|
| `<hash>` | <one-line description> |

### Build state

- Tests: <Core count> Core, <Domain count> Domain, <App count> Application,
  <Architecture count> architecture
- Warnings: 0
- Errors: 0

### What landed

Bulleted list of concrete deliverables. Reads as the table of contents
of what shipped in this sub-phase. Names the types, methods, and
contracts the next sub-phase can rely on.

### Design decisions resolved during this sub-phase

Each Q1–Qn from the planning round, with the answer chosen and any
substantive deviation. Plus any P-N parked decisions that resolved
this sub-phase, with the resolution and the resolving commit hash.

### Active parked decisions

Mirror of `docs/PARKED.md`'s active list. Table with: ID | Surfaced |
Resolve in | Topic. If a decision is newly parked this sub-phase,
note that in the report and in the entry's "Surfaced in" field.

### Surprises during implementation

Numbered list of things that came up unexpectedly during the sub-
phase — analyzer reversals, contract refinements, doc-comment
issues, etc. Honest accounting; "none" is a valid value.

### Surface for next sub-phases

Numbered notes the next sub-phase needs to know — design
implications, gotchas, contracts established that downstream code
will lean on. Not exhaustive; just things a reasonable agent might
miss without the heads-up.

### Next

One sentence pointing at the next sub-phase or asking for the next
prompt.
```

### Why the rigid structure

Reports vary when each one is improvised, and the user has to scan
to find what they're looking for. A fixed structure makes it
trivial to find:
- "What did we decide?" → Design decisions
- "What's still open?" → Active parked decisions
- "What surprised us?" → Surprises during implementation
- "What does the next sub-phase need to know?" → Surface for next

Empty sections aren't bloat. They're an explicit "no, nothing here"
that the reader can trust without rechecking the rest of the
report.
