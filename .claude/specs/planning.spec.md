# planning.spec.md
Status: RESEARCH COMPLETE — decision pending

Scope: the one plan format every code-changing plan uses (reasoning + YAML DAG), how the
orchestrator reads and advances it, a node's definition of done, the plan lifecycle and its
completion record, what `/plan-dag` asks David, the four `plan-*` skills, the investigation
→ recommended-plan handoff, the retry ladder, and lessons. Source: `docs/requirements.md` §3
"Planning model", §4 row 3.

## Dependencies

Researched in parallel and **not decided yet**. This spec assumes:

| Spec | What this spec assumes | If the assumption breaks |
|---|---|---|
| `api-contract` | Features map onto endpoints: Sell = `POST /policies`, Retrieve = `GET /policies/{reference}`, Cancel = `POST /policies/{reference}/cancellation` (+ quote, + `POST …/claims`), Renew = `POST /policies/{reference}/renewals`. The DAG names only the endpoints. Routes are owned there | Only the `endpoints:` strings in the template change |
| `agents-skills` | Roles: `tester`, `builder`, `reviewer`, `security`, `git`. The orchestrator is the main session, not an agent. `david` is a valid owner | Rename the `role:` enum |
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

1. **One file, one truth.** `docs/plans/active/<plan-id>.plan.md` holds the frontmatter, the reasoning prose and **exactly one** fenced ```` ```yaml dag ```` block under `## DAG`. There is no manifest. `/orient` lists `docs/plans/active/*.plan.md` and reads each plan's frontmatter and node statuses.
2. **Ids:** `build`, `feat-NNN`, `bug-NNN`, `inv-NNN`, with NNN = max over `active/` + `archive/` + 1, zero-padded. No slugs (the title lives in frontmatter). Node ids are `F0a, F0b, F1…F5` in `build`, and `N1, N2…` in feat/bug plans (`bug-NNN`'s `N1` is always the reproduction test). Task ids are node-local (`t1…`). Trace key: `docs/trace/<plan-id>/<node-id>/`.
3. **Status writes are branch commits** (A2), labelled `plan(<plan-id>/<node-id>): <from> → <to>`. Only the orchestrator writes node/task status. Only David writes `approved`. Plan-level `status` is derived (see Contract), never hand-set except `draft → approved`.
4. **Node DoD is mechanical first, human last.** Every item is a check whose output is evidence (§Contract "Node definition of done"). `done` is set as the last commit before the PR. **It is only true on `main` once David has merged.** `/orient` flags any node marked `done` on a branch that isn't merged.
5. **Build order (D2 + C2):** F0a harness → F0b skeleton → F1 Retrieve → F2 Sell → F3 Cancel (+COULD-1 quote, +COULD-2 claims) → F4 Renew (+COULD-3 cheque, **+CANCEL-5**). F5 IaC/deploy evidence `depends_on: [F0b]`, `cuttable: true`, budget 3h, scheduled last.
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
  - **F8 React API-testing frontend:** cuttable, a stretch goal, in `src/web/`. Starts only after F0–F6 pass.
- [x] **Cut order:** F8 → F7 → F5. F0–F4 and F6 are never cut.
- [x] **Your involvement per feature: two pauses.**
  - **Pause 1, mid-loop:** the acceptance tests are red and the stub controller is green. You call the stub in Postman and read the tests, then the loop continues.
  - **Pause 2, HITL sign-off:** reviewed using a generated `walkthrough.md`, which lists each commit in order (red, stub, domain, services, persistence, refactor) with one line each, plus the QA script and Postman.
  - These pauses change orchestration's loop and requirements.md §3.

## Contract — PENDING (DRAFT: plan file template incl. YAML DAG schema example for F1)

### Lifecycle

```text
plan:  draft ──(David sets approved)──► approved ──(first node in_progress)──► active
       active ──(every node done|cut)──► done ──(archive procedure, merged)──► archived
node:  pending ──► in_progress ──► hitl ──► done          (done = merged into main)
                       │  ▲          │
                       ▼  │ (fail)   └─(David rejects: back into loop, attempts++)
                    investigating (attempt 3) ──► in_progress (attempt 4)
                       └──► escalated (attempt 5 fail; David decides)      any ──► cut (David only)
task:  pending ──► done            (red/green/refactor commits are the evidence; see trace)
```

Plan-level `status` is derived. The file stores it only as a cache that the orchestrator refreshes on each status commit. `investigation` plans have no DAG: `draft → approved → active → done (findings + recommendations written) → archived`.

### Who may change what

| Field | Writer | Mechanism |
|---|---|---|
| `approved_by`, `approved_on`, plan `draft→approved` | David only | David's own commit to `main`. The orchestrator refuses a plan without it |
| node/task `status`, `attempts`, `branch`, `pr` | orchestrator | Commit on the feature branch in the worktree |
| node `cut`, escalation outcome | David | Recorded by the orchestrator with `decided_by: david` |
| reasoning prose | the `plan-*` skill at draft time. Amended only by David or a new plan | — |

### Orchestrator read algorithm

1. Parse the single ` ```yaml dag ` block. Validate the invariants: ids unique; every `depends_on` resolves; no cycle; every `rules:` id exists in `docs/requirements.md` (**a missing id is a hard stop: `/plan-feature` adds rule ids there first**); every node has ≥1 `acceptance`.
2. Refuse if plan `status` is `draft`.
3. **Next node** = the first node, in file order, with `status: pending`, all `depends_on` `done`, and `owner` ≠ `david`. If a node is `in_progress | investigating | hitl | escalated`, resume it instead (one feature at a time). A `david` node blocks its dependents until David marks it done.
4. If there is no next node and every node is `done|cut`, the plan is `done` → archive.

### Node definition of done (all items, in order; each leaves evidence in `docs/trace/<plan-id>/<node-id>/`)

1. Every task `done`. Every rule in `rules:` has ≥1 unit test **and** ≥1 integration test naming the rule id (the testing spec sets the naming).
2. The reviewer verdict is PASS against `rules:` + `acceptance:`, and the security verdict is PASS (verdict JSON per orchestration).
3. Rebased onto the latest `main`. Full suite green **in the worktree** (unit + integration + architecture). `dotnet build` has zero warnings.
4. Postman requests for every endpoint in `endpoints:`, including one example per rule-violation response.
5. QA script written at `qa:`. **David has run it from the worktree and signed it** (the sign-off line is in the QA file, committed).
6. PR raised. `status: done` committed as the branch's last commit. **David merges**: only then is it done on `main`.

`owner: david` nodes (F0a) skip items 2 and 4. Their acceptance is David's review in-session plus item 3.

### Plan file template (`docs/plans/active/<plan-id>.plan.md`)

````markdown
---
plan_id: build            # build | feat-NNN | bug-NNN | inv-NNN
kind: build               # build | feature | bug | investigation
title: Initial build of the Uinsure Policies API
status: approved          # derived cache: draft|approved|active|done|archived
created: 2026-10-17
approved_by: David Morgan # set by David only; empty while draft
approved_on: 2026-10-17
requirements_rev: <git short hash of docs/requirements.md at approval>
parent: null              # bug: {plan, node, rule}; feat/bug from an investigation: inv-NNN
---
# build: Initial build of the Uinsure Policies API
## Why
## Approach
## Rejected alternatives      <!-- ≥1, required: the skill refuses to write the plan without one -->
## Prior work consulted       <!-- grep hits from docs/plans/ + docs/lessons.md, or "none" -->
## DAG
```yaml dag
nodes:
  - id: F2                       # stable; trace key docs/trace/build/F2/
    title: Sell
    kind: feature                # harness | feature | bug | evidence
    owner: loop                  # loop | david
    depends_on: [F1]             # F1 = Retrieve (if D2 adopted)
    rules: [SELL-1, SELL-2, SELL-3, SELL-4, SELL-5, SELL-6]
    endpoints: ["POST /policies"]          # names per api-contract.spec (assumed)
    cuttable: false
    budget_hours: 5
    status: pending              # pending|in_progress|investigating|hitl|escalated|done|cut
    attempts: 0                  # failed reviewer/security cycles on this node
    branch: null                 # set by git agent, e.g. feature/F2-sell
    pr: null
    qa: docs/qa/build-F2.md
    investigations: []           # inv-NNN opened at attempt 3
    tasks:                       # executed in order; edges only between nodes
      - {id: t1, role: tester,  step: acceptance, rules: [SELL-6], title: "Failing POST→GET round-trip test + Postman request", status: pending}
      - {id: t2, role: builder, step: stub,       title: "Controller returns hard-coded 201 so Postman can call it", status: pending}
      - {id: t3, role: tester,  step: tdd, rules: [SELL-2], title: "Start date today..today+60 inclusive; past → 422", status: pending}
      - {id: t4, role: tester,  step: tdd, rules: [SELL-3], title: "End = start + 1y − 1d; 29 Feb start", status: pending}
      - {id: t5, role: tester,  step: tdd, rules: [SELL-4, SELL-5], title: "1–3 holders; ≥16 on start date (16th birthday passes)", status: pending}
      - {id: t6, role: tester,  step: tdd, rules: [SELL-1], title: "Duplicate reference → 409; unique index", status: pending}
      - {id: t7, role: tester,  step: tdd, rules: [SELL-6], title: "Payment created for full amount with generated reference", status: pending}
      - {id: t8, role: tester,  step: violations, title: "Integration tests + Postman examples for every 409/422 ProblemDetails", status: pending}
    acceptance:
      - "POST then GET returns the sold policy with end date, holders, property and payment"
      - "Each SELL rule violation returns RFC 7807 naming the rule in plain English"
      - "Boundaries tested with FakeTimeProvider: day 0, day 60, day 61, yesterday; 16th birthday on start"
```
<!-- `step: tdd` tasks expand into red (tester) → green (builder) → refactor (builder) commits.
     The tester/builder split per step is orchestration's to fix; the plan names the rule, not the keystrokes. -->
## Attempt log                <!-- orchestrator appends: attempt n · date · verdict ref · what changes next -->
## Completion record          <!-- appended at archive, from git; see below -->
````

**Draft build DAG (node headers only):** F0a harness (`owner: david`, kind harness, no rules) → F0b walking skeleton (deps F0a; acceptance: architecture test fails on a deliberate layering breach, health endpoint callable in Postman, loop ran end to end) → F1 Retrieve [GET-1] → F2 Sell [SELL-1..6] → F3 Cancel [CANCEL-1a/1b/1c/2/3/4/6, COULD-1, COULD-2] → F4 Renew [RENEW-1/2, 3a, 3b, 4, 5, 6, COULD-3, CANCEL-5] · F5 IaC/deploy evidence (kind evidence, deps F0b, cuttable, budget 3h, scheduled after F4).

### Completion record (appended at archive, values from git, never typed)

```yaml
completed: 2026-10-19
outcome: success            # success | partial (some nodes cut) | failed | abandoned
nodes:
  - id: F2
    outcome: done           # done | cut | escalated
    branch: feature/F2-sell
    merge_commit: <hash>    # git log --merges --grep=<branch>
    pr: <number/url>
    commits: 23             # git rev-list --count <merge>^1..<merge>^2
    files_changed: 14       # git diff --name-status, list in trace not here
    attempts: 1
    qa_signed: docs/qa/build-F2.md
lessons: [ "L-004" ]        # ids in docs/lessons.md; [] allowed only for clean success
```

### What `/plan-dag` asks David (one round, defaults pre-filled; it runs once)

1. Feature list and order: confirm the draft DAG above (F0 split? Retrieve-first?).
2. Rule → node mapping: confirm, especially CANCEL-5 → F4 and the COULDs' homes.
3. Budget per node, and the cut line: which nodes are `cuttable` (default F5 only) and the deadline arithmetic against Monday 2026-10-19.
4. Anything in requirements.md David wants **out** of the build plan (deferred to a later `feat-NNN`).
5. Acceptance additions per node beyond the generated ones (David's own interview talking points).
6. Approve: David sets `approved_by/approved_on` and commits. Until then the file stays `draft`.

Before asking, it searches `docs/plans/` and `docs/lessons.md` (prior work) and reads requirements.md + all specs' Contracts. It **refuses** to emit a node whose rule id isn't in requirements.md, and a plan without ≥1 rejected alternative.

### The four skills

| Skill | Trigger | Inputs | Output | Built |
|---|---|---|---|---|
| `/plan-dag` | Once, after analysis | requirements.md, spec Contracts, David's answers | `docs/plans/active/build.plan.md` (draft) | **Now** |
| `/plan-feature` | A change request to the live system, or an approved `feature` recommendation | Description or `inv-NNN` rec. Adds new rule id(s) to requirements.md first | `feat-NNN.plan.md`, usually 1 node | First change request |
| `/plan-bug` | A defect found **after merge** (pre-merge defects stay in the feature loop) | Symptom, the broken rule id, the introducing plan/node/commit found from the trace + `git log -S`/blame | `bug-NNN.plan.md`. **`N1` = failing reproduction test** (tester only), `N2` = fix | First escaped defect |
| `/plan-investigation` | Attempt 3, a stuck loop, or David's question | A **falsifiable** question (otherwise bounced), parent `{plan,node}` if escalating, 30-min time box | `inv-NNN.plan.md`: question, hypothesis, evidence, findings, `recommendations:` list. **No DAG, no code, no branch** | First stuck loop (may be during F1/F2) |

All four start with the prior-work search, and all write the template above. Investigation recommendations block:

```yaml recommendations
- {kind: amend-node, target: build/F2, reason: "SELL-3 failures are TimeProvider not injected into validator; inject and retest", status: proposed}
- {kind: bug, target: null, rule: GET-1, reason: "GET omits payment reference since F1 merge", status: proposed}
```

David flips each `status` to `accepted | rejected`. Accepted `feature`/`bug` → the orchestrator runs `/plan-feature`/`/plan-bug` with `parent: inv-NNN`. Accepted `amend-node` → the parent node's attempt 4 is briefed with it. The investigation links forward (`opened: [feat-NNN]`) and the new plan links back (`parent`), which gives traceability its cross-link.

### Retry ladder (per node; one attempt = one FAIL verdict cycle from reviewer or security, or a David rejection at HITL)

| Attempt | Action | Node status |
|---|---|---|
| 1–2 | Bug report → tester/builder. The brief must state **what is different** from the last attempt (logged in Attempt log) | in_progress |
| 3 | Stop the loop. `/plan-investigation` with `parent: <plan>/<node>`. The run ends: David reviews the recommendations | investigating |
| 4 | Retry, briefed by the accepted `amend-node` recommendation | in_progress |
| 5 (fails) | Stop. Escalate to David with the attempt log + investigation. David chooses: amend the plan, cut the node, or take it over | escalated |

Never more than 5. The counter does not reset when the failure changes.

### Lessons (`docs/lessons.md`, append-only, public repo, so nothing private)

```markdown
## L-004 · build/F2 · 2026-10-18 · #time #tdd
- Happened: SELL-2 boundary tests passed locally, failed after midnight UTC.
- Lesson: every date rule reads TimeProvider; a test at 23:59/00:01 is now in the testing spec.
```

Written at archive (required on attempt ≥ 3 / escalated / cut / failed) and at the close of every investigation. Read by the prior-work search of every `plan-*` skill. Corrections are new entries that reference the old id, never edits.

## Open questions

1. **Approval on `main`:** David approving = David committing the plan to `main`. Confirm that David committing to `main` himself is fine (the rule only forbids *agents* pushing to `main`).
2. **Archive branch:** a separate `chore/archive-<id>` PR per plan, or fold the archive into the next feature branch's first commit? The second option saves a PR but mixes concerns. Guardrails/orchestration to confirm.
3. **Mid-loop state visibility:** with A2, `main` shows `F2: pending` while F2 is in flight. `/orient` must read the worktree's copy of the plan. Context spec to confirm.
4. **"Appendix data":** if the task PDF's appendix holds *sample records* (not just the schema), a dev seed makes Retrieve-first natural for HITL in Postman. Not verified here: someone needs to check the PDF.
5. **Requirements.md changes implied:**
   - the F0 split
   - the F1/F2 order
   - CANCEL-5 moves to F4
   - `amend-node` as a recommendation kind
   - attempts counted per node
   - no `/plan-archive` skill
