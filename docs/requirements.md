# Uinsure Policies API: Requirements

Author: David Morgan · Last updated: 2026-10-17 · Deadline: Monday 2026-10-19

This is the **single input** for the work. The sequence is:

0. **Context first.** Build `context.spec.md` and the `/orient`, `/save-context` and `/load-context-chain` skills **before** the rest of the analysis. The analysis itself will span sessions over two days, so it needs them from the start.
1. **Analysis** reads this file and produces the specs in `.claude/specs/`, plus the `/plan-dag` skill.
2. **`/plan-dag`** produces the build plan: `docs/plans/active/build.dag.yaml` (the DAG the orchestrator loops through) and `build.plan.md` (the reasoning).
3. **Feature F0a** builds the harness (David, in-session, no loop). **F0b** is the walking skeleton and the first loop run.
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

**"Today"** means today in **Europe/London**. It is computed once in Services from the injected `TimeProvider`, and the domain receives it as a `DateOnly`.

### Sell (MUST-1)

| ID | Rule | Agreed interpretation |
|---|---|---|
| SELL-1 | Policy reference must be unique | The client supplies the reference. It is trimmed and upper-cased, then must match `^[A-Z0-9-]{3,20}$` (else **400**). `abc-1` and `ABC-1` are the same policy, so a duplicate returns **409**. A unique DB index enforces it too |
| SELL-2 | Can only be sold up to 60 days in advance | Start date must be between today and today + 60, inclusive. **No backdating**: otherwise **422** |
| SELL-3 | Always 1 year in length | The client sends only the start date. End date = **start + 1 year − 1 day**, calculated, cover inclusive. **A 29 Feb start ends on 28 Feb** of the next year (29 Feb 2028 → 28 Feb 2029; 1 Mar 2027 → 29 Feb 2028), with its own named test |
| SELL-4 | 1 to 3 policyholders | Otherwise **422** |
| SELL-5 | All policyholders over 16 on the start date | **16 or older** on the start date (a 16th birthday on the start date passes), else **422**. A 29 Feb birthday needs no special case: a 16th birthday always falls in a leap year |
| SELL-6 | Payment at sale | The request carries the payment method. A Payment is created for the full policy amount, with a generated reference |
| SELL-7 | Amount greater than zero | *Added.* The policy amount must be greater than 0, else **422** (matching RENEW-5) |

### Retrieve (MUST-2)

| ID | Rule | Agreed interpretation |
|---|---|---|
| GET-1 | Retrieve a policy | By reference. Returns the policy, its terms, policyholders, property, payments, refunds and claims. **404** if not found. The policy-level `startDate`, `endDate` and `amount` describe the **current term**: the term in force today, or the latest term if today is outside them all. `terms[]` holds the history |
| — | Status | Derived from today, never stored: `Pending` (today before the first start), `Active`, `Expired` (today after the last end), or `Cancelled`, which overrides the others |

### Cancel (SHOULD-1)

| ID | Rule | Agreed interpretation |
|---|---|---|
| CANCEL-1a | Before the start date → full refund | — |
| CANCEL-1b | On or after the start date, within the 14-day cooling-off → full refund | Cooling-off runs to **start + 14 days, inclusive** (UK consumer-law convention: counted from the day after the start) |
| CANCEL-1c | After cooling-off → pro-rata for unused days | Refund = amount × unused days ÷ **actual days in the term** (365 or 366). The cancellation day counts as used |
| CANCEL-2 | Refund to the same method as the original payment | Each refund goes to the method of the payment it reverses |
| CANCEL-3 | Cancellation date | Optional, defaults to today; an empty body is allowed. **No past dates** (422) |
| CANCEL-4 | Cancel twice | **409**, giving the original cancellation date |
| CANCEL-5 | Cancelling a renewed policy | Ends the policy. The current term follows the normal rules. A future term is refunded in full (it is before its start date) |
| CANCEL-6 | Rounding | 2dp, `MidpointRounding.AwayFromZero`, applied in one place (.NET defaults to banker's rounding) |
| CANCEL-7 | Cancellation date within cover | *Added.* The date must be on or before the latest term's end, else **422**. With CANCEL-3, this blocks cancelling an expired policy |

### Renew (SHOULD-2)

| ID | Rule | Agreed interpretation |
|---|---|---|
| RENEW-1/2 | Renew within 30 days of the end date, not after it | Window: **end − 30 days to the end date, inclusive**, measured from the latest term's end. Otherwise **422** |
| RENEW-3a | Auto-renew on → payment created | The method comes from the request, defaulting to the current term's payment method |
| RENEW-3b | Auto-renew off → no payment | The term is created unpaid. Collecting that payment is out of scope. A request that sends a `paymentMethod` anyway gets **422** RENEW-3b, because the renewal is unpaid |
| RENEW-4 | What a renewal is | The policy has **terms**. Renewal appends the next term, starting the day after the current end date, under the same reference. *Why:* renewal happens while the current term is still running, so overwriting dates would break cancellations made in its final 30 days |
| RENEW-5 | Price | The client supplies the new term's amount, which must be greater than 0 (pricing belongs to another team), else **422** |
| RENEW-6 | A cancelled policy | Can't be renewed: **409** |

### Could-haves (all in scope)

| ID | Rule | Agreed interpretation |
|---|---|---|
| COULD-1 | Quote the cancellation refund without cancelling | A read-only version of the cancel calculation |
| COULD-2 | No refund if the policy has a claim | A claim means a **£0 refund**, overriding CANCEL-1a/1b. Claims are recorded with `POST /policies/{reference}/claims` (`claimDate` required, `description` optional, max 500). Any claim sets HasClaims, which is false at sale |
| COULD-2a | Claim date not in the future | Otherwise **422** |
| COULD-2b | Claim date within a term's cover | Otherwise **422** |
| COULD-2c | No claim on a cancelled policy | **409** |
| COULD-3 | No cheque for an auto-renewing policy's renewal | Rejected with **422**, including a Cheque *defaulted* under RENEW-3a. The `detail` asks for another method |

**No £0 refunds.** A Refund record is created only for an amount above £0 against a real payment, so a COULD-2 claim or an unpaid RENEW-3b term creates none. The cancellation response still lists every term, with `refundAmount` 0.00 and `ruleApplied` `COULD-2` or `UNPAID-TERM`.

### Data (appendix, plus our additions)

- **Policy:** reference, type, start date, end date, amount, HasClaims, AutoRenew. *Added:* terms, cancellation, derived status.
- **Policyholder:** first name, last name, date of birth.
- **Property:** address line 1 (required), address lines 2 and 3, postcode (required, max 8 characters).
- **Payment:** reference, type, amount.
- **Refund:** reference, type, amount.
- *Added:* **Term** (start, end, amount, payment) and **Claim** (claim date, description).

**Out of scope:** authentication, pricing, real payment processing, live deployment.

**Response standard:** every rule violation returns RFC 9457 problem details (which obsoletes RFC 7807) that name the broken rule in plain English: `type` = `/problems/{rule-id}` (lower-case), plus `ruleId`, `traceId` and `instance`.
- **Status codes:** **400** for shape; **422** for every numbered rule (SELL-2, SELL-4, SELL-5, SELL-7, RENEW-1/2, RENEW-3b, RENEW-5, CANCEL-3, CANCEL-7, COULD-2a, COULD-2b, COULD-3); **409** for state conflicts (SELL-1, CANCEL-4, RENEW-6, COULD-2c); **404** for GET-1.
- **All violations are reported.** A 422 lists every broken rule in `violations[]`. A 400, 404 or 409 short-circuits before the rules are evaluated.
- **Strict input.** Malformed JSON, wrong types, missing fields, unknown or integer enum values, an **unknown JSON field** or an **unknown query parameter** all return **400** with a field `errors` map.
- A 500 returns a generic `detail` and a `traceId`. The full details go to the logs only, and never a stack trace outside Development.

Routes, payloads and the full body shape are in `api-contract.spec.md`.

---

## 3. Delivery requirements

### How features are built

- **Outside-in TDD.** For each feature:
  1. **Red (tester):** write the feature's **full acceptance suite** (every success and failure row for its rules) as integration tests, and add the Postman requests. The tester may add `src/` member *signatures* whose bodies only `throw new NotImplementedException()`, so the red commit compiles and fails. A hook enforces this, and green replaces every throw.
  2. **Stub (builder):** a controller that returns a hard-coded response, so the endpoint can be **called in Postman before the layers behind it exist**.
  3. **David's pause 1:** the acceptance tests are red and the stub is callable. David calls it in Postman and reads the tests.
  4. **Green (builder), per layer** (domain → services → persistence → api), with unit-level TDD of each rule (red → green → refactor, one commit per step). Real behaviour replaces the stub.
  5. **David's pause 2:** HITL sign-off, with the generated `walkthrough.md` (each commit in order, one line each), the QA script and Postman.
- **Tests:** architecture tests (enforce layering), integration tests (`WebApplicationFactory`, SQLite in-memory, `FakeTimeProvider`), unit tests (every rule, every boundary; domain tests pass a `DateOnly today`, and `FakeTimeProvider` is used only at the service and integration levels), and a Postman collection with an environment.
- **Rule coverage:** every rule test carries `[Trait("Rule", "<rule-id>")]`, and a check proves every rule ID in this file has at least one test.
- **One feature at a time.** David must fully understand each feature before the next one starts.

### Feature loop

1. An **orchestrator**, the `/feature` skill in the main session, picks up the next node from the plan DAG. It dispatches the named subagents, runs the gates itself via Bash, and stops for David's two pauses.
2. The **git agent** creates the branch (`feature/<plan>/<node>`, or `bug/<plan>/<node>`) and its **worktree** at `.claude/worktrees/<plan>-<node>`, which the session enters with `EnterWorktree`. All agents for the feature work only inside that worktree. `main` and David's own checkout are never touched by agents.
3. **tester** (red) → **builder** (stub, green, refactor) → **reviewer** (rules and acceptance met, code quality, no slop) + **security** (OWASP API Top 10 2023 on the diff). Reviewer and security produce verdicts only.
4. **Failure route.** On a FAIL, the **investigator** diagnoses the root cause into `findings.md` → the tester writes a red test from the findings (skipped only if the finding is untestable, with a reason) → the builder fixes it → reviewer and security re-run. **Retry ladder:** attempts 1–2 take that route, attempt 3 escalates to `/plan-investigation`, attempt 4 takes the route again briefed by the investigation's accepted `amend-node`, and attempt 5 stops and asks David. A repeated finding escalates early. Only reviewer, security or gate FAILs count as attempts; David's steers (pause 1, pause 2, PR comments) take the same route without counting.
5. **Integration (git agent):**
   - rebase the feature branch onto the latest `origin/main`
   - run the full test suite inside the worktree (unit, integration and architecture)
   - commit the trace trails in one `chore(trace)` commit
   - only then push and raise the PR
6. A **QA manual test script** is produced for the feature (`docs/qa/`). David runs the API **from the worktree** (port 5080, its own SQLite file, the `.slnx` opened in VS 2026), steps through the script in Postman, runs the tests, and signs off. **David is the acceptance gate.**
7. **David merges** the PR (merge commits only). The git agent removes the worktree and deletes the branch.

**PR contract.** The body is generated from the trace (rules, test counts, a link to `walkthrough.md`), plus one paragraph David writes. The git agent posts the reviewer verdict, the security verdict and the rebase receipt as PR comments. David's comments on an open PR are read with `gh pr view --comments`; each becomes a finding that goes through the failure route on the same branch, and the git agent replies with the fixing commit's SHA. **Only comments by `dpmorgandev` are acted on** (a prompt-injection guard). A comment on a merged PR becomes a `/plan-bug`.

**No separate integration agent.** Features run one at a time, so a feature branch can only fall behind `main` by changes David has already merged. `main` is the integration branch (no develop branch), and the git agent's rebase-and-full-suite step, followed by David's HITL, covers integration.

**The agents (six):** tester, builder, reviewer, security, investigator (read-only, root cause → `findings.md`) and git (branch, worktree, rebase, push, PR and all PR comments). No agent may spawn agents. Agents never read the specs: they preload contract-only skills. All skills are our own, with no third-party plugins. Roster detail is in `agents-skills.spec.md`.

### Planning model

Every plan that changes code is a **DAG** in one shared format. The orchestrator only ever executes DAGs.

```text
/plan-dag            the initial full build     ─┐
/plan-feature        a change to a live system  ─┼─► DAG (same schema) ─► orchestrator ─► loop ─► HITL ─► PR
/plan-bug            an escaped defect          ─┘
/plan-investigation  read-only reasoning ─► findings + recommendations ─► David approves ─► /plan-feature, /plan-bug or amend-node
```

- **`/plan-dag`:** the build plan, F0a to F7. It runs once, and closes when the system exists.
- **`/plan-feature`:** a change to the live system. It produces a small DAG (usually one feature node with tasks) that goes through the same loop, HITL and PR. If the change introduces a new rule, a **new rule ID is added to this file first**, so every change still traces back to a requirement.
- **`/plan-bug`:** an escaped defect, meaning one found *after* merge. Its **first node is always a failing test that reproduces the bug.** It links to the broken rule ID and to the plan and node that introduced the faulty code (found through the activity trail). A defect David finds during HITL *before* merge isn't a plan-bug: it goes back into that feature's loop.
- **`/plan-investigation`:** never changes code and never produces a DAG. Its output is findings, evidence, and **recommendations** of three kinds, each with a one-line reason: `feature`, `bug`, or `amend-node` (a revised approach for the parent node's attempt 4). David accepts or rejects each, which keeps "decide what to do" apart from "do it". It is also the **attempt-3 escalation** in the retry ladder (see *Feature loop*, step 4). A standalone investigation is committed on a `chore/inv-NNN` branch via PR.
- **Plan format:** two files per plan in `docs/plans/active/`. `<plan-id>.dag.yaml` is the only state: plan status and approval, nodes, and each node's explicit ordered tasks (per layer, each naming its rule ids), which the orchestrator loops through. `<plan-id>.plan.md` holds the reasoning (why, approach, rejected alternatives). One node per feature. Node and task status live in the YAML and change only through feature-branch commits; attempts are counted per node. Plan IDs: `build`, `feat-NNN`, `bug-NNN`, `inv-NNN`, with no slugs.
- **Archive:** a git-agent procedure, not a skill (there is no `/plan-archive`). After David merges, the git agent moves the plan to `docs/plans/archive/` on a `chore/archive-<id>` branch, with a completion record built from git facts. A lesson is required at attempt 3 or more, on escalation, or when a node is cut, and is appended to `docs/lessons.md`.
- **Prior work:** before a new plan is written, `docs/plans/` is searched. This replaces agentflow's separate `check-existing-work`, `capture-lesson` and `consult-lessons` skills, which aren't worth separate skills at this volume.
- **When the skills get built:** `/plan-dag` is built for the build. `/plan-feature`, `/plan-bug` and `/plan-investigation` are **specified now but built only when first needed**: the first escaped defect, change request or stuck loop.
- *Prior art:* agentflow_phase3's `plan-feature` (120 lines), `plan-bug` (114), `plan-investigation` (139) and `plan-archive` (152) skills.

### Rules

- The builder cannot edit tests.
- The tester cannot edit `src/`, except for throwing-stub signatures in a red commit.
- The reviewer, security and investigator agents are read-only.
- No PR unless the latest reviewer and security verdicts are PASS for the current HEAD.
- No push to `main`.
- Every push, PR and PR comment is run by the git agent and needs David's approval (an `ask` rule). Merging and making the repo public are David's alone.
- **Commits on `main`** (docs, specs, harness) are allowed until F0b starts. From F0b, a hook denies `git commit` on `main`.
- **The harness lock:** from the end of F0a, no subagent may write the harness (`.claude/` agents, skills, hooks, specs, workflows and settings, `.githooks/`, `.git/` and `CLAUDE.md`), including the copies inside worktrees. The main session may, with an `ask` prompt each time.
- **Guards hold in bypass mode:** deny rules, ask rules, hooks and git hooks all still apply, so bypass mode stays allowed.
- **No branch protection while the repo is private:** GitHub Free returns 403 for branch protection and rulesets, so the local guards carry the load until submission.

The mapping of each rule to its hook or permission is in `guardrails.spec.md`.

### Pipeline and environments

- **No CI.** The task doesn't require it. Local git hooks: **pre-commit builds**, so red commits stay committable; **pre-push builds, runs the tests and generates the idempotent migration script**, which must succeed.
- **Postman is in no git hook** and runs via the Postman CLI (Newman can't run the v3 collection format). David steps through it manually at both pauses. Before pause 2, the orchestrator also runs the feature's real-time-runnable Postman folder against the worktree API (port 5080) and records pass/fail in `loop.jsonl`, F0a confirmed that the CLI runs a local collection without login, with a non-zero exit on a failing assertion (`docs/trace/build/F0a/tools.md`).
- **Infrastructure as code and deployment are evidence, not deployed.**
  - Bicep for **dev, test and prod**, plus an Azure DevOps multi-stage pipeline with an approval gate before prod.
  - Both are validated locally and never connected to Azure; `az` is never run.
  - This work comes late, with a fixed time budget, and is cuttable (see §4).

### Repository layout

```text
CLAUDE.md          agent entry point
.claude/           the harness: specs/, agents/, skills/, hooks/, workflows/, settings.json; worktrees/ (feature worktrees)
.githooks/         pre-commit, pre-push
docs/              project records, kept separate from the harness:
                   requirements.md (this file), lessons.md,
                   plans/active/ + plans/archive/ (<plan-id>.plan.md + .dag.yaml), context/ (session snapshot chain),
                   trace/<plan-id>/<node-id>/ (trails), qa/ (signed-off QA scripts)
                   (no decisions.md: decisions live in the specs)
src/               Uinsure.Policies.Api, .Services, .Persistence, .Domain
src/web/           reserved for a future frontend (its own brief, mockups and plan later; not built here)
tests/             Domain.Tests, Services.Tests (if needed), Api.Tests (integration), Architecture.Tests, postman/
infrastructure/    Bicep + Azure DevOps pipeline (evidence)
```

Nothing in `src/` or `tests/` may reference `.claude/`. Project responsibilities and references are in `architecture.spec.md`.

### Stack

- .NET 10 LTS (SDK 10.0.402 is installed)
- Visual Studio 2026
- ASP.NET Core Web API with controllers
- EF Core: SQLite locally and in tests, Azure SQL in the environments (one SQL Server migration set)
- xUnit v3 on Microsoft.Testing.Platform, with plain `Assert` (FluentAssertions is commercial from v8)
- NetArchTest.eNhancedEdition for the architecture tests; BannedApiAnalyzers (`DateTime.Now`, `UtcNow`, `Today` banned)
- OpenAPI 3.1 with Scalar, in Development only
- Structured logging to the console locally, and to App Insights via OpenTelemetry in the environments
- Hosting: App Service Linux, with a managed identity to Entra-only Azure SQL. **No Key Vault** (there are no secrets): considered and rejected

---

## 4. Analysis: what it must produce

Each spec has four sections: **Research → Options → Decision → Contract**. Decisions are recorded inside the spec. Agents never read the specs; they act on the Contracts, distilled into contract-only skills.

**Status (2026-10-17): every spec's Decision is DECIDED by David, and every Contract is final (`6abcf9c`).**

| # | Spec (`.claude/specs/`) | Must settle |
|---|---|---|
| 0 | `context.spec.md` | **Written first.** Keeps work continuous across sessions and days. <ul><li>**`/save-context`:** a snapshot of where we are (feature, step, loop count, decisions made, open questions, next action), chained backwards with a `previous-context` link</li><li>**`/load-context-chain`:** walk the chain back to the last checkpoint, combine it into one "where we are" brief, and get David's confirmation before resuming</li><li>**`/orient` (this project only: it never reads outside the repo, because the repo is public):** pull together as much context as possible before work starts: <code>CLAUDE.md</code>, this file, the specs, plan DAG status, the latest context chain, <code>git status</code>, open worktrees and PRs, and the trace for the feature in flight</li><li>**Mechanical leg:** a <code>SessionStart</code> hook that triggers or prompts <code>/orient</code>. In agentkinOS, <code>/orient</code> was prose-only and no SessionStart hook was registered</li><li>Snapshots live in <code>docs/context/</code> and are committed, as project records rather than harness. The repo goes public, so a snapshot must never carry anything private</li><li>Snapshot size cap and checkpoint cadence</li><li>Use the real clock for timestamps. An invented timestamp once broke the chain ordering in agentkinOS</li></ul>*Prior art to port (the procedure, not the machinery):* agentkinOS's `save-context` skill (156 lines: 500-line cap, a checkpoint every 5 snapshots, `contexts/checkpoints.md`), `load-context-chain` (74 lines, read-only, human confirms before resuming) and `orient` (99 lines). These depend on OS packages (`@substrate/orientation`, the native context receipt), so they're ported as plain procedures. |
| 1 | `architecture.spec.md` | <ul><li>Project structure and layering</li><li>Domain model</li><li>Where rules live, and services</li><li>Persistence and EF provider switching</li><li>Validation</li><li>Error contract</li><li>Time and money handling</li><li>Logging</li><li>OpenAPI and its UI</li><li>**Considered and rejected:** event-driven / domain events, caching, CQRS / MediatR, auth</li><li>Architecture-test library and its rules</li></ul> |
| 2 | `api-contract.spec.md` | <ul><li>Routes</li><li>Request and response DTOs</li><li>Status code per rule ID</li><li>Error body</li></ul> |
| 3 | `planning.spec.md` | <ul><li>The shared plan format: the reasoning, plus a YAML DAG of nodes → tasks → rule IDs → dependencies</li><li>Plan IDs, and the lifecycle from active to archive</li><li>Definition of done per node</li><li>What `/plan-dag` asks David</li><li>The four `plan-*` skills and their inputs and outputs</li><li>Investigation → recommended-plan handoff</li><li>Retry ladder</li><li>Lessons</li></ul> |
| 4 | `testing.spec.md` | <ul><li>What each test type proves</li><li>Naming and builders</li><li>`FakeTimeProvider`</li><li>Mapping from rule ID to tests</li><li>Postman structure</li><li>QA script template and sign-off</li><li>Postman in hooks or not</li></ul> |
| 5 | `agents-skills.spec.md` | <ul><li>Roster: role, tools allowed/denied, model, preloaded skills, and the never-dos of each agent</li><li>The skills to build, and which agents use them</li><li>Why planning and orchestration aren't agents</li></ul> |
| 6 | `orchestration.spec.md` | <ul><li>The feature loop</li><li>Workflow vs. skill</li><li>Verdict JSON schema and bug report</li><li>Loop cap and escalation</li><li>Trace per feature</li><li>HITL step</li><li>Branch, commit and PR conventions</li></ul> |
| 6b | `traceability.spec.md` | Three trails, keyed by plan and node in `docs/trace/<plan-id>/<node-id>/`, forming one chain: rule ID → plan → node → tests + code → verdicts → QA sign-off → PR. <ul><li>**Requirement trail:** rule ID → tests → code → QA step → sign-off</li><li>**Cross-links:** investigation → the plans it recommended; bug → the broken rule and the plan/node that introduced it</li><li>**Activity trail:** which agent touched which files, in which loop, and in which commit. Candidate mechanisms: a `PostToolUse` hook logging agent and file, and commit trailers such as `Agent: builder`</li><li>**Verdict trail:** each reviewer and security verdict, the bugs raised, and the loops taken</li></ul> Also settles which trails are generated mechanically and which are written by agents. A self-reported trail isn't a receipt. |
| 7 | `guardrails.spec.md` | <ul><li>Each rule in §3 mapped to the hook or permission that enforces it</li><li>Git hooks</li><li>Secrets protection</li></ul> |
| 8 | `infrastructure.spec.md` | <ul><li>Azure resources (decided: App Service Linux, Azure SQL, App Insights; Key Vault rejected)</li><li>Bicep modules</li><li>`.bicepparam` file per environment</li><li>Naming</li><li>Managed identity</li><li>Sizing</li></ul> |
| 9 | `deployment.spec.md` | <ul><li>Azure DevOps stages and environments</li><li>Approvals</li><li>Service connections</li><li>Bicep what-if and deploy</li><li>EF migrations</li><li>Smoke tests</li></ul> |

Analysis also produces the **`/plan-dag` skill**. The feature list for the build plan:
- **F0a** harness (owner David, built in-session, no loop)
- **F0b** walking skeleton (the first loop run)
- **F1** Sell, plus a bare GET (SELL-1..7; every Sell test is a POST → GET round trip)
- **F2** Retrieve (GET-1, status, the current-term view, the full response shape)
- **F3** Cancel, with the quote and claims (CANCEL-1..4, 6, 7, COULD-1, COULD-2, COULD-2a–c)
- **F4** Renew, with the cheque rule and CANCEL-5 (RENEW-1..6, COULD-3)
- **F5** infrastructure and deployment evidence (cuttable, 3h budget, depends on F0b)
- **F6** README (must; built from a checklist of every "README states…" in the specs)
- **F7** pitch deck (cuttable; generated from the DAG and the trace trails)

**Cut order:** F7, then F5.

**Not in this plan:** the React frontend. It gets its own brief, mockups and plan DAG later; only `src/web/` is reserved.

**Order:** context → architecture → api-contract → planning → testing → agents-skills → orchestration → traceability → guardrails → infrastructure → deployment.

**Guard against over-building:** every spec, agent and skill must be used by F0a, F0b or F1. Anything that isn't is cut. David's agentkinOS audit found 46–82% of the declared machinery was never wired up.

---

## 5. Research inputs for the analysis

Findings checked on 2026-10-17:

- **Subagents.** They can dispatch subagents (3 levels by default). Agent files can preload `skills:` and set `tools` / `disallowedTools`. Source: [sub-agents](https://code.claude.com/docs/en/sub-agents).
- **Workflows.** `agent(prompt, {schema})` returns validated JSON, and there's **no user input during a run**. That fits one workflow run per feature, with HITL in the session afterwards. Workflows need explicit opt-in. A plain subagent's output can't be forced into a schema. Source: [workflows](https://code.claude.com/docs/en/workflows).
- **Hooks.** `SubagentStop` can block. Nothing fires on PR creation, so a PR gate needs a `PreToolUse` deny plus a permission deny. Source: [hooks](https://code.claude.com/docs/en/hooks).
- **Headless.** Headless `claude -p` and the Agent SDK use an API key. David's LESSONS record the headless orchestrator as ToS-barred on Max, so orchestration stays interactive.
- **CLAUDE.md imports.** `CLAUDE.md` can `@import` specs, and Claude Code ignores folders it doesn't recognise under `.claude/`. Source: [memory](https://code.claude.com/docs/en/memory).
- **Postman.** Newman can't run the v3 collection format (and is unmaintained), so use the Postman CLI. Source: [Postman docs](https://learning.postman.com/docs/collections/using-newman-cli).
- **OWASP.** The API Security Top 10 2023 is the latest edition found. Confirm on owasp.org. Auth-related items are out of scope here and should be stated, not skipped. Source: [Nordic APIs](https://nordicapis.com/a-look-at-the-owasp-api-security-top-10-2023/).
- **Architecture tests.** NetArchTest is lighter. ArchUnitNET adds cycle detection. .NET 10 support is unconfirmed. Source: [comparison](https://www.dotnet-guide.com/articles/software-architecture/netarchtest-archunitnet-roslyn-analyzers/).
- **Lessons from David's own harnesses.** Sources: agentflow_phase3 and agentkinOS.
  - Worth borrowing: the fresh-context evaluator (a producer never grades its own work), deterministic gates outside the agent, and the anti-oracle-editing checks.
  - Avoid **commit-on-stop**: it would pollute a history that has to show TDD.
- **Proven on the deleted first scaffold.**
  - A Stop hook that refuses to finish while `dotnet build` fails (warnings as errors) worked.
  - `.gitattributes` must keep `*.sh` as LF.

---

## 6. Open items

Closed during analysis (2026-10-17):

1. ~~Postman in the pre-push hook.~~ **No.** Postman is manual only, via the Postman CLI.
2. ~~GitHub remote for PRs.~~ **A private repo under `dpmorgandev`**, made public at submission by David.
3. ~~Workflow vs. skill for orchestration.~~ **The `/feature` skill** in the main session, dispatching named subagents.
4. ~~Bicep CLI / `az`.~~ **The Bicep CLI, installed via winget**, validates locally. `az` is never run. Installed in F0a: Bicep 0.48.1 and Postman CLI 1.75.0 (`docs/trace/build/F0a/tools.md`).
5. ~~Worktrees.~~ **`EnterWorktree`** into `.claude/worktrees/<plan>-<node>`, shared by every agent on the node; HITL on port 5080 with a SQLite file per worktree and the `.slnx` in VS 2026. Inheritance is confirmed by a spike in F0a.
6. ~~Agent identity in hooks.~~ **`agent_type`** in hook input (the agent file's `name:`) drives the per-agent path guard and the activity trail. The F0a spike refined it: `agent_type` is sometimes empty on a subagent event, so a subagent is detected by `agent_id` and an empty role fails closed.
7. ~~Integration.~~ **The git agent's rebase plus the full suite** before the PR, with `main` as the integration branch. No separate integration agent.

Closed after analysis (2026-10-17). Nothing is open:

1. ~~F0a spikes.~~ **Done (t3–t5, 2026-10-17)**, with receipts in `docs/trace/build/F0a/spikes.md`.
   - Subagents inherit the `EnterWorktree` cwd, and hook `cwd` is the worktree.
   - `agent_type` is the agent's name.
   - SubagentStop exit 2 re-prompts.
   - Subagents load the project and user-level `CLAUDE.md`.
   - Ask rules prompt in bypass mode.

   One finding changes the design: the main checkout's settings, hooks, agents and `CLAUDE.md` govern inside a worktree, fixed at session start. Its corrections to orchestration and guardrails are recorded there as open decisions for David.
2. ~~Contracts.~~ **Resolved (2026-10-17):** every spec's Contract is final, including the architecture Contract for the four projects (`6abcf9c`).
3. ~~App Insights wiring.~~ **Resolved:** F0b owns the app-side OpenTelemetry setup; F5 only supplies the connection string (`planning.spec.md`).
4. ~~OWASP for the harness.~~ **Resolved (David, 2026-10-17):** mapped to the OWASP LLM Top 10 2026 plus the OWASP Top 10 for Agentic Applications for 2026, both verified from OWASP's own PDFs (`guardrails.spec.md`).
