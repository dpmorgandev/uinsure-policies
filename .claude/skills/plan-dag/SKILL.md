---
name: plan-dag
description: Draft the build plan, docs/plans/active/build.plan.md (F0a to F7), from the planning Contract and requirements.md, after asking David once. Main session only, run once in F0a. Writes a draft; David approves it by his own commit on main.
argument-hint: ""
---

# /plan-dag

Contract: planning.spec (Files and ids, Plan file template, The build DAG, Cut order, The four plan-* skills).
Main session only; subagents never run this skill. It writes one file and never stages or commits.

## 0. Guards
Run from the repo root (`git rev-parse --show-toplevel`). Refuse with the reason, writing nothing, if:
- `docs/plans/active/build.plan.md` or `docs/plans/archive/build.plan.md` exists ("build plan already exists: <path>"; it runs once).
- the branch isn't `main` (the build plan is approved by David's commit on `main`).
- `.claude/specs/planning.spec.md` or `docs/requirements.md` is missing.
- the tree has uncommitted changes under `docs/requirements.md` or `.claude/specs/` (the defaults must come from committed text).

## 1. Prior work
Search what earlier plans and lessons already say. Record each hit as `path: one line`, or `none`:
- `ls docs/plans/active docs/plans/archive 2>/dev/null`
- `grep -n -i -E 'build|F0a|F0b|walking skeleton' docs/plans -r 2>/dev/null`
- `cat docs/lessons.md 2>/dev/null`

## 2. Defaults
Read these from committed text, and never from memory:
- **The DAG:** the single ```` ```yaml dag ```` block under `### The build DAG` in planning.spec, plus the note after it (status fields omitted, file order = schedule).
- **The cut order** (`### Cut order`), and the budget question still open in planning.spec `## Open questions`.
- **The template** (`### Plan file template`) and the named task lists.
- **Rejected alternatives:** the planning.spec `## Options` entries that the Decision did not choose and that shape this plan. Examples are C1/C3 (one F0, or renumbering), D2 (Retrieve before Sell), A4 (a separate manifest), and React inside this plan. Write each as one line with the reason in your own words.
- **Rule ids:** `grep -oE '^\| [A-Z]+-[0-9][0-9A-Za-z/]* \|' docs/requirements.md`.

Before asking, cross-check the mapping and list every gap you find:
- a requirements rule id that no node's `rules:` lists. Every id in a table row is its own rule, so lettered children don't cover a parent that has its own row (e.g. `COULD-2` vs `COULD-2a..c`)
- an id in `rules:` that isn't in requirements.md (a hard stop: it must be added there before the plan can pass)
- a node rule that its acceptance never exercises

## 3. Ask David once
Print the defaults first, in at most 40 lines:
- the node table: id · title · kind · owner · depends_on · rules · budget · cuttable
- the cut order
- the gaps from step 2
- the prior-work hits

Then make **one** `AskUserQuestion` call with these four questions. Put the default first in each, labelled "(Recommended)".
1. **Order and mapping:** feature list, order and rule → node mapping as shown / changes (Other).
2. **Budgets and cut line:** a time box per node, and the clock times on Monday 2026-10-19 at which F7, then F5, is cut. Default: F5 3h and the rest null. The cut time has no default, so offer two concrete options and Other.
3. **Deferred or extra:** nothing deferred and no extra acceptance / the gaps from step 2 resolved as listed / changes (Other).
4. **Write the draft:** write `build.plan.md` now / stop.

Apply the answers literally. A change that touches a rule id that isn't in requirements.md stops the skill: report it, and say that requirements.md needs the id first. If David picks "stop", write nothing.

Don't ask again. If an answer is ambiguous, apply the default, then name the assumption in the step 6 report.

## 4. Compose
Write the draft to a temp file outside the repo (`mktemp`), following the plan template exactly.
- **Frontmatter:**
  - `plan_id: build`, `kind: build`, `title: Uinsure Policies API build`, `status: draft`
  - `created:` from `date -u +%Y-%m-%d`
  - `approved_by:` and `approved_on:` both empty (David's alone)
  - `requirements_rev:` from `git log -1 --format=%h -- docs/requirements.md`
  - `parent: null`
- **## Why:** at most 6 lines. What the build delivers, the Monday 2026-10-19 deadline, and that this plan runs once and closes when the system exists.
- **## Approach:** at most 10 lines.
  - F0a is session-built harness; F0b is the first loop run.
  - One node per feature, outside-in TDD inside it.
  - Two pauses per loop node.
  - File order is the schedule.
  - The cut order.
- **## Rejected alternatives:** at least one `- ` bullet, from step 2.
- **## Prior work consulted:** the step 1 hits, or `none`.
- **## DAG:** the build DAG with David's answers applied, and every node fully expanded:
  - `status: pending`, `attempt: 0`
  - `branch: null`, `worktree: null`, `pr: null`
  - `qa: docs/qa/build-<id>.md`, `trace: docs/trace/build/<id>/`
  - Key order follows the template.
  - Acceptance items are double-quoted strings.
  - Keep the comments out of the block. The template's comments are documentation, not plan content.
- **## Attempt log** and **## Completion record:** headings only, each followed by `(none)`.

## 5. Validate
`node .claude/skills/plan-dag/validate-dag.mjs <draft> docs/requirements.md`. Copy the draft to a temp path named `build.plan.md` first, because the validator matches `plan_id` against the file name.
- **Exit 1:** print every `error:` line, fix the draft, and re-run. If an error comes from David's answers rather than your composing, stop. Report the error and write nothing. Never weaken the draft to pass, for example by dropping a rule or an acceptance item.
- **Exit 2:** a setup error. Report it and write nothing.
- **Then** run `bash .claude/hooks/privacy-scan.sh <draft>`, because the plan lands in a public repo. On exit 1, print each hit as `line: category`, and write nothing.

## 6. Write and report
Create `docs/plans/active/` if it's missing, then move the draft to `docs/plans/active/build.plan.md`. Don't stage or commit it. Report:

```
wrote docs/plans/active/build.plan.md · <n> nodes · <r> rules · dag: ok · privacy: clean
assumptions: <each applied default on an ambiguous answer, or none>
gaps carried: <each step 2 gap David left open, or none>
```

Then say what approval is, and stop. David reviews the file, sets `approved_by`, `approved_on` and `status: approved` himself, and commits on `main` as `plan(build): approve`. The skill never sets those fields, never commits, and never starts a node.
