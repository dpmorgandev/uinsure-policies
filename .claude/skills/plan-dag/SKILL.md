---
name: plan-dag
description: Draft the build plan from the planning Contract and requirements.md, after asking David once - docs/plans/active/build.dag.yaml (the DAG with explicit tasks the orchestrator loops through) and build.plan.md (the reasoning). Main session only, run once in F0a. Writes drafts; David approves by his own commit on main.
argument-hint: ""
---

# /plan-dag

Contract: planning.spec (Files and ids, Plan file template, DAG file template, Task sequences, The build DAG, Cut order, The four plan-* skills).
Main session only; subagents never run this skill. It writes two files and never stages or commits.

## 0. Guards
Run from the repo root (`git rev-parse --show-toplevel`). Refuse with the reason, writing nothing, if:
- any of `docs/plans/{active,archive}/build.{plan.md,dag.yaml}` exists ("build plan already exists: <path>"; it runs once).
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
- **The nodes:** the single ```` ```yaml ```` block under `### The build DAG` in planning.spec. It holds every node with its explicit tasks. Copy it **verbatim**, and don't recompose tasks the spec already lists.
- **The header:** the plan-level keys of `### DAG file template`.
- **The task sequences** (`### Task sequences`), used only if David's answers change a node's tasks.
- **The cut order** (`### Cut order`), and the budget question still open in planning.spec `## Open questions`.
- **Rejected alternatives:** the planning.spec `## Options` entries that the Decision didn't choose and that shape this plan, such as C1/C3, D2, A4 (with the amendment's note that one DAG file is still one source of truth) and React inside this plan. Write each as one line with the reason in your own words.

Build the default draft in a temp dir outside the repo (`mktemp -d`): `build.dag.yaml` = header (`status: draft`, approval keys `null`) + the copied nodes, and a stub `build.plan.md` that has only the frontmatter and section headings. Run `node .claude/skills/plan-dag/validate-dag.mjs <tmp>/build.dag.yaml docs/requirements.md`. Any `error:` here is a defect in the committed spec: report it with the gaps below and ask about it in step 3.

Then list every gap:
- **Unmapped rules:** requirements ids that no node lists. `grep -oE '^\| [A-Z]+-[0-9][0-9A-Za-z/]* \|' docs/requirements.md | tr -d '| ' | sort -u`, compared with the union of every node's `rules`. Every table row is its own rule; lettered children don't cover a parent that has its own row.
- **Unknown rules:** ids in `rules:` that aren't in requirements.md. This is a hard stop: requirements.md needs the id first.
- **Unnamed rules:** node rules that no acceptance line names. This is informational only, because the rule-coverage check still binds them.

## 3. Ask David once
Print the defaults first, in at most 40 lines:
- the node table: id · title · kind · owner · depends_on · rules · tasks (count) · budget · cuttable
- the cut order
- the validator result
- the gaps
- the prior-work hits

Then make **one** `AskUserQuestion` call with these four questions. Put the default first in each, labelled "(Recommended)".
1. **Order, mapping and tasks:** nodes, order, rule → node mapping and task lists as in the spec / changes (Other).
2. **Budgets and cut line:** a time box per node, and the clock times on Monday 2026-10-19 at which F7, then F5, is cut. Default: F5 3h and the rest null. The cut time has no default, so offer two concrete options and Other.
3. **Deferred or extra:** nothing deferred and no extra acceptance / each gap resolved as you propose / changes (Other).
4. **Write the drafts:** write both files now / stop.

Apply the answers literally. If a change adds or moves tasks, compose them from the task sequences, renumber the node's tasks `t1…` in run order, and keep every rule in t1 and in at least one green task. A change that touches a rule id that isn't in requirements.md stops the skill: report it, and say requirements.md needs the id first. If David picks "stop", write nothing.

Don't ask again. If an answer is ambiguous, apply the default, then name the assumption in the step 6 report.

## 4. Compose
Write both drafts into the step 2 temp dir.
- **`build.dag.yaml`:**
  - header: `plan_id: build`, `kind: build`, `title: Uinsure Policies API build`, `status: draft`
  - `created:` from `date -u +%Y-%m-%d`
  - `approved_by: null`, `approved_on: null`
  - `requirements_rev:` from `git log -1 --format=%h -- docs/requirements.md`
  - `parent: null`
  - then `nodes:` with David's answers applied
  - Keep the comments out of the file. Task statuses stay as the spec gives them, so a task the spec marks `done` (with its commit) stays `done`.
- **`build.plan.md`** (prose only, no DAG, no status):
  - frontmatter: `plan_id: build`, `kind: build`, `title`, `created`, `dag: build.dag.yaml`, `parent: null`
  - **## Why:** at most 6 lines. What the build delivers, the Monday 2026-10-19 deadline, and that this plan runs once and closes when the system exists.
  - **## Approach:** at most 12 lines.
    - `build.dag.yaml` is the only state, and the orchestrator walks each node's tasks in order.
    - F0a is session-built harness; F0b is the first loop run.
    - Each feature node runs outside-in per layer, tester red then builder green, with each task naming its rules.
    - The two pauses.
    - File order is the schedule.
    - The budgets, the cut order and the cut times.
  - **## Rejected alternatives:** at least one `- ` bullet, from step 2.
  - **## Prior work consulted:** the step 1 hits, or `none`.
  - **## Attempt log** and **## Completion record:** headings only, each followed by `(none)`.

## 5. Validate
`node .claude/skills/plan-dag/validate-dag.mjs <tmp>/build.dag.yaml docs/requirements.md`. It reads the sibling `build.plan.md` too.
- **Exit 1:** print every `error:` line, fix the draft, and re-run. If an error comes from David's answers rather than your composing, stop. Report the error and write nothing. Never weaken the draft to pass, for example by dropping a rule, a task or an acceptance item.
- **Exit 2:** a setup error. Report it and write nothing.
- **Then** run `bash .claude/hooks/privacy-scan.sh <file>` on both files, because the plan lands in a public repo. On exit 1, print each hit as `file:line: category`, and write nothing.

## 6. Write and report
Create `docs/plans/active/` if it's missing, then move both drafts there. Don't stage or commit them. Report:

```
wrote docs/plans/active/build.dag.yaml + build.plan.md · <n> nodes · <t> tasks · <r> rules · dag: ok · privacy: clean
assumptions: <each applied default on an ambiguous answer, or none>
gaps carried: <each gap David left open, or none>
```

Then say what approval is, and stop. David reviews both files, then sets `status: approved`, `approved_by` and `approved_on` in `build.dag.yaml` himself, and commits both on `main` as `plan(build): approve`. The skill never sets those fields, never commits, and never starts a node.
