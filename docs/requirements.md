# Uinsure Policies API: Requirements

Author: David Morgan · Last updated: 2026-10-17 · Deadline: Monday 2026-10-19

This is the **single input** for the work. The sequence is:

0. **Context first.** Build `context.spec.md` and the `/orient`, `/save-context` and `/load-context-chain` skills **before** the rest of the analysis. The analysis itself will span sessions over two days, so it needs them from the start.
1. **Analysis** reads this file and produces the specs in `.claude/specs/`, plus the `/plan-dag` skill.
2. **`/plan-dag`** produces the build plan, `docs/plans/active/build.plan.md`.
3. **Feature F0** builds the harness.
4. **Features F1 and onwards** build the API, one feature at a time, with a sign-off from David (human in the loop) after each.

Sources: `Uinsure - Tehcnial Challenge -Backend.pdf` (the task) and `Uinsure---Senior-Software-Engineer---2026.pdf` (the role).

---

## 1. Context

**The task.** Build a REST API in C# for storing home insurance policies. It is the take-home for the second-round interview for Uinsure's Senior Software Engineer role. The task's constraints:
- a C# REST API using the appendix data
- automated tests
- informative responses
- a public GitHub repo

The code is the agenda for a two-way technical discussion. **Every line must be something David can explain and defend.**

**What the role spec tells us to show:**
- .NET 8 upgrading to 10
- Azure, Azure SQL, EF Core
- Azure DevOps, Bicep IaC, push-button deployments
- TDD (listed as non-negotiable)
- clear code and handling of ambiguity
- pragmatism ("done is better than perfect")
- AI use with a critical eye ("no AI slop")

**Principles.**
- Code that can be explained beats clever code.
- Done beats perfect.
- A visible process beats a tidy end state.
- Their stack beats our preferences.
- Written assumptions beat silent guesses.

---

## 2. Functional requirements

Policy types: **Household**, **Buy to Let**. Payment methods: **Card**, **Direct Debit**, **Cheque**.

All of MUST, SHOULD and COULD are in scope. Each rule appears with the interpretation we agreed for it.

### Sell (MUST-1)

| ID | Rule | Agreed interpretation |
|---|---|---|
| SELL-1 | Policy reference must be unique | The client supplies the reference. A duplicate returns **409**. A unique DB index enforces it too |
| SELL-2 | Can only be sold up to 60 days in advance | Start date must be between today and today + 60, inclusive. **No backdating**: otherwise **422** |
| SELL-3 | Always 1 year in length | The client sends only the start date. End date = **start + 1 year − 1 day**, calculated, cover inclusive. A 29 Feb start is handled and tested |
| SELL-4 | 1 to 3 policyholders | — |
| SELL-5 | All policyholders over 16 on the start date | **16 or older** on the start date (a 16th birthday on the start date passes) |
| SELL-6 | Payment at sale | The request carries the payment method. A Payment is created for the full policy amount, with a generated reference |

### Retrieve (MUST-2)

| ID | Rule | Agreed interpretation |
|---|---|---|
| GET-1 | Retrieve a policy | By reference. Returns the policy, its terms, policyholders, property, payments and refunds. **404** if not found |

### Cancel (SHOULD-1)

| ID | Rule | Agreed interpretation |
|---|---|---|
| CANCEL-1a | Before the start date → full refund | — |
| CANCEL-1b | On or after the start date, within the 14-day cooling-off → full refund | Cooling-off runs to **start + 14 days, inclusive** (UK consumer-law convention: counted from the day after the start) |
| CANCEL-1c | After cooling-off → pro-rata for unused days | Refund = amount × unused days ÷ **actual days in the term** (365 or 366). The cancellation day counts as used |
| CANCEL-2 | Refund to the same method as the original payment | Each refund goes to the method of the payment it reverses |
| CANCEL-3 | Cancellation date | Optional, defaults to today. **No past dates** (422) |
| CANCEL-4 | Cancel twice | **409**, giving the original cancellation date |
| CANCEL-5 | Cancelling a renewed policy | Ends the policy. The current term follows the normal rules. A future term is refunded in full (it is before its start date) |
| CANCEL-6 | Rounding | 2dp, `MidpointRounding.AwayFromZero`, applied in one place (.NET defaults to banker's rounding) |

### Renew (SHOULD-2)

| ID | Rule | Agreed interpretation |
|---|---|---|
| RENEW-1/2 | Renew within 30 days of the end date, not after it | Window: **end − 30 days to the end date, inclusive** |
| RENEW-3a | Auto-renew on → payment created | The method comes from the request, defaulting to the current term's payment method |
| RENEW-3b | Auto-renew off → no payment | The term is created unpaid. Collecting that payment is out of scope |
| RENEW-4 | What a renewal is | The policy has **terms**. Renewal appends the next term, starting the day after the current end date, under the same reference. *Why:* renewal happens while the current term is still running, so overwriting dates would break cancellations made in its final 30 days |
| RENEW-5 | Price | The client supplies the new term's amount, which must be greater than 0 (pricing belongs to another team) |
| RENEW-6 | A cancelled policy | Can't be renewed: **409** |

### Could-haves (all in scope)

| ID | Rule | Agreed interpretation |
|---|---|---|
| COULD-1 | Quote the cancellation refund without cancelling | A read-only version of the cancel calculation |
| COULD-2 | No refund if the policy has a claim | A claim means a **£0 refund**, overriding CANCEL-1a/1b. Claims are recorded with `POST /policies/{reference}/claims`. HasClaims is false at sale |
| COULD-3 | No cheque for an auto-renewing policy's renewal | Rejected with 422 |

### Data (appendix, plus our additions)

- **Policy:** reference, type, start date, end date, amount, HasClaims, AutoRenew. *Added:* terms, cancellation.
- **Policyholder:** first name, last name, date of birth.
- **Property:** address line 1 (required), address lines 2 and 3, postcode (required, max 8 characters).
- **Payment:** reference, type, amount.
- **Refund:** reference, type, amount.
- *Added:* **Term** (start, end, amount, payment) and **Claim**.

**Out of scope:** authentication, pricing, real payment processing, live deployment.

**Response standard:** every rule violation returns RFC 7807 ProblemDetails that name the broken rule in plain English.

---

## 3. Delivery requirements

### How features are built

- **Outside-in TDD.** For each endpoint:
  1. Write a failing integration test and add the Postman request.
  2. Add a controller that returns a hard-coded response, so the endpoint can be **called in Postman before the layers behind it exist**.
  3. Do unit-level TDD of each rule (red → green → refactor, one commit per step). Real behaviour replaces the stub.
  4. Add integration tests and Postman examples for the rule-violation responses.
- **Tests:** architecture tests (enforce layering), integration tests (`WebApplicationFactory`), unit tests (every rule, every boundary, injected `TimeProvider`), and a Postman collection with an environment.
- **One feature at a time.** David must fully understand each feature before the next one starts.

### Feature loop

1. An **orchestrator** in the main session picks up the next feature from the plan DAG.
2. The **git agent** creates the feature branch and its **worktree** (for example `../wt/F1-sell` on branch `feature/F1-sell`). All agents for the feature work only inside that worktree. `main` and David's own checkout are never touched by agents.
3. **tester** (red) → **builder** (green) → **reviewer** (requirements met?) + **security** (OWASP).
4. If the reviewer or security fails it, the bug report goes back to the tester and builder. The loop repeats until it passes, following the retry ladder in *Planning model*: attempt 3 opens a `/plan-investigation`, and attempt 5 stops and escalates to David.
5. **Integration (git agent):**
   - rebase the feature branch onto the latest `main`
   - run the full test suite inside the worktree (unit, integration and architecture)
   - only then raise the PR
6. A **QA manual test script** is produced for the feature. David runs the API **from the worktree**, steps through the script in Postman, runs the tests, and signs off. **David is the acceptance gate.**
7. **David merges** the PR. The git agent removes the worktree and deletes the branch.

*Proposed: no separate integration agent.* Features run one at a time, so a feature branch can only fall behind `main` by changes David has already merged. A mechanical rebase-and-test step in the git agent, followed by David's HITL, covers integration. This is to be confirmed in `orchestration.spec`.

### Planning model

Every plan that changes code is a **DAG** in one shared format. The orchestrator only ever executes DAGs.

```text
/plan-dag            the initial full build     ─┐
/plan-feature        a change to a live system  ─┼─► DAG (same schema) ─► orchestrator ─► loop ─► HITL ─► PR
/plan-bug            an escaped defect          ─┘
/plan-investigation  read-only reasoning ─► findings + recommended plans ─► David approves ─► /plan-feature or /plan-bug
```

- **`/plan-dag`:** the build plan, F0 to F5. It runs once, and closes when the system exists.
- **`/plan-feature`:** a change to the live system. It produces a small DAG (usually one feature node with tasks) that goes through the same loop, HITL and PR. If the change introduces a new rule, a **new rule ID is added to this file first**, so every change still traces back to a requirement.
- **`/plan-bug`:** an escaped defect, meaning one found *after* merge. Its **first node is always a failing test that reproduces the bug.** It links to the broken rule ID and to the plan and node that introduced the faulty code (found through the activity trail). A defect David finds during HITL *before* merge isn't a plan-bug: it goes back into that feature's loop.
- **`/plan-investigation`:** never changes code and never produces a DAG. Its output is findings, evidence, and **recommended plans** (feature or bug, each with a one-line reason). David chooses which to open, which keeps "decide what to do" apart from "do it". It is also the **attempt-3 escalation** in the retry ladder:
  - attempts 1–2 retry with a different approach
  - attempt 3 opens an investigation
  - attempt 5 stops and escalates to David
- **Plan format:** one file per plan, `docs/plans/active/<plan-id>.plan.md`. It holds the reasoning (why, approach, rejected alternatives) and a fenced YAML block with the DAG, which the orchestrator reads. The build plan uses the same format at a larger scale. Plan IDs: `build`, `feat-NNN`, `bug-NNN`, `inv-NNN`.
- **Archive:** after David merges, the plan moves to `docs/plans/archive/` with a completion record built from git facts. Any lesson is appended to `docs/lessons.md`.
- **Prior work:** before a new plan is written, `docs/plans/` is searched. This replaces agentflow's separate `check-existing-work`, `capture-lesson` and `consult-lessons` skills, which aren't worth separate skills at this volume.
- **When the skills get built:** `/plan-dag` is built for the build. `/plan-feature`, `/plan-bug` and `/plan-investigation` are **specified now but built only when first needed**: the first escaped defect, change request or stuck loop.
- *Prior art:* `agentflow_phase3/.claude/skills/` (`plan-feature` 120 lines, `plan-bug` 114, `plan-investigation` 139, `plan-archive` 152).

### Rules

- The builder cannot edit tests.
- The tester cannot edit `src/`.
- The reviewer and security agents are read-only.
- No PR without a PASS verdict.
- No push to `main`.
- Making the repo public and every push are David's actions.

### Pipeline and environments

- **No CI.** The task doesn't require it. Local git hooks run the build and tests. Whether the hooks also run Postman is open.
- **Infrastructure as code and deployment are evidence, not deployed.**
  - Bicep for **dev, test and prod**, plus an Azure DevOps multi-stage pipeline with an approval gate before prod.
  - Both are validated locally and never connected to Azure.
  - This work comes last and has a fixed time budget. It is the first thing cut if time runs short.

### Repository layout

```text
CLAUDE.md          agent entry point
.claude/           the harness: specs/, agents/, skills/, hooks/, workflows/, settings.json
docs/              project records, kept separate from the harness:
                   requirements.md (this file), lessons.md,
                   plans/active/ + plans/archive/ (<plan-id>.plan.md), context/ (session snapshot chain),
                   trace/<plan-id>/<node-id>/ (trails), qa/ (signed-off QA scripts)
src/               application
tests/             test projects + Postman collection
infrastructure/    Bicep + Azure DevOps pipeline (evidence)
```

Nothing in `src/` or `tests/` may reference `.claude/`.

### Stack

- .NET 10 LTS (SDK 10.0.402 is installed)
- Visual Studio 2026
- ASP.NET Core Web API with controllers
- EF Core: SQLite locally, Azure SQL in the environments
- xUnit, with plain `Assert` (FluentAssertions is commercial from v8)

---

## 4. Analysis: what it must produce

Each spec has four sections: **Research → Options → Decision → Contract**. Agents act only on the Contract. Decisions are recorded inside the spec.

| # | Spec (`.claude/specs/`) | Must settle |
|---|---|---|
| 0 | `context.spec.md` | **Written first.** Keeps work continuous across sessions and days. <ul><li>**`/save-context`:** a snapshot of where we are (feature, step, loop count, decisions made, open questions, next action), chained backwards with a `previous-context` link</li><li>**`/load-context-chain`:** walk the chain back to the last checkpoint, combine it into one "where we are" brief, and get David's confirmation before resuming</li><li>**`/orient` (this project only: it never reads outside the repo, because the repo is public):** pull together as much context as possible before work starts: <code>CLAUDE.md</code>, this file, the specs, plan DAG status, the latest context chain, <code>git status</code>, open worktrees and PRs, and the trace for the feature in flight</li><li>**Mechanical leg:** a <code>SessionStart</code> hook that triggers or prompts <code>/orient</code>. In agentkinOS, <code>/orient</code> was prose-only and no SessionStart hook was registered</li><li>Snapshots live in <code>docs/context/</code> and are committed, as project records rather than harness. The repo goes public, so a snapshot must never carry anything private</li><li>Snapshot size cap and checkpoint cadence</li><li>Use the real clock for timestamps. An invented timestamp once broke the chain ordering in agentkinOS</li></ul>*Prior art to port (the procedure, not the machinery):* `agentkinOS/.claude/skills/save-context/SKILL.md` (156 lines: 500-line cap, a checkpoint every 5 snapshots, `contexts/checkpoints.md`), `load-context-chain/SKILL.md` (74 lines, read-only, human confirms before resuming), `orient/SKILL.md` (99 lines). These depend on OS packages (`@substrate/orientation`, the native context receipt), so they're ported as plain procedures. |
| 1 | `architecture.spec.md` | <ul><li>Project structure and layering</li><li>Domain model</li><li>Where rules live, and services</li><li>Persistence and EF provider switching</li><li>Validation</li><li>Error contract</li><li>Time and money handling</li><li>Logging</li><li>OpenAPI and its UI</li><li>**Considered and rejected:** event-driven / domain events, caching, CQRS / MediatR, auth</li><li>Architecture-test library and its rules</li></ul> |
| 2 | `api-contract.spec.md` | <ul><li>Routes</li><li>Request and response DTOs</li><li>Status code per rule ID</li><li>Error body</li></ul> |
| 3 | `planning.spec.md` | <ul><li>The shared plan format: the reasoning, plus a YAML DAG of nodes → tasks → rule IDs → dependencies</li><li>Plan IDs, and the lifecycle from active to archive</li><li>Definition of done per node</li><li>What `/plan-dag` asks David</li><li>The four `plan-*` skills and their inputs and outputs</li><li>Investigation → recommended-plan handoff</li><li>Retry ladder</li><li>Lessons</li></ul> |
| 4 | `testing.spec.md` | <ul><li>What each test type proves</li><li>Naming and builders</li><li>`FakeTimeProvider`</li><li>Mapping from rule ID to tests</li><li>Postman structure</li><li>QA script template and sign-off</li><li>Postman in hooks or not</li></ul> |
| 5 | `agents-skills.spec.md` | <ul><li>Roster: role, tools allowed/denied, model, preloaded skills, and the never-dos of each agent</li><li>The skills to build, and which agents use them</li><li>Why planning and orchestration aren't agents</li></ul> |
| 6 | `orchestration.spec.md` | <ul><li>The feature loop</li><li>Workflow vs. skill</li><li>Verdict JSON schema and bug report</li><li>Loop cap and escalation</li><li>Trace per feature</li><li>HITL step</li><li>Branch, commit and PR conventions</li></ul> |
| 6b | `traceability.spec.md` | Three trails, keyed by plan and node in `docs/trace/<plan-id>/<node-id>/`, forming one chain: rule ID → plan → node → tests + code → verdicts → QA sign-off → PR. <ul><li>**Requirement trail:** rule ID → tests → code → QA step → sign-off</li><li>**Cross-links:** investigation → the plans it recommended; bug → the broken rule and the plan/node that introduced it</li><li>**Activity trail:** which agent touched which files, in which loop, and in which commit. Candidate mechanisms: a `PostToolUse` hook logging agent and file, and commit trailers such as `Agent: builder`</li><li>**Verdict trail:** each reviewer and security verdict, the bugs raised, and the loops taken</li></ul> Also settles which trails are generated mechanically and which are written by agents. A self-reported trail isn't a receipt. |
| 7 | `guardrails.spec.md` | <ul><li>Each rule in §3 mapped to the hook or permission that enforces it</li><li>Git hooks</li><li>Secrets protection</li></ul> |
| 8 | `infrastructure.spec.md` | <ul><li>Azure resources (App Service or Container Apps, Azure SQL, Key Vault, App Insights)</li><li>Bicep modules</li><li>`.bicepparam` file per environment</li><li>Naming</li><li>Managed identity</li><li>Sizing</li></ul> |
| 9 | `deployment.spec.md` | <ul><li>Azure DevOps stages and environments</li><li>Approvals</li><li>Service connections</li><li>Bicep what-if and deploy</li><li>EF migrations</li><li>Smoke tests</li></ul> |

Analysis also produces the **`/plan-dag` skill**. The draft feature list for the build plan:
- **F0** harness and walking skeleton
- **F1** Sell
- **F2** Retrieve
- **F3** Cancel, with the quote and claims
- **F4** Renew, with the cheque rule
- **F5** infrastructure and deployment evidence

**Order:** context → architecture → api-contract → planning → testing → agents-skills → orchestration → traceability → guardrails → infrastructure → deployment.

**Guard against over-building:** every spec, agent and skill must be used by F0 or F1. Anything that isn't is cut. David's agentkinOS audit found 46–82% of the declared machinery was never wired up.

---

## 5. Research inputs for the analysis

Findings checked on 2026-10-17:

- **Subagents.** They can dispatch subagents (3 levels by default). Agent files can preload `skills:` and set `tools` / `disallowedTools`. Source: [sub-agents](https://code.claude.com/docs/en/sub-agents).
- **Workflows.** `agent(prompt, {schema})` returns validated JSON, and there's **no user input during a run**. That fits one workflow run per feature, with HITL in the session afterwards. Workflows need explicit opt-in. A plain subagent's output can't be forced into a schema. Source: [workflows](https://code.claude.com/docs/en/workflows).
- **Hooks.** `SubagentStop` can block. Nothing fires on PR creation, so a PR gate needs a `PreToolUse` deny plus a permission deny. Source: [hooks](https://code.claude.com/docs/en/hooks).
- **Headless.** Headless `claude -p` and the Agent SDK use an API key. David's LESSONS record the headless orchestrator as ToS-barred on Max, so orchestration stays interactive.
- **CLAUDE.md imports.** `CLAUDE.md` can `@import` specs, and Claude Code ignores folders it doesn't recognise under `.claude/`. Source: [memory](https://code.claude.com/docs/en/memory).
- **Postman.** Newman can't read Postman v12+ collections, so use the Postman CLI. Source: [Postman docs](https://learning.postman.com/docs/collections/using-newman-cli).
- **OWASP.** The API Security Top 10 2023 is the latest edition found. Confirm on owasp.org. Auth-related items are out of scope here and should be stated, not skipped. Source: [Nordic APIs](https://nordicapis.com/a-look-at-the-owasp-api-security-top-10-2023/).
- **Architecture tests.** NetArchTest is lighter. ArchUnitNET adds cycle detection. .NET 10 support is unconfirmed. Source: [comparison](https://www.dotnet-guide.com/articles/software-architecture/netarchtest-archunitnet-roslyn-analyzers/).
- **Lessons from David's own harnesses.** Sources: agentflow_phase3 and agentkinOS.
  - Worth borrowing: the fresh-context evaluator (a producer never grades its own work), deterministic gates outside the agent, and the anti-oracle-editing checks.
  - Avoid **commit-on-stop**: it would pollute a history that has to show TDD.
- **Proven on the deleted first scaffold.**
  - A Stop hook that refuses to finish while `dotnet build` fails (warnings as errors) worked.
  - `.gitattributes` must keep `*.sh` as LF.

---

## 6. Open items (settled during analysis)

1. Postman in the pre-push hook: yes or no.
2. GitHub remote for PRs: private now, public at submission. David creates it, or explicitly approves.
3. Workflow vs. skill for orchestration.
4. Confirm the Bicep CLI / `az` is installed so the infrastructure can be validated locally.
5. **Worktrees: research needed.**
   - How do subagents and workflow agents run *inside a specific worktree*? Agent frontmatter's `isolation: worktree` gives **each agent its own** worktree. That's wrong here, because the tester and builder must share one.
   - Running the API from a worktree for HITL: which port and which SQLite file.
   - How VS 2026 opens a worktree.
6. **Traceability: research needed.** Does hook input identify which subagent made a tool call (`agent_id` / `agent_type`)? If it does, the activity trail can be mechanical rather than self-reported.
7. Integration: confirm that David plus the git agent's rebase-and-test step is enough, rather than a separate integration agent.
