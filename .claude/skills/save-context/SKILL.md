---
name: save-context
description: Write a session snapshot to docs/context/ so work can resume across sessions and days. Main session only. Use at node boundaries (step change, verdict, attempt increment, HITL hand-off), after a confirmed compact reload, or when David asks.
argument-hint: "[--checkpoint] [--status in-progress|blocked|final] [--slug <slug>]"
---

# /save-context

Contract: context.spec C1–C3, C6, C8. Run every step in order. Never commit or stage.
Subagents never run this skill.

## 1. Clock
Run `date -u +%Y%m%d-%H%M%S` and `date -u +%Y-%m-%dT%H:%M:%SZ`. Never estimate the time.

## 2. Tail
`ls docs/context/*.md 2>/dev/null | sort | tail -1` → the previous snapshot's basename, or `null`.

## 3. Facts
Run these and use their output verbatim:
- `git rev-parse --abbrev-ref HEAD` → `branch`
- `git rev-parse --short HEAD` → `head`
- `git status --porcelain | wc -l` → uncommitted count
- `git rev-parse --show-toplevel` and `git worktree list`. Report the worktree as a path relative to the main checkout (`.claude/worktrees/<plan>-<node>`), or `null`. Never write an absolute path.
- The active plan in `docs/plans/active/*.dag.yaml`, if any: `plan`, the in-flight `node`, its `attempt` and status, and its next task. If there is no plan: `plan: null`, `node: null`, `phase: analysis`.
- `tests`: `X/Y` from a test run in this session, otherwise `not-run`.

## 4. Checkpoint
`checkpoint: true` if ANY of these hold, otherwise `false`:
- `--checkpoint` was passed
- this is the 5th snapshot since the last `checkpoint: true` one (count back along `previous-context`)
- the node reached DONE or HITL sign-off since the tail
- `--status final`
- the draft is over 150 lines

Nothing subjective.

## 5. Compose
Slug: `--slug` if given, otherwise derived from the work (kebab-case, `[a-z0-9-]`, ≤ 40 chars).
Write the draft to a temp file OUTSIDE the repo (`mktemp`), using exactly this shape:

```markdown
---
timestamp: <ISO UTC from step 1>
previous-context: <tail basename or null>
checkpoint: <true|false>
status: <in-progress|blocked|final>   # default in-progress
plan: <build|feat-NNN|bug-NNN|inv-NNN|null>
node: <F0a..F7|null>
phase: <analysis|plan|loop|hitl|merge>
step: <red|stub|green|refactor|review|security|investigate|rebase|qa|null>
attempt: <1-5|null>
branch: <branch>
worktree: <relative path|null>
head: <short sha>
tests: <X/Y|not-run>
next-action: <one line, ≤ 120 chars>
open-decisions: <count of bullets under Open decisions>
---

## Summary
<≤ 5 lines>

## Completed since last snapshot
- <item> (<sha>)

## Current state
<branch + head, tests X/Y, uncommitted count, blockers>

## Next action
<expands next-action>

## Carry-overs
- <deferred item for the next node: a P2 finding, debt, a follow-up> — <where it was raised>
  (/feature SETUP turns each into a task on that node, or David cuts it)

## Open decisions
- <decision> — <spec or question it belongs to>

## Decisions made
- <link to a spec's Decision section, e.g. .claude/specs/api-contract.spec.md#decision>; never restate it

## Pointers
- <plan file, docs/trace/<plan>/<node>/, PR number>
```

Every key and every section is present. Write `(none)` for an empty section and `null` for a key that doesn't apply.
Write only about this repo's work: nothing personal, relational or from a private workspace. Use links, never pasted trail or spec text.

## 6. Cap
`wc -l < draft`. If it's over 200, refuse: report the count, ask for a tighter draft, and write nothing.

## 7. Privacy
`bash .claude/hooks/privacy-scan.sh <draft>`. On exit 1, refuse: print each hit as `line: category` (the category is the denylist section the pattern sits under), delete the draft, and write nothing. Exit 2 is a setup error: report it and write nothing.

## 8. Write
Move the draft to `docs/context/<YYYYMMDD-HHMMSS>-<slug>.md` (create the folder if it's missing). Don't stage or commit it.
Report exactly:
`saved docs/context/<file> · <n>/200 lines · checkpoint: <true|false> · privacy: clean`

Committing is a separate act (C8): its own `docs(context): <slug>` commit on the current branch, never inside a red/green/refactor commit. From F0b, a snapshot taken on `main` stays uncommitted until the next feature branch.
