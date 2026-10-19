---
name: plan-bug
description: Draft a bug plan (docs/plans/active/bug-NNN.plan.md + bug-NNN.dag.yaml) for a defect found after merge - symptom, the broken rule, the introducing commit, a micro-DAG whose N1 t1 is the failing reproduction test. Main session only. Writes drafts in the main checkout; David approves; /feature runs it.
---

# /plan-bug "<symptom>" --rule <RULE-ID> [--parent inv-NNN]

Contract: planning.spec (The four plan-* skills, Plan file template, DAG file template, Task sequences, Who may
change what). Main session only; subagents never run this skill. It dispatches no agent, changes no code or test,
and writes exactly two files.

Use it for a defect **already merged into `main`**, including a comment on a merged PR. A defect found before
merge stays in `/feature`'s failure route.

## 0. Guards
Refuse with the reason, writing nothing, if any of these fails:
1. The session is in the **main checkout** on `main`, with a clean tree (`git status --porcelain` is empty).
2. **The symptom is observable:** one sentence naming the request or input, the observed result and the expected
   result ("`POST /policies` with `"policyholders":[null]` returns 500; strict input says 400"). Bounce a fix
   instruction ("null-check the mapper") and suggest a symptom wording.
3. **The rule exists:** `--rule` is an id in a rule row of `docs/requirements.md`
   (`grep -n '^| <RULE-ID> |' docs/requirements.md`). A defect against a requirement with no rule id (for example
   the Errors section) names the nearest rule whose input it concerns, and the Why cites the requirement's line.
4. **It reproduces on `main`.** Record the command and its output that show the symptom (a `curl` against the
   API, a test filter, a Postman request) and its date. No reproduction, no plan: report what was tried.
5. With `--parent inv-NNN`: that plan exists and has an accepted `{kind: bug}` recommendation for this rule.
6. No other `bug-NNN` or `feat-NNN` plan in `docs/plans/active/` has a node in flight (one feature at a time).

## 1. Id and clock
- NNN = 1 + the highest `bug-NNN` in `docs/plans/active/` and `docs/plans/archive/`, padded to 3 digits
  (`bug-001` when none exists).
- `date -u +%Y-%m-%d` → `created`. `git log -1 --format=%h -- docs/requirements.md` → `requirements_rev`.

## 2. Prior work
Record each hit as `path: one line`, or `none`:
- `ls docs/plans/active docs/plans/archive 2>/dev/null`
- `grep -rn -i -E '<rule id>|<key nouns from the symptom>' docs/plans docs/lessons.md 2>/dev/null`

## 3. Where it came from
Find the introducing plan, node and commit from the trail, not from memory:
- the file and line where it fails (from the reproduction's stack trace or log, or by reading the code path);
- `git log --format='%h %s%n%(trailers)' -S '<the failing expression>' -- <file>` and `git blame -L <n>,<n> <file>`
  → the commit and its `Plan:`/`Node:` trailers;
- `docs/trace/requirements-matrix.md`: the rule's row (tests, red, green, QA step) and whether any test covers
  the failing input (`grep -rn` the input shape in `tests/`).
Write `parent: {plan: <plan>, node: <node>, rule: <RULE-ID>}`, or `parent: inv-NNN` with `--parent`.

## 4. Write `docs/plans/active/bug-NNN.plan.md`
````markdown
---
plan_id: bug-NNN
kind: bug
title: <short title>
created: <YYYY-MM-DD>
dag: bug-NNN.dag.yaml
parent: {plan: <plan>, node: <node>, rule: <RULE-ID>}
---
# bug-NNN: <title>
## Why
<the symptom; the reproduction command, its output and date; the requirement it breaks (rule id, and the
requirements.md line when the rule row doesn't state it)>
## Approach
<where it fails (file:line), the introducing commit and node, why the node's tests didn't catch it, and the
test-first shape of the fix: the reproduction test, then the layer that changes. No code>
## Rejected alternatives
- <at least one option weighed and not chosen, with the reason>
## Prior work consulted
<step 2's hits, or none>
## Attempt log
## Completion record
````
No status, approval or DAG in this file.

## 5. Write `docs/plans/active/bug-NNN.dag.yaml`
The DAG file template, `kind: bug`, `status: draft`, `approved_by: null`, `approved_on: null`, `parent` as in the
plan file. Usually one node:
- `id: N1`, `kind: bug`, `owner: loop`, `depends_on: []`, `rules: [<RULE-ID>]`, `cuttable: false`,
  `budget` from David's ask or `null`, `status: pending`, `attempt: 0`, `branch: null`, `worktree: null`,
  `pr: null`, `qa: docs/qa/bug-NNN-N1.md`, `trace: docs/trace/bug-NNN/N1/`.
- `acceptance`: the symptom's expected result as a checkable statement, plus "the reproduction test fails on the
  merge base and passes at the PR head", plus "the full suite and the rule-coverage check pass".
- `tasks`: the **feature** sequence. **t1 is the failing reproduction test**: `role: tester, step: red,
  layer: acceptance`, the node rule, titled "Reproduction: <symptom>; fails on main". Then builder `stub` only if
  a route is new (otherwise leave it out), david `pause1`, the red/green tasks for each layer the fix touches and
  no others (a layer with nothing to do is left out), builder `refactor`, orchestrator `gates`, `review`, tester
  `qa`, git `integrate`, orchestrator `trace`, git `pr`, david `pause2`, david `merge`.

## 6. Check
- `node .claude/skills/plan-dag/validate-dag.mjs docs/plans/active/bug-NNN.dag.yaml` exits 0. Fix every `error:`
  line and re-run; never leave an invalid plan.
- `bash .claude/hooks/privacy-scan.sh docs/plans/active/bug-NNN.plan.md docs/plans/active/bug-NNN.dag.yaml`
  exits 0.

## 7. Stop for David
Leave both files **uncommitted** in the main checkout (`main` is locked). Print, then end the run:

```text
BUG PLAN — bug-NNN <title> · rule <RULE-ID> · parent <plan>/<node> (<introducing sha>)
Symptom:   <one line>
Repro:     <command> → <observed>   (<date>)
Plan:      docs/plans/active/bug-NNN.plan.md · bug-NNN.dag.yaml (status: draft)
N1 tasks:  t1 red reproduction · … · merge
Reply:     approve  |  change: <what>
```

- `change: …` → edit the drafts, re-run step 6, and re-print the block.
- `approve` → set `status: approved`, `approved_by: david`, `approved_on: <date -u +%Y-%m-%d>` in the `.dag.yaml`,
  exactly as David said, and re-run step 6. Then `/feature bug-NNN N1` takes over.

## 8. The approval commit (run by `/feature` SETUP step 2b)
`main` is locked, so the plan reaches `main` through the node's PR. After the git agent's `setup` and
`EnterWorktree`, before the `pending → in_progress` commit:
1. Copy `bug-NNN.plan.md` and `bug-NNN.dag.yaml` from the main checkout into the worktree's `docs/plans/active/`.
2. Re-run step 6 there.
3. Commit both, staged by path, as `plan(bug-NNN): approved by David`, with the trailers `Agent: main`,
   `Plan: bug-NNN` and `Node: N1`, quoting David's `approve` in the body. This is David's approval commit on the
   plan's first node branch.
4. Delete the two copies from the main checkout, so the merge can bring them back without a clash.

The same steps apply to a `feat-NNN` plan.

## Never
- Change code, tests or another plan, or write anything but the two plan files.
- Approve the plan for David, or write a plan without a reproduction, a rule id or a rejected alternative.
- Commit on `main`, or dispatch any agent.
