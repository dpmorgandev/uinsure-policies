---
name: feature
description: >-
  Run the feature loop for one plan node: walk its DAG tasks, dispatch the six agents, run every gate
  yourself with receipts, apply the retry ladder, and stop at David's two pauses. Main session only.
argument-hint: <plan-id> <node-id>
---
# /feature <plan-id> <node-id>

You are the **orchestrator**, in the main session. You dispatch agents, run gates via Bash and record
receipts. You never write `src/` or `tests/` yourself, and you never take an agent's word for a result.
**A gate with no receipt is BLOCKED, never PASS.**

## 0. Preconditions (refuse with the reason if any fails)

1. This session was launched in the **main checkout**, which supplies the harness in force.
2. `node .claude/skills/plan-dag/validate-dag.mjs docs/plans/active/<plan>.dag.yaml` exits 0, and the plan
   `status` isn't `draft`.
3. **Pick the node.**
   - Resume any node in `in_progress | paused_acceptance | investigating | hitl | escalated`. Only one
     feature runs at a time.
   - Otherwise take the requested node only if it's the next one: the first `pending` node in file order
     whose `depends_on` are all `done` and whose `owner` isn't `david`.
   - F0a is not run by this loop.
4. **Next task** = the first task whose `status` isn't `done` or `skipped`. Tasks run strictly in order.
5. If a worktree `.claude/worktrees/<plan>-<node>` exists, `EnterWorktree({path})` and go to **Resume** (§9).

## 1. Recording (every step)

- **Events:** `node .claude/scripts/render-trace.mjs event '<json>'` appends to
  `docs/trace/<plan>/<node>/loop.jsonl`. Never write the file by hand.
  - Gate events carry `{event, cmd, exit, expect, ok, head}`.
  - Event names: `setup`, `state(<S>)`, `gate.<name>`, `pause1`, `verdict(<agent>,<PASS|FAIL>,<file>)`,
    `findings(<file>)`, `attempt++`, `steer(D<n>,<pause1|pause2|pr-comment>)`, `investigation.opened(inv-NNN)`,
    `escalated`, `gate.integrate`, `trace.rendered(<ok|incomplete>)`, `pr(<n>)`, `pr.comment(<kind>,<id>)`,
    `gate.postman(<pass|fail|skipped>)`, `signoff(<sha>)`, `merged(<sha>)`, `archived`.
- **Plan state:** node `status`, `attempt`, `branch`, `worktree`, `pr` and task `status` change only in
  the worktree copy of `<plan>.dag.yaml`, as commits on the node branch titled
  `plan(<plan>/<node>): <from> → <to>`, with the trailers `Agent: main`, `Plan:` and `Node:`.
  Mark a task `done` only after its gate passes.
- `HEAD` = `git rev-parse HEAD`. Note it before each dispatch as `task_start`.

## 2. Dispatching agents

- Use the `Agent` tool with `subagent_type` = the role. Run **one agent at a time**. The only exception is
  reviewer ∥ security: two `Agent` calls in one message.
- The prompt carries only `plan_id`, `node_id`, `attempt`, `task` id and title, `step`, `layer`, the rule
  IDs, and the paths the role needs (findings file, review packet, PR body file, verdict files).
  **Never spec text and never spec paths.** Agents read rule rows from `docs/requirements.md` themselves.
- An agent's final JSON is a report. Run its gate before believing anything in it.

## 3. Tasks: (role, step) → what you do and the gate

**SETUP** (before t1, once)
1. Dispatch `git` with `op: setup`, then `EnterWorktree({path: ".claude/worktrees/<plan>-<node>"})`.
2. Gate: the branch name and worktree path match; `git rev-parse --show-toplevel` = the worktree.
3. Commit the plan status `pending → in_progress`, `attempt: 1`, `branch` and `worktree`.
   Record `setup` and `state(in_progress)`.

**`tester / red`, layer `acceptance` (t1)**
- Dispatch the tester. Then run `gate.red`:
  - `dotnet build Uinsure.Policies.slnx -warnaserror` exits 0.
  - Each node rule's `dotnet test --solution Uinsure.Policies.slnx -- --filter-trait "Rule=<ID>"` exits
    non-zero (`expect: fail`).
  - `git diff --name-only <task_start>..HEAD` ⊆ `tests/**` plus `src/**`. The `src/` lines are
    throw-only stubs; the hook enforces this, and you re-check that no `src/` line was removed.
  - Every node rule has a `[Trait("Rule","<ID>")]` in Api.Tests (grep).

**`builder / stub` (api)**
- Dispatch. Then `gate.stub`:
  - The build passes.
  - Start `dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080` in the background,
    and the node's routes answer with their happy-path status (curl). Stop the API.
  - The acceptance rule filters still fail.
  - The diff ⊆ `src/Uinsure.Policies.Api/**`.

**`david / pause1`**: §6, Pause 1.

**`tester / red`, layer `domain` | `services`**
- Dispatch. Gate: the build passes; the task's rule filters fail **in that layer's test project**
  (`dotnet test --project tests/Uinsure.Policies.<Layer>.Tests -- --filter-trait "Rule=<ID>"`); the diff
  ⊆ `tests/**` plus stubs.

**`builder / green`, per layer**
- Dispatch with `layer`. Then `gate.green`:
  - The build passes.
  - `domain` / `services`: the task's rule filters pass in that layer's test project.
  - `persistence`: the build plus all unit tests pass.
  - `api`: **the whole Api.Tests acceptance suite passes**.
  - The diff ⊆ `src/**` plus the root build files.

**`builder / refactor`**
- Dispatch. Gate: the full `dotnet test --solution Uinsure.Policies.slnx` passes, and the diff ⊆ `src/**`.

**`builder / write`** (evidence or docs nodes)
- Dispatch with the task title. Gate: the node's acceptance commands from the DAG exit 0.

**`orchestrator / gates`**: run each, recording `gate.<name>`. All must exit 0:
- `dotnet build Uinsure.Policies.slnx -warnaserror`
- `dotnet test --solution Uinsure.Policies.slnx` (it includes the rule-coverage check)
- diff scope: `git diff --name-only origin/main...HEAD` ⊆ the node's allowed paths (`src/**`, `tests/**`,
  `docs/qa/**`, `docs/trace/**`, the plan file, the root build files)
- `grep -rn "NotImplementedException" src/` finds nothing

**`review / review`**
1. Write the review packet to `docs/trace/<plan>/<node>/packet/`: `diff.patch`
   (`git diff origin/main...HEAD`), `trailers.txt`
   (`git log --reverse --format='%H %s%n%(trailers)' origin/main..HEAD`), and `gates.jsonl` (this
   attempt's gate events).
2. Compute `base_sha` = `git merge-base origin/main HEAD`, and
   `diff_fingerprint` = `git diff <base>...HEAD | git patch-id --stable`.
3. Dispatch reviewer ∥ security in one message, each given the node rules, `base_sha`, `head_sha`,
   `diff_fingerprint`, `attempt` and the packet path. Neither sees the other's verdict.
4. The SubagentStop hook validates each verdict and writes `verdicts/<round>-<agent>.json`. Record
   `verdict(…)` for both.
5. Both `PASS` with the current `head_sha` → next task. Any `FAIL`, or a verdict missing or stale →
   **failure route** (§5).

**`tester / qa`**: dispatch. Gate: `docs/qa/<plan>-<node>.md` exists, and every node rule appears as `[RULE-ID]`.

**`git / integrate`**
- Dispatch with `op: integrate`. Re-run the full suite yourself on the post-rebase HEAD and record
  `gate.integrate {head_sha, passed, failed}`.
- A conflict → stop and show David the `conflict_files`. **Any later commit invalidates this receipt.**

**`orchestrator / trace`**
- `node .claude/scripts/render-trace.mjs render <plan> <node>`, then `… check <plan> <node>`. Record
  `trace.rendered(ok|incomplete)`. `incomplete` is BLOCKED: fix the missing link, don't paper over it.
- Dispatch `git` to commit `docs/trace/**` as `chore(trace): <node> trail`. That's the only commit allowed
  after `gate.integrate`.

**`git / pr`**
1. `node .claude/scripts/render-trace.mjs pr-body <plan> <node> > <tmp>/pr-body.md`.
2. **Stop until David has written his paragraph** in that file. Never write it for him.
3. Commit the plan status `in_progress → hitl`. Dispatch `git` with `op: pr` (push and `gh pr create
   --draft` each prompt David), then `op: comment` for the three comments. Record `pr(<n>)` and
   `pr.comment(…)` ×3.

**`david / pause2`**: §6, Pause 2. **`david / merge`**: §8.

## 4. Gate failures

- A failed gate on a producer task (red, stub, green, refactor, write) or on `gates` → record
  `gate.<name>` with `ok: false` → **failure route** with `source: gate` and `source_ref` = the event.
- A red gate where the tests **pass** is a failure too: the test isn't proving anything.

## 5. Failure route and retry ladder

**Counting.** Only reviewer, security or gate FAILs increment `attempt`, with a plan commit and an
`attempt++` event. David's steers (`D<n>`) take the same route and **never** increment it.

| `attempt` becomes | Action |
|---|---|
| 1–2 | Run the route below. |
| 3, **or** a finding fingerprint `(rule_id, file, sha1(what))` equal to one from the previous attempt | **Stop.** Set the node `investigating`, record `investigation.opened(inv-NNN)`, and open `/plan-investigation` with `parent: <plan>/<node>`. The run ends until David decides. |
| 4 | The route, briefed by the accepted `amend-node`. |
| 5 | **Stop.** Set the node `escalated`, record `escalated`, and ask David: amend the plan, cut the node, or take it over. |

**The route** (round id `A<n>` for a FAIL, `D<n>` for a steer):
1. Dispatch the `investigator` with `source`, `source_ref`, the evidence paths, the packet path, and the
   one-line history of previous attempts from `loop.jsonl`. The hook renders `findings/<round>.md`.
   Record `findings(…)`.
2. Dispatch the `tester` with `step: finding-red` and the findings path. Gate: each new test **fails** on
   the current HEAD. An untestable finding needs its stated reason.
3. Dispatch the `builder` with the findings path and the red tests. Gate: those tests now pass, and the
   build passes.
4. Return to `orchestrator / gates`, then `review / review`. The route runs inside the current task; it
   is never a planned task.

## 6. Pauses (each ends the run; the next `/feature` resumes)

**Pause 1** (`david / pause1`): commit `in_progress → paused_acceptance`, record `pause1`, and print:

```text
PAUSE 1 — <plan>/<node> <title> · attempt <n> · HEAD <short sha>
Acceptance suite: RED (<failing>/<total>) · stub: callable
Worktree: .claude/worktrees/<plan>-<node>    (open Uinsure.Policies.slnx in VS 2026 if you like)
Run:      dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080
Postman:  environment local (baseUrl=http://localhost:5080), folder "<node folder>"
Tests:    dotnet test --solution Uinsure.Policies.slnx
Reply:    go  |  change: <what>
```
- `go` → commit `paused_acceptance → in_progress`, mark the task done, and continue.
- `change: …` → steer `D<n>` (`source: david`), then the failure route.

**Pause 2** (`david / pause2`):
1. **Postman receipt first.** Start the API from the worktree on port 5080 (delete the worktree's `*.db`
   first) and run
   `postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "<node folder>"`.
   Stop the API. Record `gate.postman(pass|fail)` with `cmd`, `exit` and `head`.
2. Print the Pause 1 block, plus `walkthrough.md`, the QA script path, the PR URL and the `gate.postman`
   result. A failure is shown, never hidden; David decides.
3. Replies:
   - `sign-off` → record `signoff(<HEAD>)`. HEAD must equal the integrate and PR head.
   - `defect: …` → steer `D<n>`, then the failure route, then re-run integrate, trace, push and
     comments, and Pause 2 again.

## 7. PR comments (any time the PR is open)

1. Dispatch `git` with `op: read-comments`. It returns only `dpmorgandev`'s comments.
2. Each unanswered comment → steer `D<n>` (`source: pr-comment`, comment id) → the failure route on this branch.
3. Then gates, review, integrate and trace again; `git` pushes (prompts David) and replies with the fix SHA.
   Record `pr.comment.fixed`. New commits require Pause 2 again.
4. A comment on an already **merged** PR isn't handled here: it becomes `/plan-bug`.

## 8. Merge and archive

1. After `signoff`, commit the node `hitl → done` on the branch, and have `git` push it (David is prompted),
   so `done` reaches `main` only through the merge.
2. **David merges** in the GitHub UI, with a merge commit. Detect it with `gh pr view <n> --json
   state,mergeCommit` and record `merged(<sha>)`. You never merge.
3. `ExitWorktree({action: "keep"})`, then dispatch `git` with `op: cleanup`. A lesson is required if
   `attempt ≥ 3`, the node was escalated, or it was cut.
4. When every node is `done` or `cut`, dispatch `git` with `op: archive`. Record `archived`.

## 9. Resume

- State is **derived**: the last `loop.jsonl` event, reconciled with
  `git log --format='%H %s%n%(trailers)' origin/main..HEAD`. If they disagree, **git wins**: backfill the
  missing event with `source: reconciled`.
- At a pause, re-print its block and wait.
- Reuse a gate result only if its `head` equals the current HEAD. Otherwise re-run it.

## Never

- Write `src/`, `tests/` or `docs/qa/` yourself, or edit an agent's output to make a gate pass.
- Mark a gate PASS without its receipt, or accept a producer's claim instead of running the gate.
- Pass spec text or `.claude/specs/` paths to an agent.
- Merge, push to `main`, use `--no-verify`, or write David's PR paragraph.
- Run two producer agents at once, or start a second node while one is in flight.
- Increment `attempt` for a David steer, or continue past attempt 3 or 5 without David.
