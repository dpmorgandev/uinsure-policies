---
name: plan-investigation
description: Read-only investigation of one falsifiable question - docs/plans/active/inv-NNN.plan.md with question, hypothesis, evidence, findings and recommendations (feature | bug | amend-node) for David to accept or reject. Never changes code, never writes a DAG. Main session only; it is /feature's attempt-3 escalation, or David's own question.
argument-hint: "\"<falsifiable question>\" [--parent <plan>/<node>] [--budget 30m]"
---

# /plan-investigation

Contract: planning.spec (The four plan-* skills, Plan file template, Retry ladder, Lessons).
Main session only; subagents never run this skill. It dispatches only the read-only investigator, writes one
file, and changes no code, test or DAG node.

## 0. Guards
Refuse with the reason, writing nothing, if any of these fails:
1. **The question is falsifiable.** It is one question ending in `?` that names an observable behaviour and
   the condition under which it happens, so evidence can answer it yes or no ("Why does the reviewer FAIL
   build/F2 at attempt 2, and is the cause in the code or in the tests?"). Bounce an instruction ("fix the
   hasClaims bug"), a goal ("make F2 pass") or a bundle of questions, and suggest one falsifiable rewording.
2. **Parent, when escalating** (`--parent <plan>/<node>`, set by `/feature` at attempt 3 or on a repeated
   fingerprint):
   - The session is in that node's worktree (`git rev-parse --show-toplevel` ends in
     `.claude/worktrees/<plan>-<node>`), on its branch.
   - In the worktree's `<plan>.dag.yaml` the node is `status: investigating`.
   - The node's `loop.jsonl` has an `investigation.opened(inv-NNN)` event; that NNN is this plan's id.
3. **Standalone** (no `--parent`): the session is in the main checkout on `main`, and NNN is allocated in step 1.
4. `docs/plans/active/inv-NNN.plan.md` doesn't exist yet.

## 1. Id and clock
- Escalation: the id comes from the `investigation.opened(inv-NNN)` event (`/feature` allocated it).
- Standalone: NNN = 1 + the highest `inv-NNN` in `docs/plans/active/` and `docs/plans/archive/`, padded to 3
  digits (`inv-001` when none exists).
- Run `date -u +%Y-%m-%dT%H:%M:%SZ` and note it as `started`. The time box is `--budget`, default **30m**.

## 2. Prior work
Record each hit as `path: one line`, or `none`:
- `ls docs/plans/active docs/plans/archive 2>/dev/null`
- `grep -rn -i -E '<rule ids and key nouns from the question>' docs/plans docs/lessons.md 2>/dev/null`
- When escalating, the parent's trail: the one-line history of `loop.jsonl` (every `attempt++`, `verdict(…)`,
  `findings(…)`, `steer(…)` and failed `gate.*`), each `findings/*.md`, and each `verdicts/*.json`.

## 3. Hypothesis
Before any dispatch, write one hypothesis that the evidence could disprove, and what would disprove it.
It is a claim about cause ("hasClaims is canned in the Api mapping, and no test seeds true"), never a fix.

## 4. Evidence: dispatch the investigator (once)
One `Agent` call, `subagent_type: investigator`. The prompt carries only:
- `plan_id`, `node_id`, `attempt` (the parent's current attempt; standalone: `plan_id: inv-NNN`,
  `node_id: none`, `attempt: 0`, and the hook files nothing, so copy each finding's id, cause and file:line
  into Findings yourself)
- `source`: the source of the FAIL that opened the investigation (`reviewer`, `security`, `gate`, `david` or
  `pr-comment`); standalone: `david`
- `source_ref`: the verdict file(s) of that round (`docs/trace/<plan>/<node>/verdicts/A<n>-<agent>.json`), the
  gate event, or the comment id. The SubagentStop hook files the findings under the round it names
  (`findings/A<n>.md`), so name the round's verdict files explicitly.
- the question, the hypothesis, the evidence paths (verdicts, findings, the review packet), and the one-line
  history from step 2
- "Diagnose every problem in the source, and say for each whether it recurs from a previous attempt (same
  fingerprint) and why the previous route did not prevent it."

Never pass spec text or `.claude/specs/` paths. The investigator's JSON is a report, not a decision.
When escalating, record `findings(<findings file>)` in the parent's `loop.jsonl` with
`node .claude/scripts/render-trace.mjs event '<json>'` (`source: orchestrator`).

If the time box runs out before the evidence answers the question, stop digging: write the plan with the
question unanswered, say so in Findings, and recommend the next falsifiable question.

## 5. Write `docs/plans/active/inv-NNN.plan.md`
Exactly this shape. Write about this repo's work only, and link to the trail instead of pasting it.

````markdown
---
plan_id: inv-NNN
kind: investigation
title: <short title>
created: <YYYY-MM-DD from step 1>
dag: null
parent: <plan>/<node> | null
---
# inv-NNN: <title>
## Question
<the falsifiable question, one line, ending in ?>
## Hypothesis
<step 3's hypothesis, and what would disprove it>
## Evidence
- <path or event: what it shows>
## Findings
<answer the question yes or no first. Then one line per finding: id, root cause with file:line, recurring or
new. Link the findings file; never paste it>
## Recommendations
```yaml recommendations
- {kind: amend-node, target: <plan>/<node>, reason: "<one line: what the node's next attempt does differently>", status: proposed}
```
## Why
<what opened it: the attempt-3 stop on <plan>/<node>, a repeated fingerprint, or David's question>
## Approach
<read-only: the trail, the investigator's diagnosis, the time box and whether it held>
## Rejected alternatives
- <at least one option weighed and not recommended, with the reason>
## Prior work consulted
<step 2's hits, or none>
## Attempt log
<escalation: one line per attempt of the parent: attempt n · verdict or gate ref · cause>
## Completion record
<empty; appended at archive>
````

**Recommendations.** One line each, every one `status: proposed` (only David moves a status):
- `amend-node`: the revised approach for the parent node's next attempt. `target` is the parent. Say what
  changes against the previous attempts (the tasks to add or reorder, the test to write first, the cause to
  remove), not just "fix it". Only an escalation can recommend one.
- `feature` / `bug`: work that belongs in a new plan (`/plan-feature`, or `/plan-bug` for a defect already
  merged). `target: null`, because the plan doesn't exist yet; a bug names its `rule`.
- A finding with no recommendation is either fixed by an `amend-node` or named in Rejected alternatives.

## 6. Check
- `node .claude/skills/plan-dag/validate-dag.mjs docs/plans/active/inv-NNN.plan.md` exits 0. Fix every
  `error:` line and re-run; never commit an invalid plan.
- `bash .claude/hooks/privacy-scan.sh docs/plans/active/inv-NNN.plan.md` exits 0.

## 7. Commit
- **Escalation:** on the parent's node branch, in its worktree. `git add docs/plans/active/inv-NNN.plan.md`
  (plus the parent's `docs/trace/**` if the findings event is new), then a commit titled
  `docs(inv-NNN): <title>` with the trailers `Agent: main`, `Plan: <plan>` and `Node: <node>`. It reaches
  `main` with the node's PR.
- **Standalone:** leave the file uncommitted in the main checkout and dispatch `git` to open a
  `chore/inv-NNN` branch and PR with it (each push and `gh pr create` prompts David). David merges.

## 8. Stop for David, then hand back
Print, then end the run:

```text
INVESTIGATION — inv-NNN <title> · parent <plan>/<node> | none · <started> + <elapsed> of <budget>
Question:  <question>
Answer:    <yes | no | unanswered> — <one line>
Plan:      docs/plans/active/inv-NNN.plan.md
 # | kind        | target          | reason
 1 | amend-node  | <plan>/<node>   | <reason>
Reply:     accept <n…> / reject <n…>   (every recommendation needs one)
```

On David's reply, set each recommendation's `status` to `accepted` or `rejected` exactly as he said,
re-run step 6, and commit as `docs(inv-NNN): recommendations decided`, quoting his reply in the body. While
any recommendation is still `proposed`, the node stays `investigating` and `/feature` re-prints this block.

**Hand-back to `/feature`** (escalation, once every recommendation is decided):
1. Commit the node `investigating → in_progress` (`plan(<plan>/<node>): investigating → in_progress`) and
   record `state(in_progress)`. `attempt` is unchanged: only a FAIL counts.
2. Run `/feature`'s failure route for the round that opened the investigation (§5, steps 1–4). Each dispatch
   (investigator, tester, builder) also gets the path `docs/plans/active/inv-NNN.plan.md` and the accepted
   `amend-node` reasons, so the next attempt follows the investigation's approach.
3. The next reviewer, security or gate FAIL makes `attempt` 4: the route again, still briefed by the accepted
   `amend-node`. Attempt 5 stops the node as `escalated`.
4. An accepted `feature` or `bug` recommendation waits for David to run `/plan-feature` or `/plan-bug` with
   `parent: inv-NNN`; when that plan exists, add `opened: [<plan-id>]` to this plan's frontmatter.
5. All rejected and no `amend-node`: ask David whether the node continues on the plain route, is cut, or is
   taken over. A cut or an attempt ≥ 3 needs a lesson at archive.

## Never
- Change code, tests, a DAG node or a task, or write anything but `inv-NNN.plan.md` and the trail event.
- Accept or reject a recommendation for David, or write one without a one-line reason.
- Dispatch any agent but the investigator, or pass it spec text.
- Write a plan without a falsifiable question or a rejected alternative.
