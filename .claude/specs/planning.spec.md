# planning.spec.md
Status: DECIDED — contract final (2026-10-17)

Scope: the one plan format every code-changing plan uses (reasoning + YAML DAG), how the
orchestrator reads and advances it, a node's definition of done, the plan lifecycle and its
completion record, what `/plan-dag` asks David, the four `plan-*` skills, the investigation
→ recommended-plan handoff, the retry ladder, and lessons. Source: `docs/requirements.md` §3
"Planning model", §4 row 3.

## Dependencies

All decided 2026-10-17. This spec relies on:

| Spec | What this spec assumes | If the assumption breaks |
|---|---|---|
| `api-contract` | Features map onto endpoints: Sell = `POST /policies`, Retrieve = `GET /policies/{ref}`, Cancel = `POST /policies/{ref}/cancellation` (+ `GET …/cancellation-quote`, + `POST …/claims`), Renew = `POST /policies/{ref}/renewals`. Routes are owned there | Only acceptance strings change |
| `agents-skills` | Roles: `tester`, `builder`, `reviewer`, `security`, `investigator`, `git`. The orchestrator is the main session, not an agent. `david` is a valid owner | Rename the `role:` enum |
| `traceability` | Trails are keyed `docs/trace/<plan-id>/<node-id>/`. So plan ids and node ids must be **stable, short and filesystem-safe**, with no slugs and no renames | Keying on something else would let slugs back into the ids |
| `orchestration` | Consumes the DAG. It owns the loop, the verdict JSON, the worktree/branch naming and the HITL step. This spec only defines **what state the plan holds and who may change it** | Status names may change. The schema shape holds |
| `testing` | Supplies the QA-script path (`docs/qa/<plan-id>-<node-id>.md`) and the full-suite command that a node's done-check runs | Path string only |

## Research

### Prior art: agentflow_phase3 (`.claude/skills/plan-*`, `plans/`)

- **`plan-feature` (120 lines), `plan-bug` (114), `plan-investigation` (139), `plan-archive` (152).**
  Each plan is a markdown file with YAML frontmatter and prose sections (Problem, Approach,
  Rejected Alternatives, Expected Outcomes, Validation, Attempt Log). **There is no DAG in the plan.**
  Order and dependencies lived in a separate `feature_list.json` (rows with `depends_on`,
  `passes`, `attempt_count`, `evidence`, `closed_at`) plus a `plans/active.md` manifest table.
- **Worth porting:**
  - The **rejected-alternatives requirement** (`plan-feature` refuses to write a plan without one). This is the interview's "handling ambiguity" made mechanical.
  - The **falsifiable-question bounce** in `plan-investigation` ("fix the auth bug" is refused; "why does X fail when Y?" is accepted).
  - The **time box** on investigations (30 min default).
  - **Git-derived completion records** (`git log main..branch`, `git diff --name-status`), not self-reported ones.
  - **"Failed/partial plans must capture ≥1 lesson"** (`plan-archive` refuses otherwise).
  - The retry-policy comment block, which requirements.md already adopts.
- **Not worth porting at this size (about 6 features in 2 days):**
  - *Three sources of truth* (plan file + `feature_list.json` + `active.md`). They drift, and the receipts show it. `plans/active.md` still lists `investigate-022`, `investigate-020`, `investigate-023` and `bug-028` as `completed` but un-archived. Row `bug-033-symbol-motif-…` points at file `bug-033-screen-css-tokens-and-use-shadow-invisible.md` (the id and the filename diverged).
  - **Count-based ID generation** (`ls … | wc -l` + 1). Gaps and parallel series break it. The archive holds both `bug-028…033` and `bug-201…215`. Use max-existing + 1 instead.
  - **Slugs in ids.** They are unstable, so they break trace keys.
  - **Branch creation inside the planning skill.** Here the git agent owns branches and worktrees (requirements §3 Feature loop step 2).
  - The `check-existing-work` / `capture-lesson` / `consult-lessons` trio. requirements.md already folds these into "search `docs/plans/` + `docs/lessons.md` before writing a plan" plus an append at archive.
  - The 7-bullet lesson template and tag discipline. Too heavy for perhaps 3–8 lessons in total.
  - Statuses `superseded` / `parked` / priority `P0–P2`. There's no backlog to prioritise.

### Prior art: agentkinOS (`plan-dag@1`, ADR 0015, scaffold `06-plan-door.md`)

- `plan-dag@1` node = `{id, kind, depends_on, produces, acceptance, phase, run_stage, ui_surface}` plus `implements: [spec:…]`. Of those fields, **`id`, `kind`, `depends_on`, `acceptance` and an `implements`-style link (here: `rules:`)** are the useful core. `phase`, `run_stage` and `ui_surface` are OS-specific.
- **ADR 0015 records the failure this spec must avoid:** "Plan-dag completion (`executor runPlan`) returns an in-memory result — **no durable receipt at all**." Lesson: the DAG's `done` must be backed by a git fact (merge/commit), never by an in-memory or self-written flag alone.
- The "plan door" (search before open, refusals proven by provoking them, lesson-refusal on failed close) is the most-proven governance in the lineage (37/37 receipted opens). The **procedure** ports. The machinery (`plan.ts`, CheckReceipt tokens, fingerprints) does not.

### Constraints that shape the design

1. **Agents never touch `main` or David's checkout** (§3 Feature loop 2, Rules). So the orchestrator cannot write node status into `docs/plans/active/build.plan.md` on `main` mid-feature. Plan-state changes during a feature must be **commits on the feature branch, inside the worktree**, and they reach `main` when David merges. This is the main design consequence of the whole spec.
2. **Workflows take no user input mid-run** (§5). Every point that needs David (approval, attempt-3 decision, attempt-5 escalation, HITL) is therefore a point where the run **ends** and control returns to the session. The DAG must hold enough state to resume from there.
3. **TDD history must stay readable** (no commit-on-stop). Plan-status commits must be few and clearly labelled (`plan(build/F1): status → in_progress`) so they don't drown the red/green/refactor commits.
4. **Over-build guard:** everything must be used by F0 or F1. `/plan-dag` and the format are used by F0. The other three skills are specified here, built on first trigger.

### The draft feature list: two structural questions

**Q1: split F0?** It holds two different kinds of work.
- *Harness*: CLAUDE.md, `.claude/agents|skills|hooks|settings`, git hooks. **It cannot be built by the loop it builds**: there is no tester/builder/reviewer yet. It is session work, reviewed by David.
- *Walking skeleton*: solution + projects + layering, an architecture test, one integration test via `WebApplicationFactory`, a health/stub endpoint, a Postman collection + environment, SQLite wiring. This **is** loop-shaped work, and it is the right **first run of the loop**: a feature with near-zero domain risk that proves the tester → builder → reviewer/security → rebase → PR → QA → HITL → merge → archive pipeline end to end before Sell's six rules ride on it.
- Splitting gives each half an honest definition of done: harness done = "the loop can run". Skeleton done = "the loop did run, and here is its PR". Keeping one F0 forces one DoD over two different things.

**Q2: Retrieve before Sell?** Outside-in says an integration test should verify behaviour through the public API. "Sold a policy" is only observable through the public API if GET exists.

| Order | How Sell's integration test proves persistence | Cost |
|---|---|---|
| F1 Sell, then F2 Retrieve (draft) | Assert the 201 body + Location, then peek via `DbContext` in the test, or not at all until F2 | Test couples to persistence. The round-trip test only appears in F2 |
| **F1 Retrieve, then F2 Sell** | Retrieve is TDD'd against a policy seeded through the test fixture. Sell's tests are then pure API: POST → GET round trip | Fixture seeding couples to persistence on the *arrange* side only (common and defensible). GET-1 is one rule, so the first domain feature is small |
| Merge Sell + Retrieve into one F1 | POST → GET in one feature | Biggest first feature (7 rules), against "David must fully understand each feature before the next" |

A note against Retrieve-first: GET-1 returns terms, payments, refunds and claims. Building it first tempts modelling the whole aggregate before the rules that create it. Mitigation: Retrieve-first returns **only what exists at sale** (policy, policyholders, property, payment, a single term). Refunds and claims appear in the response as Cancel adds them, each extended by its own feature's tests.

### Rule placement the draft list gets wrong

- **CANCEL-5 (cancelling a renewed policy) can't be built in F3.** Renewed policies (RENEW-4, terms) don't exist until F4. CANCEL-5 belongs in **F4 Renew** as a task, with a `depends_on` F3. The DAG's `rules:` field makes this visible, which is a point in favour of a rule-keyed DAG.
- **F5 depends only on the skeleton**, not on F4. It is cuttable and time-boxed (§3). The DAG should say `cuttable: true` and carry the budget, so "cut F5" is a recorded decision rather than silent omission.

## Options

**A. Status source of truth**
- A1 *Plan YAML is the only state; the orchestrator edits it on `main`.* Simple, but it breaks Constraint 1.
- A2 *Plan YAML is the state; edits are commits on the feature branch and land at merge* (`main` shows a node as `done` exactly when its PR is merged). One source of truth, and git is the receipt. Mid-loop state (attempts, current task) lives in the worktree copy. **Recommended.**
- A3 *State derived purely from git* (branch exists → in progress, merged → done; no status field). Most honest, but loses attempts, escalation and cut state, and makes `/orient` compute rather than read.
- A4 *A separate `feature_list.json`/manifest.* Rejected: this is agentflow's drift, receipted above.

**B. DAG granularity**
- B1 *Node-level DAG, ordered task list inside each node* (edges only between features). This matches "one feature at a time": inside a node, TDD order is the plan. **Recommended.**
- B2 *Task-level DAG with edges between tasks.* More expressive, but useless when one tester/builder pair works sequentially in one worktree. Overkill.

**C. F0**
- C1 Keep one F0.
- C2 **Split into F0a harness (`owner: david`, session-built, no loop) and F0b walking skeleton (first loop run).** Recommended. It keeps the F-numbers of F1 to F5 stable for the parallel specs.
- C3 Renumber (F0 harness, F1 skeleton, F2 Sell…). Rejected: it churns every parallel spec and shifts the "used by F0 or F1" guard away from Sell.

**D. Retrieve vs Sell**: D1 draft order · **D2 swap: F1 Retrieve, F2 Sell (recommended, moderate confidence)** · D3 merge.

**E. Archive / completion record**
- E1 A separate `/plan-archive` skill (agentflow).
- E2 **A procedure run by the git agent in its post-merge cleanup step, writing on a tiny `chore/archive-<plan-id>` branch that David merges**, or folded into the next feature's branch as its first commit. Recommended: there is no extra skill to build, and no agent touches `main`.
- E3 David archives by hand. Rejected: it isn't mechanical.

## Recommendation

1. **One file, one truth.** `docs/plans/active/<plan-id>.plan.md` holds the frontmatter, the reasoning prose and **exactly one** fenced ```` ```yaml dag ```` block under `## DAG`. There is no manifest. `/orient` lists `docs/plans/active/*.plan.md` and reads each plan's frontmatter and node statuses. *(Superseded by the Decision amendment: the DAG and all state live in `<plan-id>.dag.yaml`; the `.plan.md` is prose only.)*
2. **Ids:** `build`, `feat-NNN`, `bug-NNN`, `inv-NNN`, with NNN = max over `active/` + `archive/` + 1, zero-padded. No slugs (the title lives in frontmatter). Node ids are `F0a, F0b, F1…F5` in `build`, and `N1, N2…` in feat/bug plans (`bug-NNN`'s `N1` is always the reproduction test). *(Superseded in the Contract: nodes run to F7, and the reproduction test is N1's first task.)* Task ids are node-local (`t1…`). Trace key: `docs/trace/<plan-id>/<node-id>/`.
3. **Status writes are branch commits** (A2), labelled `plan(<plan-id>/<node-id>): <from> → <to>`. Only the orchestrator writes node/task status. Only David writes `approved`. Plan-level `status` is derived (see Contract), never hand-set except `draft → approved`.
4. **Node DoD is mechanical first, human last.** Every item is a check whose output is evidence (§Contract "Node definition of done"). `done` is set as the last commit before the PR. **It is only true on `main` once David has merged.** `/orient` flags any node marked `done` on a branch that isn't merged.
5. **Build order (D2 + C2):** F0a harness → F0b skeleton → F1 Retrieve → F2 Sell → F3 Cancel (+COULD-1 quote, +COULD-2 claims) → F4 Renew (+COULD-3 cheque, **+CANCEL-5**). F5 IaC/deploy evidence `depends_on: [F0b]`, `cuttable: true`, budget 3h, scheduled last. *(D2 rejected in the Decision: F1 is Sell, F2 Retrieve.)*
6. **Retry ladder counts per node** (one attempt = one reviewer/security FAIL cycle on that node, whatever the cause). Counting "per same error" (agentflow) needs judgement. Counting per node is mechanical.
7. **Archive (E2)** as a git-agent procedure. A completion record is built from git and appended to `docs/lessons.md` if there's a lesson. A lesson is **required** when any node hit attempt ≥ 3, was escalated, or was cut.
8. **Investigation recommendations gain a third kind, `amend-node`** (a revised approach for the parent node's attempt 4), next to `feature` and `bug`. Without it, the attempt-3 investigation has nowhere to put its main output. **This changes requirements.md** (it currently allows only feature/bug).

## Decision — DECIDED 2026-10-17 (David)

- [x] **A, B, ids, attempts, investigation kinds, archive, lessons, `/plan-dag` question set:** recommendations accepted as a block.
- [x] **C → C2:** F0a harness (`owner: david`, built in-session, no loop), then F0b walking skeleton (the first loop run).
- [x] **D → keep Sell first, rejecting D2.** F1 is Sell plus a *bare* GET, so every Sell test is a POST → GET round trip through the API. F2 Retrieve adds GET-1 (404), the derived `status`, the current-term view and the full response shape.
- [x] **Rule → node mapping:**
  - **F1 Sell:** SELL-1..6.
  - **F2 Retrieve:** GET-1.
  - **F3 Cancel:** CANCEL-1..4, 6, 7, plus COULD-1 quote and COULD-2a–c claims.
  - **F4 Renew:** RENEW-1..6, COULD-3, and CANCEL-5 (moved from F3).
- [x] **Nodes after F4:**
  - **F5 IaC + pipeline:** cuttable, 3 h budget, `depends_on: [F0b]`.
  - **F6 README:** must. It's built from a checklist of every "the README states…" in the specs.
  - **F7 Pitch deck:** cuttable. Generated from the DAG plus the trace trails.
- [x] **React frontend: removed from this build plan (David, 2026-10-17).** It gets its own brief, mockups and plan DAG after the build plan finishes. It will cover more than an API demo, for example displaying the traceability trails. Reserve `src/web/` in the layout, but build nothing for it in this plan.
- [x] **Cut order:** F7 → F5. F0–F4 and F6 are never cut.
- [x] **Your involvement per feature: two pauses.**
  - **Pause 1, mid-loop:** the acceptance tests are red and the stub controller is green. You call the stub in Postman and read the tests, then the loop continues.
  - **Pause 2, HITL sign-off:** reviewed using a generated `walkthrough.md`, which lists each commit in order (red, stub, domain, services, persistence, refactor) with one line each, plus the QA script and Postman.
  - These pauses change orchestration's loop and requirements.md §3.
- [x] **Final rulings (David, 2026-10-17):**
  - New rule SELL-7 (policy amount > 0, else 422) joins F1.
  - OQ7: only reviewer, security or gate FAILs count as attempts. David's steers at Pause 1, Pause 2 and in PR comments don't increment `attempt`; they still go investigate → red → fix as findings `D1`, `D2`…
  - OQ8: a standalone investigation is committed on a `chore/inv-NNN` branch via PR.
  - Accepted as proposed: `tests/rule-coverage.pending.txt` is empty once F4 is done.
- [x] **Amendment (David, 2026-10-17, after the first `/plan-dag` draft): the DAG moves to its own file, and every node gets explicit tasks.**
  - `docs/plans/active/<plan-id>.dag.yaml` is the only state: plan status, approval, nodes and tasks. The orchestrator loops over it. `<plan-id>.plan.md` keeps the reasoning prose only and holds no DAG. That's still one source of truth, so A4's objection (two places holding status) doesn't apply.
  - Tasks are explicit per node, never a named list. Feature nodes follow the per-layer sequence (testing C6 plus the orchestration state machine), and each task names the rule ids it covers. This closes agents-skills open question 4 in favour of batching per layer, not per rule.
  - F0a gets its explicit session task list, harvested from every spec's F0a and F0 items. F0b gains the "verify in F0" checks.

## Contract

Binding. Where this section and `docs/requirements.md` §3 disagree, this section wins until requirements.md is updated (an F0a acceptance item).

**The React frontend is out of this plan.** It gets its own brief, mockups and plan DAG later. `src/web/` is reserved in the layout only.

### Files, ids, derived paths

- One plan = two files in `docs/plans/active/`:
  - `<plan-id>.dag.yaml` is **the only state**: plan status and approval, nodes, tasks. The orchestrator reads and writes only this file.
  - `<plan-id>.plan.md` holds the reasoning prose (frontmatter, Why, Approach, Rejected alternatives, Prior work, Attempt log, Completion record). It holds **no** DAG and no status.
  - There's no other manifest and no `feature_list.json`. An `inv-NNN` plan has only the `.plan.md`.
- Plan ids: `build`, `feat-NNN`, `bug-NNN`, `inv-NNN`. NNN = max over `active/` + `archive/` + 1, zero-padded to 3. No slugs; the title lives in frontmatter.
- Node ids: `F0a`, `F0b`, `F1`…`F7` in `build`; `N1`, `N2`… in feat/bug plans. Task ids are node-local (`t1`…). Ids are never renamed.
- Per node: branch `feature/<plan>/<node>` (`bug/<plan>/<node>` in a bug plan), worktree `.claude/worktrees/<plan>-<node>`, trace `docs/trace/<plan>/<node>/`, QA script `docs/qa/<plan>-<node>.md`.

### Lifecycle and node states (David's two pauses are states)

```text
plan:  draft ─(David commits approved_by/on)─► approved ─(first node in_progress)─► active
       active ─(every node done|cut)─► done ─(archive PR merged)─► archived
node:  pending ─► in_progress ─► paused_acceptance ─► in_progress ─► hitl ─► done
                    │  ▲                                              │      (done = PR merged
                    │  └── FAIL: attempt++ · David steer D<n>: no++ ◄┘       into main)
                    ├──► investigating (attempt 3, or repeated fingerprint) ─► in_progress
                    └──► escalated (attempt 5; David decides)            any ─► cut (David only)
owner: david nodes:  pending ─► in_progress ─► done   (no loop, no pauses)
```

| State | Meaning | Leaves when |
|---|---|---|
| `paused_acceptance` | **Pause 1.** The node's full acceptance suite is red and the stub controller is green. The run ends. David calls the stub in Postman and reads the tests | David says continue → `in_progress` (green steps). A change he asks for → failure route as steer `D<n>`, no `attempt++` |
| `hitl` | **Pause 2.** Draft PR open. David signs off using the generated `walkthrough.md` (each commit in order, one line each), the QA script and Postman, on port 5080 from the worktree | David signs the QA script, marks the PR ready and merges → `done`. A David finding → failure route as steer `D<n>`, no `attempt++` |
| `investigating` | `/plan-investigation` is open for this node. The run ends | David accepts/rejects the recommendations → `in_progress` |
| `escalated` | Attempt 5 reached. The run ends | David amends the plan, cuts the node, or takes it over |

Plan `status` is derived; the file caches it, refreshed on each status commit. `inv-NNN` plans have no DAG: `draft → active → done (findings + recommendations written) → archived`.

### Who may change what

| Field | Writer | Mechanism |
|---|---|---|
| `approved_by`, `approved_on`, `status: approved` (in `.dag.yaml`) | David only | `build`: David's commit on `main` (allowed until F0b starts). Later plans: David's commit on the plan's first node branch, before the loop starts. The orchestrator refuses an unapproved plan |
| node `status`, `attempt`, `branch`, `worktree`, `pr`; task `status` (in `.dag.yaml`) | orchestrator (main session); for `owner: david` nodes, the main session as David works | A commit on the node's branch (David's nodes: on `main` until F0b starts), labelled `plan(<plan>/<node>): <from> → <to>`. It reaches `main` at merge |
| `cut`, escalation outcome | David | Recorded by the orchestrator with `decided_by: david` |
| prose, `rules`, `acceptance`, `depends_on` | the `plan-*` skill at draft time; afterwards David or an accepted `amend-node` | — |
| move to `archive/`, completion record | git agent | `chore/archive-<plan-id>` branch (see Archive) |

### Orchestrator read algorithm

1. Parse `docs/plans/active/<plan-id>.dag.yaml` and validate it with `node .claude/skills/plan-dag/validate-dag.mjs <file>`, which `/feature` also imports. The checks:
   - ids are unique; every `depends_on` resolves; there's no cycle
   - every `rules:` id exists in `docs/requirements.md` (**a missing id is a hard stop**; `/plan-feature` adds it there first), and no rule is mapped to two nodes
   - every node has at least one `acceptance` item and a non-empty explicit `tasks` list
   - no non-cuttable node depends on a cuttable one
   - each loop node's tasks contain its sequence's steps in order (below); t1 of a feature node is the red acceptance suite naming every node rule, and every node rule has a green task
2. Refuse if plan `status` is `draft`.
3. Resume any node in `in_progress | paused_acceptance | investigating | hitl | escalated` (one feature at a time). Otherwise **next node** = the first `pending` node in file order whose `depends_on` are all `done` and whose `owner` ≠ `david`. A `david` node blocks its dependents until David marks it done.
4. Inside the node, **next task** = the first task whose `status` isn't `done` or `skipped`. Tasks run strictly in order. A `role: david` task (`pause1`, `pause2`, `merge`) ends the run until David answers. The failure route (investigate → red → fix) is not a planned task: it runs inside the current task and is logged in the attempt log and the trace.
5. No next node and every node `done|cut` → plan `done` → archive.

`main` shows an in-flight node as `pending` by design. `/orient` reads in-flight state from the worktree copies listed by `git worktree list`.

### Node definition of done (in order; each leaves evidence in the node's `trace`)

1. Every task `done` (or `skipped` with a reason in the trace). Every rule in `rules:` has tests tagged `[Trait("Rule","<id>")]` per the testing spec, its ids are gone from `tests/rule-coverage.pending.txt` (empty once F4 is done), and the rule-coverage check passes.
2. Reviewer PASS and security PASS for the current diff fingerprint.
3. The git agent has rebased onto `origin/main`; the full suite is green in the worktree; `dotnet build` has zero warnings; the idempotent migration script generates.
4. Postman v2.1 requests for the node's endpoints, with one example per violation response.
5. `chore(trace)` committed; draft PR raised (body from the trace summary plus David's paragraph); verdicts and the rebase receipt posted as PR comments.
6. QA script at `qa:` signed by David from the worktree; `status: done` committed; **David merges**. Only then is the node done on `main`.

`owner: david` nodes skip 2, 4 and 5; their acceptance is David's in-session review. `evidence`/`docs` nodes skip the rule-coverage part of 1 and item 4.

### Plan file template (`docs/plans/active/<plan-id>.plan.md`, prose only)

````markdown
---
plan_id: feat-001          # build | feat-NNN | bug-NNN | inv-NNN
kind: feature              # build | feature | bug | investigation
title: <short title>
created: 2026-10-17
dag: feat-001.dag.yaml     # null for inv-NNN
parent: null               # bug: {plan, node, rule}; from an investigation: inv-NNN
---
# <plan-id>: <title>
## Why
## Approach
## Rejected alternatives      <!-- ≥1, required: the skill refuses to write the plan without one -->
## Prior work consulted       <!-- hits from docs/plans/ + docs/lessons.md, or "none" -->
## Attempt log                <!-- orchestrator appends: attempt n · date · verdict/finding ref · what changes next -->
## Completion record          <!-- appended at archive, from git -->
````

No status, approval or DAG lives here. The validator rejects a `yaml dag` block, or a `status`/`approved_*` key, in this file.

### DAG file template (`docs/plans/active/<plan-id>.dag.yaml`, the only state)

```yaml
plan_id: feat-001            # matches the file name
kind: feature                # build | feature | bug
title: <short title>
status: draft                # draft|approved|active|done|archived; draft → approved is David's alone
created: 2026-10-17
approved_by: null            # David only
approved_on: null
requirements_rev: <sha>      # git short hash of docs/requirements.md when drafted; refreshed at approval
parent: null
nodes:
  - id: N1                   # stable, never renamed
    title: <short title>
    kind: feature            # harness | feature | bug | evidence | docs
    owner: loop              # loop | david
    depends_on: []           # node ids in this plan
    rules: [SELL-2]          # ids present in docs/requirements.md; [] for non-rule nodes; each rule in one node only
    cuttable: false
    budget: null             # time box (2h, 90m); null = unset
    status: pending          # pending|in_progress|paused_acceptance|investigating|hitl|escalated|done|cut (+ decided_by: david for cut/escalated)
    attempt: 0               # reviewer/security/gate FAIL cycles; David's steers (D<n>) don't count; never reset
    branch: null             # feature/<plan>/<node> | bug/<plan>/<node>; set by the git agent
    worktree: null           # .claude/worktrees/<plan>-<node>
    pr: null                 # PR number
    qa: docs/qa/<plan>-<node>.md
    trace: docs/trace/<plan>/<node>/
    acceptance:              # ≥1 checkable statement, double-quoted
      - "<statement>"
    tasks:                   # explicit, in run order, one flow map per line
      - {id: t1, role: tester, step: red, layer: acceptance, rules: [SELL-2], title: "<what this task produces>", status: pending}
```

Task fields:
- `id`: `t1`, `t2`… sequential, in run order.
- `role`: `tester | builder | review` (reviewer ∥ security, dispatched together) `| git | orchestrator | david`.
- `step`: `session | red | stub | pause1 | green | refactor | gates | review | qa | integrate | trace | pr | pause2 | merge | write`. Each step belongs to one role: red and qa → tester; stub, green, refactor and write → builder; integrate and pr → git; gates and trace → orchestrator; pause1, pause2 and merge → david; session → david on `owner: david` nodes only.
- `layer`: `acceptance | domain | services | persistence | api | null`.
- `rules`: the subset of the node's rules this task proves or implements; `[]` for plumbing.
- `status`: `pending | in_progress | done | skipped`. Only the orchestrator writes it (the main session on David's nodes), as a `plan(<plan>/<node>): <task> <from> → <to>` commit.

### Task sequences

Every node lists its tasks explicitly; these are the shapes the `plan-*` skills compose from. Each task is one or more commits, and `walkthrough.md` follows this order.

| Sequence | For | Tasks, in order |
|---|---|---|
| **feature** | `owner: loop`, kind `feature`/`bug` | tester **red** acceptance (every node rule; Postman folder; throwing stubs; ids out of `rule-coverage.pending.txt`) · builder **stub** api → david **pause1** · per layer that has work, in order domain → services: tester **red** unit tests, then builder **green** · builder **green** persistence · builder **green** api (the whole acceptance suite goes green last) · builder **refactor** · orchestrator **gates** · **review** (reviewer ∥ security) · tester **qa** · git **integrate** · orchestrator **trace** · git **pr** (push, draft PR, verdict and rebase comments) · david **pause2** (Postman receipt, then sign-off) · david **merge** |
| **docs** | `owner: loop`, kind `docs`/`evidence` | builder **write** · orchestrator **gates** · **review** · tester **qa** · git **integrate** · orchestrator **trace** · git **pr** · david **pause2** · david **merge** (no Pause 1) |
| **session** | `owner: david` | `role: david, step: session` tasks only, built in the main session with no subagent loop |

A layer with nothing to do is left out, not skipped. Services gets a tester red task only when it has logic beyond orchestration (testing C1); `UkCalendar` qualifies.

### The build DAG (`docs/plans/active/build.dag.yaml`)

`/plan-dag` copies the nodes below verbatim, adds the plan header and applies David's answers. File order is the schedule: F5 sits after F4 so it isn't picked straight after F0b.

```yaml
nodes:
  - id: F0a
    title: Harness
    kind: harness
    owner: david
    depends_on: []
    rules: []
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F0a.md
    trace: docs/trace/build/F0a/
    acceptance:
      - "CLAUDE.md, the six agents, our own skills (incl. the contract-only architecture skill and /feature), Node .mjs hooks with a manifest table and a passing wiring self-test, sh git hooks (pre-commit build; pre-push build + tests + idempotent migration script)"
      - "Spike: a subagent dispatched after EnterWorktree inherits the worktree (cwd and writes land there); receipt in the trace"
      - "Spike: agent_type arrives in PreToolUse and SubagentStop hook input for each of the six agents"
      - "Spike: which settings.json (main checkout or worktree copy) governs the session and subagents inside a worktree"
      - "Spike: ask rules on git push / gh pr create / gh pr comment still prompt in bypass mode"
      - "Spike: a dispatched subagent has CLAUDE.md loaded"
      - "A failed spike is fixed in F0a or recorded as a design change; never passed silently"
      - "Harness lock live: a provoked subagent write to .claude/agents/** is denied, including inside a worktree"
      - "Postman CLI and Bicep CLI installed; postman --version and bicep --version recorded; the Postman CLI runs a local collection without login (or Postman is recorded as David-only)"
      - "requirements.md updated: F0 split, rule→node mapping (COULD-2 in F3), CANCEL-5 in F4, CANCEL-7 and COULD-2a–c added, amend-node, attempts per node, two pauses, F6/F7, React out, no /plan-archive, plan = .plan.md prose + .dag.yaml state"
      - "docs/readme-checklist.md lists every 'the README states…' from the spec Contracts (extracted by the main session; agents never read specs)"
      - "Context smoke tests S1–S9 pass and are recorded in the F0a trail"
      - "/plan-dag run; build.plan.md and build.dag.yaml approved by David's commit on main"
    tasks:
      - {id: t1, role: david, step: session, layer: null, rules: [], title: "Step 0: /orient, /save-context, /load-context-chain and the SessionStart hook", status: done}
      - {id: t2, role: david, step: session, layer: null, rules: [], title: "/plan-dag skill and DAG validator; build plan drafted, reviewed and approved by David's commit on main", status: pending}
      - {id: t3, role: david, step: session, layer: null, rules: [], title: "Spike: a subagent dispatched after EnterWorktree inherits cwd and writes land there; hook cwd is the worktree on Windows; which settings.json governs inside a worktree", status: pending}
      - {id: t4, role: david, step: session, layer: null, rules: [], title: "Spike: agent_type in PreToolUse and SubagentStop for a probe project agent (and workflow agent()); SubagentStop exit 2 re-prompts; CLAUDE.md is loaded in the subagent; Grep honours .ignore", status: pending}
      - {id: t5, role: david, step: session, layer: null, rules: [], title: "Spike: ask rules on git push, gh pr create and gh pr comment prompt in bypass mode", status: pending}
      - {id: t6, role: david, step: session, layer: null, rules: [], title: "Tools: Postman CLI and Bicep CLI installed with versions recorded; Postman CLI runs a local collection without login", status: pending}
      - {id: t7, role: david, step: session, layer: null, rules: [], title: "requirements.md brought in line with the spec Contracts (the F0a acceptance list)", status: pending}
      - {id: t8, role: david, step: session, layer: null, rules: [], title: "CLAUDE.md per the agents-skills contract: at most 80 lines, contract only, no spec imports", status: pending}
      - {id: t9, role: david, step: session, layer: null, rules: [], title: "The six agents in .claude/agents/ per the roster and frontmatter template, bodies at most 120 lines", status: pending}
      - {id: t10, role: david, step: session, layer: null, rules: [], title: "Preloaded skills: architecture, dotnet10-webapi, tdd-outside-in, requirements-trace, code-quality, verdict-format, owasp-api-security, root-cause, git-workflow", status: pending}
      - {id: t11, role: david, step: session, layer: null, rules: [], title: "/feature orchestrator skill: the node state machine over the DAG file's tasks, gates with receipts, retry ladder, both pauses", status: pending}
      - {id: t12, role: david, step: session, layer: null, rules: [], title: "Traceability: activity and render hooks, record schemas, render script, completeness check (reports OK git-only for F0a)", status: pending}
      - {id: t13, role: david, step: session, layer: null, rules: [], title: "Guard hooks: guard-agent.mjs and guard-bash.mjs (G1-G3, G8, G9, G14), SubagentStart and SubagentStop with the verdict schema check", status: pending}
      - {id: t14, role: david, step: session, layer: null, rules: [], title: "settings.json deny and ask rules (G5-G7) and the repo-root .ignore excluding .claude/specs/", status: pending}
      - {id: t15, role: david, step: session, layer: null, rules: [], title: "Git hooks in .githooks (pre-commit, commit-msg, pre-push; sh, LF); core.hooksPath installed; .gitattributes; .gitignore for the local denylist", status: pending}
      - {id: t16, role: david, step: session, layer: null, rules: [], title: "Wiring self-test guards.test.mjs (G13) passes, including line caps", status: pending}
      - {id: t17, role: david, step: session, layer: null, rules: [], title: "docs/readme-checklist.md extracted from every 'the README states…' in the spec Contracts", status: pending}
      - {id: t18, role: david, step: session, layer: null, rules: [], title: "Context smoke tests S1-S9 run and recorded", status: pending}
      - {id: t19, role: david, step: session, layer: null, rules: [], title: "F0a README: every probe result, and 'hooks not yet live: witnessed by git and trailers only'", status: pending}
      - {id: t20, role: david, step: session, layer: null, rules: [], title: "Harness lock live (G4) and .githooks/main-locked as the last F0a commit; a provoked subagent write to .claude/agents/** is denied, including in a worktree", status: pending}
  - id: F0b
    title: Walking skeleton
    kind: feature
    owner: loop
    depends_on: [F0a]
    rules: []
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F0b.md
    trace: docs/trace/build/F0b/
    acceptance:
      - "Four src projects with the architecture references; architecture tests pass and a deliberate layering breach fails them (shown in the red commit)"
      - "/health/live and /health/ready (DB) answer; one WebApplicationFactory integration test on SQLite in-memory with FakeTimeProvider"
      - "Initial SQL Server migration; pre-push generates the idempotent script; SQLite uses EnsureCreated()"
      - "BannedApiAnalyzers bans DateTime.Now/UtcNow/Today; UkCalendar.Today(TimeProvider) tested across the BST/GMT change"
      - "RFC 9457 error mapping skeleton: a 500 returns a generic detail and traceId, no stack trace outside Development"
      - "Azure.Monitor.OpenTelemetry.AspNetCore wired in the composition root, on only when APPLICATIONINSIGHTS_CONNECTION_STRING is set; console logging otherwise; a test proves startup without it; no PII in log templates"
      - "OpenAPI 3.1 + Scalar in Development only"
      - "Postman v2.1 collection + local environment call health via the Postman CLI"
      - "Every 'verify in F0' item is checked and recorded in the trace: xunit.v3 on Microsoft.Testing.Platform without Microsoft.NET.Test.Sdk; Mvc.Testing, EF Core Sqlite, NetArchTest.eNhancedEdition and TrxReport versions; BannedApi doc-IDs; whether Program needs public partial class"
      - "RuleCoverageTests runs with every rule id listed in tests/rule-coverage.pending.txt"
      - "The commit-on-main deny hook is active from F0b start"
      - "The loop ran end to end: both pauses, walkthrough.md, signed QA script, PR merged, trace complete"
    tasks:
      - {id: t1, role: tester, step: red, layer: acceptance, rules: [], title: "Four test projects; architecture tests with a deliberate breach shown failing; health integration test on SQLite + FakeTimeProvider; UkCalendar BST/GMT tests; 500 problem-body and startup-without-App-Insights tests; RuleCoverageTests with every id pending; Postman health requests; throwing stubs", status: pending}
      - {id: t2, role: builder, step: stub, layer: api, rules: [], title: "Four src projects, slnx, Directory.Build.props and Directory.Packages.props with verified versions; health routes answering with canned responses", status: pending}
      - {id: t3, role: david, step: pause1, layer: null, rules: [], title: "Pause 1: call the health stub in Postman, read the tests; go or change", status: pending}
      - {id: t4, role: builder, step: green, layer: services, rules: [], title: "UkCalendar.Today(TimeProvider) in Europe/London; BannedApiAnalyzers with BannedSymbols.txt", status: pending}
      - {id: t5, role: builder, step: green, layer: persistence, rules: [], title: "PoliciesDbContext with the SQL Server / SQLite switch, initial migration, EnsureCreated for SQLite; idempotent script generates", status: pending}
      - {id: t6, role: builder, step: green, layer: api, rules: [], title: "Real health checks (live, ready with DB), RFC 9457 error mapping, OpenTelemetry only with the connection string, OpenAPI 3.1 + Scalar in Development", status: pending}
      - {id: t7, role: builder, step: refactor, layer: null, rules: [], title: "Refactor with the full suite green", status: pending}
      - {id: t8, role: orchestrator, step: gates, layer: null, rules: [], title: "build -warnaserror, full test, rule coverage, diff scope, no NotImplementedException in src", status: pending}
      - {id: t9, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t10, role: tester, step: qa, layer: null, rules: [], title: "QA script docs/qa/build-F0b.md", status: pending}
      - {id: t11, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t12, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md, README and matrix; chore(trace) commit; completeness check passes", status: pending}
      - {id: t13, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t14, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: Postman receipt on port 5080, then sign-off from walkthrough.md and the QA script", status: pending}
      - {id: t15, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F1
    title: Sell (+ bare GET)
    kind: feature
    owner: loop
    depends_on: [F0b]
    rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7]
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F1.md
    trace: docs/trace/build/F1/
    acceptance:
      - "POST /policies → 201 + Location; every Sell test is a POST → GET round trip through a bare GET /policies/{ref}"
      - "Every numbered-rule failure → 422 with violations[] (all reported), including SELL-7 amount ≤ 0; SELL-1 duplicate → 409, with abc-1 and ABC-1 the same policy"
      - "400 for shape: malformed JSON, missing field, unknown field, unknown or integer enum, postcode > 8, bad reference pattern"
      - "Boundaries on FakeTimeProvider in Europe/London: day 0, 60, 61, yesterday; 16th birthday on the start date; a named 29 Feb test (End = 28 Feb next year)"
      - "SELL-6: a Payment for the full amount with a generated reference"
    tasks:
      - {id: t1, role: tester, step: red, layer: acceptance, rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7], title: "Sell acceptance suite in Api.Tests as POST → GET round trips, every success and failure row; Postman F1 folder; throwing stubs; F1 ids out of rule-coverage.pending.txt", status: pending}
      - {id: t2, role: builder, step: stub, layer: api, rules: [], title: "Stub POST /policies (201 + Location) and bare GET /policies/{ref} with canned bodies", status: pending}
      - {id: t3, role: david, step: pause1, layer: null, rules: [], title: "Pause 1: call the Sell stub in Postman, read the acceptance tests; go or change", status: pending}
      - {id: t4, role: tester, step: red, layer: domain, rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7], title: "Domain.Tests for Policy.Sell at every boundary: reference normalisation, start today to today+60, end = start + 1y − 1d incl. 29 Feb, 1-3 holders, 16 on the start date, full-amount payment, amount > 0", status: pending}
      - {id: t5, role: builder, step: green, layer: domain, rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7], title: "Policy.Sell enforces SELL-1..7 with DateOnly today passed in, collecting every violation", status: pending}
      - {id: t6, role: tester, step: red, layer: services, rules: [SELL-2, SELL-6], title: "Services.Tests: PolicyService.Sell reads UK today once (BST/GMT) and generates the payment reference", status: pending}
      - {id: t7, role: builder, step: green, layer: services, rules: [SELL-2, SELL-6], title: "PolicyService.Sell: UkCalendar.Today, ReferenceGenerator, repository Add and SaveChanges", status: pending}
      - {id: t8, role: builder, step: green, layer: persistence, rules: [SELL-1], title: "Policy, Policyholder, Property and Payment configurations; unique index on the normalised reference; duplicate → PersistenceConflictException; migration", status: pending}
      - {id: t9, role: builder, step: green, layer: api, rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7], title: "Real controller → PolicyService: 400 shape, 422 violations[], 409 duplicate, 201 Location, bare GET; whole acceptance suite green", status: pending}
      - {id: t10, role: builder, step: refactor, layer: null, rules: [], title: "Refactor with the full suite green", status: pending}
      - {id: t11, role: orchestrator, step: gates, layer: null, rules: [], title: "build -warnaserror, full test, rule coverage, diff scope, no NotImplementedException in src", status: pending}
      - {id: t12, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t13, role: tester, step: qa, layer: null, rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6, SELL-7], title: "QA script docs/qa/build-F1.md, steps tagged per rule", status: pending}
      - {id: t14, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t15, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md, README and matrix; chore(trace) commit; completeness check passes", status: pending}
      - {id: t16, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t17, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: Postman receipt on port 5080, then sign-off from walkthrough.md, the QA script and Postman", status: pending}
      - {id: t18, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F2
    title: Retrieve
    kind: feature
    owner: loop
    depends_on: [F1]
    rules: [GET-1]
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F2.md
    trace: docs/trace/build/F2/
    acceptance:
      - "Unknown reference → 404 problem body"
      - "status derived from today: Pending, Active, Expired (Cancelled is covered in F3)"
      - "startDate/endDate/amount describe the current term; terms[] holds the history; full response shape per api-contract"
    tasks:
      - {id: t1, role: tester, step: red, layer: acceptance, rules: [GET-1], title: "Retrieve acceptance suite: 404 problem body, derived status, current-term view, full response shape; Postman F2 folder; GET-1 out of rule-coverage.pending.txt", status: pending}
      - {id: t2, role: builder, step: stub, layer: api, rules: [], title: "Stub GET /policies/{ref} with the full canned response shape", status: pending}
      - {id: t3, role: david, step: pause1, layer: null, rules: [], title: "Pause 1: call the Retrieve stub in Postman, read the acceptance tests; go or change", status: pending}
      - {id: t4, role: tester, step: red, layer: domain, rules: [GET-1], title: "Domain.Tests: status Pending, Active, Expired from today; current term = the term in force today, else the latest", status: pending}
      - {id: t5, role: builder, step: green, layer: domain, rules: [GET-1], title: "Policy derives status and the current term from DateOnly today", status: pending}
      - {id: t6, role: builder, step: green, layer: services, rules: [GET-1], title: "PolicyService.Get: normalised reference, UK today, null for not found", status: pending}
      - {id: t7, role: builder, step: green, layer: persistence, rules: [GET-1], title: "GetByReference loads terms, policyholders, property, payments, refunds and claims", status: pending}
      - {id: t8, role: builder, step: green, layer: api, rules: [GET-1], title: "Real GET: response contract per api-contract, 404 problem body; whole acceptance suite green", status: pending}
      - {id: t9, role: builder, step: refactor, layer: null, rules: [], title: "Refactor with the full suite green", status: pending}
      - {id: t10, role: orchestrator, step: gates, layer: null, rules: [], title: "build -warnaserror, full test, rule coverage, diff scope, no NotImplementedException in src", status: pending}
      - {id: t11, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t12, role: tester, step: qa, layer: null, rules: [GET-1], title: "QA script docs/qa/build-F2.md, steps tagged per rule", status: pending}
      - {id: t13, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t14, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md, README and matrix; chore(trace) commit; completeness check passes", status: pending}
      - {id: t15, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t16, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: Postman receipt on port 5080, then sign-off from walkthrough.md, the QA script and Postman", status: pending}
      - {id: t17, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F3
    title: Cancel (+ quote, + claims)
    kind: feature
    owner: loop
    depends_on: [F2]
    rules: [CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c]
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F3.md
    trace: docs/trace/build/F3/
    acceptance:
      - "POST /policies/{ref}/cancellation (empty body = today) and GET …/cancellation-quote return ruleApplied and the day counts, and agree for the same date"
      - "CANCEL-4 and COULD-2c → 409; CANCEL-7 blocks a date after the latest term's end"
      - "POST /policies/{ref}/claims; any claim → £0 refund; Cancelled overrides the derived status"
      - "A concurrent write → 409 via the Version token"
      - "CANCEL-2 refunds to the original payment method, CANCEL-6 rounds 2dp away from zero in one place, and COULD-2a/b reject a future claim date or one outside every term with 422"
    tasks:
      - {id: t1, role: tester, step: red, layer: acceptance, rules: [CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c], title: "Cancel, quote and claims acceptance suite, every success and failure row; Postman F3 folder; throwing stubs; F3 ids out of rule-coverage.pending.txt", status: pending}
      - {id: t2, role: builder, step: stub, layer: api, rules: [], title: "Stub POST cancellation, GET cancellation-quote and POST claims with canned bodies", status: pending}
      - {id: t3, role: david, step: pause1, layer: null, rules: [], title: "Pause 1: call the Cancel stubs in Postman, read the acceptance tests; go or change", status: pending}
      - {id: t4, role: tester, step: red, layer: domain, rules: [CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c], title: "Domain.Tests for policy.Cancel, RefundCalculator, Money.Round and policy.RecordClaim at every boundary: before start, cooling-off day 14/15, pro-rata on 365/366, per-payment method, past date, cancel twice, after cover, claim rules", status: pending}
      - {id: t5, role: builder, step: green, layer: domain, rules: [CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c], title: "policy.Cancel, pure RefundCalculator (quote = same calculation), Money.Round, policy.RecordClaim", status: pending}
      - {id: t6, role: tester, step: red, layer: services, rules: [CANCEL-3, COULD-1, COULD-2a], title: "Services.Tests: cancellation date defaults to UK today; quote computes without saving; claim date checked against UK today", status: pending}
      - {id: t7, role: builder, step: green, layer: services, rules: [CANCEL-3, COULD-1, COULD-2a], title: "PolicyService.Cancel, Quote and RecordClaim with UK today; refund references generated", status: pending}
      - {id: t8, role: builder, step: green, layer: persistence, rules: [CANCEL-2, COULD-2], title: "Refund and Claim configurations; Guid Version concurrency token → PersistenceConflictException; migration", status: pending}
      - {id: t9, role: builder, step: green, layer: api, rules: [CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c], title: "Real cancellation, quote and claims endpoints: 422 violations, 409 for CANCEL-4, COULD-2c and concurrency; whole acceptance suite green", status: pending}
      - {id: t10, role: builder, step: refactor, layer: null, rules: [], title: "Refactor with the full suite green", status: pending}
      - {id: t11, role: orchestrator, step: gates, layer: null, rules: [], title: "build -warnaserror, full test, rule coverage, diff scope, no NotImplementedException in src", status: pending}
      - {id: t12, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t13, role: tester, step: qa, layer: null, rules: [CANCEL-1a, CANCEL-1b, CANCEL-1c, CANCEL-2, CANCEL-3, CANCEL-4, CANCEL-6, CANCEL-7, COULD-1, COULD-2, COULD-2a, COULD-2b, COULD-2c], title: "QA script docs/qa/build-F3.md, steps tagged per rule", status: pending}
      - {id: t14, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t15, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md, README and matrix; chore(trace) commit; completeness check passes", status: pending}
      - {id: t16, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t17, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: Postman receipt on port 5080, then sign-off from walkthrough.md, the QA script and Postman", status: pending}
      - {id: t18, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F4
    title: Renew
    kind: feature
    owner: loop
    depends_on: [F3]
    rules: [RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5]
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F4.md
    trace: docs/trace/build/F4/
    acceptance:
      - "POST /policies/{ref}/renewals appends a term the day after the current end; window end − 30 days to end, inclusive"
      - "RENEW-3b: auto-renew off with a paymentMethod → 422; COULD-3: a defaulted Cheque → 422 asking for another method"
      - "CANCEL-5: cancelling a renewed policy ends it; the current term follows the normal rules and a future term is refunded in full"
      - "RENEW-3a creates a payment using the request method or the current term's method, RENEW-5 rejects an amount <= 0 with 422, and RENEW-6 returns 409 for a cancelled policy"
    tasks:
      - {id: t1, role: tester, step: red, layer: acceptance, rules: [RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5], title: "Renew acceptance suite, every success and failure row, plus cancelling a renewed policy; Postman F4 folder; throwing stubs; F4 ids out of rule-coverage.pending.txt (empty after this)", status: pending}
      - {id: t2, role: builder, step: stub, layer: api, rules: [], title: "Stub POST /policies/{ref}/renewals with a canned body", status: pending}
      - {id: t3, role: david, step: pause1, layer: null, rules: [], title: "Pause 1: call the Renew stub in Postman, read the acceptance tests; go or change", status: pending}
      - {id: t4, role: tester, step: red, layer: domain, rules: [RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5], title: "Domain.Tests for policy.Renew and cancel-after-renew: window edges end − 31/−30/0/+1, payment or unpaid term, amount > 0, cancelled → conflict, cheque on auto-renew, future term refunded in full", status: pending}
      - {id: t5, role: builder, step: green, layer: domain, rules: [RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5], title: "policy.Renew appends the next term; Cancel handles a future term", status: pending}
      - {id: t6, role: tester, step: red, layer: services, rules: [RENEW-1/2, RENEW-3a], title: "Services.Tests: renewal window measured from UK today; payment method defaults to the current term's", status: pending}
      - {id: t7, role: builder, step: green, layer: services, rules: [RENEW-1/2, RENEW-3a], title: "PolicyService.Renew with UK today and the method default", status: pending}
      - {id: t8, role: builder, step: green, layer: persistence, rules: [RENEW-4], title: "Appended terms persisted under the same reference with the Version check; migration if the schema changes", status: pending}
      - {id: t9, role: builder, step: green, layer: api, rules: [RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5], title: "Real renewals endpoint: 422 violations, 409 for a cancelled policy; whole acceptance suite green", status: pending}
      - {id: t10, role: builder, step: refactor, layer: null, rules: [], title: "Refactor with the full suite green", status: pending}
      - {id: t11, role: orchestrator, step: gates, layer: null, rules: [], title: "build -warnaserror, full test, rule coverage (pending file empty), diff scope, no NotImplementedException in src", status: pending}
      - {id: t12, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t13, role: tester, step: qa, layer: null, rules: [RENEW-1/2, RENEW-3a, RENEW-3b, RENEW-4, RENEW-5, RENEW-6, COULD-3, CANCEL-5], title: "QA script docs/qa/build-F4.md, steps tagged per rule", status: pending}
      - {id: t14, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t15, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md, README and matrix; chore(trace) commit; completeness check passes", status: pending}
      - {id: t16, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t17, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: Postman receipt on port 5080, then sign-off from walkthrough.md, the QA script and Postman", status: pending}
      - {id: t18, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F5
    title: IaC + pipeline (evidence, never deployed)
    kind: evidence
    owner: loop
    depends_on: [F0b]
    rules: []
    cuttable: true
    budget: 3h
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F5.md
    trace: docs/trace/build/F5/
    acceptance:
      - "Hand-written Bicep modules at subscription scope; bicep build and bicep build-params succeed for dev, test and prod"
      - "Pipeline: build/test → dev → test → prod from one stage template; AzureCLI@2 with what-if; prod approval; idempotent migrations; curl /health/ready smoke; schema-checked offline"
      - "APPLICATIONINSIGHTS_CONNECTION_STRING supplied as an app setting; managed identity to Entra-only SQL; no secrets"
      - "az never run; nothing deployed"
    tasks:
      - {id: t1, role: builder, step: write, layer: null, rules: [], title: "Bicep modules, three param files and the pipeline YAML; bicep build and build-params for dev, test and prod; pipeline schema check", status: pending}
      - {id: t2, role: orchestrator, step: gates, layer: null, rules: [], title: "bicep build and build-params exit 0 for all three environments; pipeline schema check; build and tests still green", status: pending}
      - {id: t3, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t4, role: tester, step: qa, layer: null, rules: [], title: "QA script docs/qa/build-F5.md", status: pending}
      - {id: t5, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t6, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md and README; chore(trace) commit; completeness check passes", status: pending}
      - {id: t7, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t8, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: sign-off from walkthrough.md and the QA script", status: pending}
      - {id: t9, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F6
    title: README
    kind: docs
    owner: loop
    depends_on: [F4]
    rules: []
    cuttable: false
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F6.md
    trace: docs/trace/build/F6/
    acceptance:
      - "Every item in docs/readme-checklist.md is ticked and links to the README section that satisfies it; F5's items say 'cut' if F5 was cut"
    tasks:
      - {id: t1, role: builder, step: write, layer: null, rules: [], title: "README.md covering every docs/readme-checklist.md item, each ticked with a link to its section", status: pending}
      - {id: t2, role: orchestrator, step: gates, layer: null, rules: [], title: "Every checklist item ticked and linked; links resolve; build and tests still green", status: pending}
      - {id: t3, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t4, role: tester, step: qa, layer: null, rules: [], title: "QA script docs/qa/build-F6.md", status: pending}
      - {id: t5, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t6, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md and README; chore(trace) commit; completeness check passes", status: pending}
      - {id: t7, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t8, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: sign-off from walkthrough.md and the QA script", status: pending}
      - {id: t9, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
  - id: F7
    title: Pitch deck
    kind: docs
    owner: loop
    depends_on: [F6]
    rules: []
    cuttable: true
    budget: null
    status: pending
    attempt: 0
    branch: null
    worktree: null
    pr: null
    qa: docs/qa/build-F7.md
    trace: docs/trace/build/F7/
    acceptance:
      - "Generated from build.dag.yaml and docs/trace/build/**; no hand-typed counts"
    tasks:
      - {id: t1, role: builder, step: write, layer: null, rules: [], title: "Self-contained pitch-deck.html generated from build.dag.yaml and docs/trace/build/**", status: pending}
      - {id: t2, role: orchestrator, step: gates, layer: null, rules: [], title: "Regenerating the deck reproduces it byte for byte; no hand-typed counts", status: pending}
      - {id: t3, role: review, step: review, layer: null, rules: [], title: "Reviewer and security verdicts PASS for HEAD", status: pending}
      - {id: t4, role: tester, step: qa, layer: null, rules: [], title: "QA script docs/qa/build-F7.md", status: pending}
      - {id: t5, role: git, step: integrate, layer: null, rules: [], title: "Rebase onto origin/main; full suite on the post-rebase HEAD", status: pending}
      - {id: t6, role: orchestrator, step: trace, layer: null, rules: [], title: "Render walkthrough.md and README; chore(trace) commit; completeness check passes", status: pending}
      - {id: t7, role: git, step: pr, layer: null, rules: [], title: "Push, draft PR from the body template, reviewer, security and rebase comments", status: pending}
      - {id: t8, role: david, step: pause2, layer: null, rules: [], title: "Pause 2: sign-off from walkthrough.md and the QA script", status: pending}
      - {id: t9, role: david, step: merge, layer: null, rules: [], title: "David marks the PR ready and merges", status: pending}
```

**Why F0b owns the App Insights wiring, not F5:** F5 is cuttable and the logging decision is not. The wiring is app code that is testable without Azure. F5 only supplies the connection string.

### Cut order

**F7, then F5.** F0a–F4 and F6 are never cut. A cut is David's decision, recorded as `status: cut` with `decided_by: david`, and it requires a lesson.

### The four `plan-*` skills

All four start with the prior-work search (`docs/plans/` + `docs/lessons.md`), write the templates above (`.plan.md`, plus `.dag.yaml` except for investigations), compose explicit tasks from the task sequences, run the validator, and refuse a plan without ≥1 rejected alternative or with a rule id missing from requirements.md. Feature and bug plans are **micro-DAGs**: the same schema, nodes `N1…`, explicit tasks in the feature sequence, and the same worktree/branch pattern.

| Skill | Trigger | Inputs | Output |
|---|---|---|---|
| `/plan-dag` | Once, in F0a | requirements.md, spec Contracts, David's answers | `docs/plans/active/build.plan.md` (prose) + `build.dag.yaml` (draft), the build DAG above |
| `/plan-feature` | A change request, or an accepted `feature` recommendation | A description or an `inv-NNN` recommendation; new rule ids are added to requirements.md first | `feat-NNN.plan.md`, usually one node; branch `feature/feat-NNN/N1` |
| `/plan-bug` | A defect found **after merge**, incl. a comment on a merged PR (pre-merge defects stay in the failure route) | Symptom, the broken rule id, the introducing plan/node/commit from the trace + `git log -S`/blame | `bug-NNN.plan.md`, `parent: {plan, node, rule}`; branch `bug/bug-NNN/N1`. **N1's t1 is the failing reproduction test** |
| `/plan-investigation` | Attempt 3, a repeated fingerprint, or David's question | A **falsifiable** question (otherwise bounced), parent `{plan, node}` if escalating, a 30-min time box. The read-only investigator does the digging | `inv-NNN.plan.md`: question, hypothesis, evidence, findings, `recommendations:`. No DAG, no code. When escalated from a node it is committed on that node's branch; a standalone one (no parent node) is committed on a `chore/inv-NNN` branch via PR, which David merges |

The reproduction test is a task, not its own node: a merged node must leave `main` green, and pre-push runs the tests.

`/plan-dag` asks David once, defaults pre-filled from the Decision: the feature list and order; the rule → node mapping; budgets and the cut line against Monday 2026-10-19; anything deferred out of the plan; extra acceptance per node; then approval.

Recommendations block:

```yaml recommendations
- {kind: amend-node, target: build/F1, reason: "SELL-2 fails near midnight: validator reads UTC, not UkCalendar.Today", status: proposed}
- {kind: bug, target: null, rule: GET-1, reason: "GET omits the payment reference since the F1 merge", status: proposed}
```

Kinds: `feature | bug | amend-node`. David sets each `status` to `accepted | rejected`. Accepted `feature`/`bug` → `/plan-feature`/`/plan-bug` with `parent: inv-NNN`. Accepted `amend-node` → it briefs the parent node's next attempt. The investigation links forward (`opened: [feat-NNN]`) and the new plan links back (`parent`).

### Retry ladder (per node)

One attempt = one FAIL from reviewer, security or a gate. `attempt` counts them and never resets. **David's steers don't count:** a change at Pause 1, a finding at `hitl`, or a `dpmorgandev` comment on the open PR goes through the same failure route (investigate → red → fix) as `D1`, `D2`… with `findings/D<n>.md` (traceability), and leaves `attempt` unchanged.

| `attempt` becomes | Action | Node status |
|---|---|---|
| 1–2 | **Failure route:** the investigator writes `findings.md`; the tester writes a red test from it (skipped only if untestable, with a reason); the builder gets findings + red test; reviewer ∥ security re-run | `in_progress` |
| 3, **or earlier on a repeated finding fingerprint** | Stop. `/plan-investigation` with `parent: <plan>/<node>`. The run ends until David decides the recommendations | `investigating` |
| 4 | Failure route, briefed by the accepted `amend-node` | `in_progress` |
| 5 | Stop and ask David: amend the plan, cut the node, or take it over | `escalated` |

### Archive

After David merges the last node (or every node is `done|cut`), the git agent, on `chore/archive-<plan-id>`: moves both plan files to `docs/plans/archive/`, appends the completion record (values from git, never typed), appends any required lesson, and raises a PR that David merges. It refuses to archive without a lesson when one is required.

```yaml
completed: 2026-10-19
outcome: success            # success | partial (some nodes cut) | failed | abandoned
nodes:
  - id: F1
    outcome: done           # done | cut | escalated
    branch: feature/build/F1
    merge_commit: <hash>    # git log --merges --grep=<branch>
    pr: <number>
    commits: 23             # git rev-list --count <merge>^1..<merge>^2
    files_changed: 14       # git diff --name-status <merge>^1 <merge>
    attempt: 1
    qa_signed: docs/qa/build-F1.md
lessons: [L-004]            # ids in docs/lessons.md; [] only for a clean success
```

### Lessons (`docs/lessons.md`, append-only, public)

**Required** when a node reached attempt ≥ 3, was escalated, or was cut. The orchestrator drafts it from the attempt log and findings; the git agent commits it on the archive branch. Every `plan-*` skill reads it in the prior-work search. A correction is a new entry referencing the old id, never an edit.

```markdown
## L-004 · build/F1 · 2026-10-18 · #time #tdd
- Happened: SELL-2 boundary tests passed in the day, failed after 23:00 UTC during BST.
- Lesson: every date rule reads UkCalendar.Today; a 23:59/00:01 Europe/London pair is now in the testing spec.
```

## Open questions

1. ~~Approval on `main`~~ **Resolved:** `build` is approved by David's commit on `main` (allowed until F0b starts); later plans by David's commit on their first node branch.
2. ~~Archive branch~~ **Resolved:** a separate `chore/archive-<id>` PR, by the git agent.
3. ~~Mid-loop state visibility~~ **Resolved:** `/orient` reads worktree copies via `git worktree list`.
4. ~~requirements.md changes~~ **Resolved:** owed as an F0a acceptance item.
5. Resolved in reconciliation (2026-10-17): the task PDF's appendix is a field list only, with no sample records, so there's no seed data from it. Postman pre-request scripts generate the data. "Appendix data": if the task PDF's appendix holds sample records, a dev seed would help HITL in Postman. Someone needs to check the PDF.
6. **(open)** Budgets for every node except F5 (3h), and the clock time at which David cuts F7, then F5. `/plan-dag` asks.
7. Resolved (David, 2026-10-17): no. Only reviewer, security or gate FAILs count. David's steers at Pause 1, Pause 2 and in PR comments go investigate → red → fix as `D<n>` without incrementing `attempt` (Retry ladder).
8. Resolved (David, 2026-10-17): on a `chore/inv-NNN` branch via PR (the four `plan-*` skills table).
9. Resolved in reconciliation (2026-10-17): F7 is a self-contained `pitch-deck.html`, as David suggested. F7's output format (slides file or markdown).
