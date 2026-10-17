# agents-skills.spec.md

Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Settles: requirements.md §4 row 5 (roster, skills, why planning/orchestration aren't agents)

## Dependencies

**Upstream (researched in parallel, NOT decided — assumptions stated):**

| Spec | What this spec assumes until it's decided |
|---|---|
| architecture | `src/` holds the projects (Api, Domain/Application, Infrastructure); `tests/` holds Unit, Integration, Architecture projects + `tests/postman/`. Rules live in the domain, not in controllers |
| api-contract | Routes under `/policies`, ProblemDetails names the rule ID. The reviewer checks status codes against this spec's per-rule table |
| testing | Tester owns `tests/**` incl. Postman collection + environment. QA script template lives in testing.spec; scripts land in `docs/qa/` |
| orchestration | Main-session orchestrator, one feature at a time; each agent commits its own TDD step (`test:` red by tester, `feat:`/`refactor:` by builder). Verdict JSON schema is owned by orchestration — this spec only drafts the *fields each agent emits* |
| guardrails | Owns the hook *scripts*. This spec states *which* boundaries need mechanical enforcement and the mechanism that's feasible |
| traceability | Activity trail is hook-generated from `agent_type` (verified feasible below) |

**Downstream:** orchestration (dispatch order, verdict schema), guardrails (hook per boundary), traceability (agent identity in trails), planning (plan-* skills).

## Research

### R1. Frontmatter capabilities (verified, code.claude.com/docs/en/sub-agents, 2026-10-17)

- Fields: `name`, `description`, `tools` (allowlist; omitted = inherit all), `disallowedTools` (applied first), `model` (`sonnet`|`opus`|`haiku`|`fable`|full ID|`inherit`), `permissionMode`, `maxTurns`, `skills`, `mcpServers`, `hooks`, `memory`, `background`, `effort`, `isolation`, `color`, `omitClaudeMd`, … Unknown fields are **silently ignored** (agentflow's `mcp_servers:` snake_case key was probably a no-op — the documented key is `mcpServers`).
- **`tools` has no path scoping.** The docs give no `Edit(src/**)` syntax for agent files; "`disallowedTools: Bash(git push *)` still removes the whole tool." For path restrictions the docs point to a `PreToolUse` hook that exits 2.
- **`skills:`** — "The full content of each listed skill is injected into the subagent's context at startup." Skills with `disable-model-invocation: true` can't be preloaded. Without `skills:`, an agent can still invoke skills via the Skill tool unless `Skill` is removed from `tools`.
- Subagents get CLAUDE.md + preloaded skills, **not** conversation history (fresh context — the evaluator property comes for free).
- Model precedence: per-invocation `model` → frontmatter → `CLAUDE_CODE_SUBAGENT_MODEL` → session model.
- Nesting: 3 levels default; omit `Agent` from `tools` to prevent an agent spawning others. Recommend all five omit it.
- Permissions: if the main session is in `bypassPermissions`/`acceptEdits`/auto, that mode overrides the agent's `permissionMode`. So **`permissionMode` is not a boundary**; don't rely on it.

### R2. Hooks see which agent made the call (verified, /hooks)

> "When a subagent calls a tool, tool events such as `PreToolUse` and `PostToolUse` fire the same configured hooks as in the main conversation, and the input carries the `agent_id` and `agent_type` … that identify the subagent."

`agent_type` = the agent's name (e.g. `builder`). Absent for main-thread calls. Settings-file hooks apply inside subagents. Deny = exit 2 (stderr to Claude; overrides even an "allow" JSON) or `permissionDecision: "deny"`. Frontmatter `hooks:` also work (scoped to that agent's lifetime; project ones need workspace trust; `Stop` → `SubagentStop`). `cwd` in hook input follows the worktree; `${CLAUDE_PROJECT_DIR}` does **not** (stays at the launch checkout) — hook scripts must resolve paths against `cwd`.

**Answers requirements.md §6 item 6: yes — the activity trail can be mechanical.**

### R3. How "builder cannot edit tests/" is enforced — mechanically

Three layers, weakest to strongest:

1. **Prose** in the agent body ("never edit `tests/`"). Not a boundary. agentflow's `reviewer.md`/`security.md` say "read-only" in prose but grant `Write, Edit` + `permissionMode: acceptEdits` — the prose was the only guard. Lesson: never declare a role read-only while granting write tools.
2. **Tool removal** (`tools`/`disallowedTools`). Real and coarse: right for reviewer/security (no `Write`/`Edit`/`NotebookEdit` at all). Can't express "Edit but not under tests/".
3. **`PreToolUse` hook keyed on `agent_type`** — one settings-level script (`.claude/hooks/role-boundary.*`), matcher `Edit|Write|NotebookEdit|Bash|PowerShell`:
   - `builder` + target path under `tests/` → exit 2
   - `tester` + target under `src/` → exit 2
   - `reviewer`/`security` + any write tool or mutating shell → exit 2 (belt and braces over layer 2)
   - any agent + `git push`/`gh pr merge`/`git merge` into main → exit 2 (only `git` may `gh pr create`)
4. **Gap: shell writes.** A hook sees `Edit.file_path` reliably but a Bash `echo > tests/x.cs` or `sed -i` only as command text. Worktree isolation checks also don't track shell writes (docs say so explicitly). So add a **post-hoc diff gate**: `SubagentStop` hook (can block: exit 2 "prevents the subagent from stopping") runs `git diff --name-only <start-sha>..HEAD` + `git status --porcelain` in `cwd` and rejects if a builder run touched `tests/**` or a tester run touched `src/**`. This is the deterministic leg — it checks the *result*, not the self-report. Needs the start SHA recorded at `SubagentStart` (keyed by `agent_id`).

Recommend layers 2+3+4. Layer 3 alone is defeatable through Bash; layer 4 alone gives late feedback. Script content belongs to guardrails.spec.

### R4. Shared worktree for tester + builder (requirements.md §6 item 5)

- `isolation: worktree` = a **temporary, fresh** worktree per agent, branched from the default branch — wrong here (and auto-deleted if unchanged). Don't set it on any agent.
- Agent tool / workflow `agent()` have **no `cwd` option** (workflow `agent()` opts: `label, phase, schema, model, effort, isolation, agentType` — from the bundled workflow-authoring reference). A subagent "starts in the main conversation's current working directory."
- Therefore: **the main session moves into the feature worktree**, and every agent it spawns inherits that cwd. Mechanism: `EnterWorktree` with the target path. The docs: entering a path **outside** `<repo>/.claude/worktrees/` prompts for approval every time and "an `EnterWorktree` permission rule or 'don't ask again' doesn't suppress this prompt". Inside `.claude/worktrees/` it doesn't.
- While the session is in a worktree, Claude Code blocks edits/commands/git redirects that target the main checkout — **for the session and every subagent it spawns**. That's the "agents never touch `main` or David's checkout" rule enforced for free.
- Worktrees made by `git worktree add` (no Claude marker) are never removed by the periodic sweep — good; the git agent owns removal.
- **Implication for requirements.md:** worktree path `../wt/F1-sell` → **`.claude/worktrees/F1-sell`** (branch still `feature/F1-sell`, created by git agent with `git worktree add`). Add `.claude/worktrees/` to `.gitignore`. Alternative (keep `../wt/`): David approves one prompt per feature — acceptable, honest HITL, but noisier.
- Secondary: an agent can also be told absolute paths, but Bash cwd still resolves to the session cwd and `cd` doesn't persist — fragile. Not recommended.
- **Unverified:** whether workflow `agent()` subagents inherit a worktree the session entered *after* launch. Docs say subagents do; workflows run "in an isolated environment" — assume yes, **spike in F0**.
- Project `.claude/agents|skills` read through from the main checkout when the worktree lacks them — but our harness is tracked, so the worktree carries its own copy *at the branch's version*. Harness edits made on `main` mid-feature don't reach an in-flight worktree until rebase. Fine (one feature at a time) — note it.

### R5. Prior art lessons (agentflow_phase3, agentkinOS)

| Lesson | Source | Applied here |
|---|---|---|
| Fresh-context evaluator: "you did not see how it was built and you should not trust the builder's own assessment"; verdict first line `PASS`/`NEEDS_WORK` for grep | `agentflow/.claude/agents/evaluator.md` (4.7 KB, tools `Read, Glob, Grep, Bash`, sonnet) | reviewer + security mirror this; verdict is structured JSON (orchestration) with a greppable first token |
| Self-reports lie: "validated off the mechanism + agent self-reports; the actual pixels were still 3/10" (#screenshot-not-self-report) | agentflow LESSONS | reviewer verifies by **running** `dotnet test` + reading the diff, never from the builder/tester summary. Trails are hook-generated |
| Judge out-discerns generator ("default posture is skeptical; I affirm only what survives an honest attempt to refute"; `model: opus`) | `agentkinOS/.claude/agents/forge-verifier.md` | reviewer on opus; prompt-to-refute posture |
| Lane discipline: tester "write test files only… hard constraint" — but enforced only in prose (bug-024 shows it was breached) | `agentflow/tester.md` | made mechanical (R3) |
| Read-only roles granted Write/Edit + acceptEdits | `agentflow/reviewer.md`, `security.md` | tools removed, not described |
| Bloat: tester.md 24 KB, ui-designer 26 KB, 26 agents; requirements.md cites 46–82% unwired machinery | agentflow agents dir | 5 agents, target ≤ ~120 lines each; skills carry the knowledge |
| git-agent was the *merge* owner (`close-feature` merges to main) | `agentflow/git-agent.md` | **inverted**: our git agent never merges; David merges |
| Anti-oracle editing (producer can't edit its grader) | requirements.md §5 | builder can't touch tests; tester can't touch src; reviewer checks the red commit preceded green and no test assertion was weakened in a later commit (`git log -p tests/`) |

### R6. OWASP API Security Top 10 — latest edition

api-security.owasp.org (fetched 2026-10-17) lists **2023** as the newest; no 2025/2026 edition or RC found. Mapping to this API:

| ID | Item | Here | Check (.NET specific) |
|---|---|---|---|
| API1:2023 | Broken Object Level Authorization | **Out (stated)** — no auth, no tenancy; reference is the only key. Note: references are client-chosen and guessable — say so in README | — |
| API2:2023 | Broken Authentication | **Out (stated)** — auth out of scope | — |
| API3:2023 | Broken Object Property Level Authz (mass assignment + excessive exposure) | **In** | Request DTOs separate from entities (no binding to EF entities); client can't set `EndDate`, `HasClaims`, payment/refund refs, `Cancellation`; response DTOs don't leak internal IDs/rowversion |
| API4:2023 | Unrestricted Resource Consumption | **In (light)** | Kestrel `MaxRequestBodySize` sane; bounded collections (1–3 policyholders enforced *before* processing); string max lengths (postcode 8) on DTO + EF column; no unbounded list endpoint. Rate limiting: note-only (no deploy) |
| API5:2023 | Broken Function Level Authorization | **Out (stated)** — no roles; note that `POST /claims` would be an admin function in real life | — |
| API6:2023 | Unrestricted Access to Sensitive Business Flows | **In — the interesting one** | Refund abuse: cancel-twice → 409 (CANCEL-4); renew-then-cancel can't refund more than was paid; claim recorded → £0; refund total ≤ sum of payments per term; quote endpoint is read-only (no state change); concurrent double-cancel (rowversion/unique constraint, or stated as accepted risk) |
| API7:2023 | SSRF | **Out** — no outbound URL fetching. State it | grep for `HttpClient` in src = none |
| API8:2023 | Security Misconfiguration | **In** | ProblemDetails without stack traces/exception messages outside Development (`UseExceptionHandler`, no `UseDeveloperExceptionPage` in prod path); no connection strings or secrets committed; HTTPS redirection; OpenAPI UI Development-only (or deliberately on — decided in architecture); HSTS |
| API9:2023 | Improper Inventory Management | **In (light)** | OpenAPI doc matches actual routes; no leftover stub/hard-coded endpoints after outside-in step 2 |
| API10:2023 | Unsafe Consumption of APIs | **Out** — no upstream APIs (pricing is client-supplied, RENEW-5). State it | — |

Plus non-Top-10 .NET checks: EF parameterisation (no `FromSqlRaw`/`ExecuteSqlRaw` with interpolation — `FromSqlInterpolated` or LINQ only); money as `decimal` (CANCEL-6); vulnerable packages (`dotnet list package --vulnerable`); no `.env`/secrets in the diff.

### R7. Community skills — reuse vs author

| Source | What | Trust/quality concern |
|---|---|---|
| `dotnet/skills` (github, MIT, .NET team org, "curated set of portable skills") — plugins `dotnet-aspnetcore`, `dotnet-data`, `dotnet-test` | Most credible | Broad, not scoped to our layering or rules; content not audited here; plugin install adds machinery we didn't write and can't defend line by line; may favour MSTest/Microsoft.Testing.Platform |
| `Aaronontheweb/dotnet-skills` (~30 skills, 5 agents; ~967★ third-party count) | Respected author | Akka/Aspire-heavy; agents would compete with our roster |
| `codewithmukesh/dotnet-claude-kit` (47 skills, 10 agents, .NET 10) | Large | Breadth = slop risk; unknown review |
| `github/awesome-copilot` dotnet-best-practices, `novotnyllc/dotnet-artisan` | Generic | Auto-loading foundational skill = context we don't control |

Constraints from requirements.md: "every line must be something David can explain", "no AI slop", over-building guard. A third-party skill injected into the builder's context is unreviewed prompt text steering the code. Also: skills are executable instructions — a supply-chain surface. **Recommend: author our own, small, scoped skills; read `dotnet/skills` `dotnet-aspnetcore`/`dotnet-test` as reference while authoring, cite it, copy nothing unreviewed. Don't install plugins.**

Stale note: this session lists project skills `decide` and `tdd` "from Uinsure.Policies/.claude/skills", but no `.claude/` existed in the repo before this spec — leftovers from the deleted first scaffold (cached listing). `tdd`'s idea (one rule, red/green/refactor, commit per step) is now the orchestrator's job; `decide` overlaps spec Decision sections. Recommend neither is rebuilt.

### R8. Models (cost vs quality)

David runs on Max: tokens draw on usage limits, not a bill — the constraint is limit burn across ~2 days, not £. Per-feature dispatch count is high: unit TDD is per rule, so F1 (6 rules) ≈ 6×(tester+builder) + refactors + reviewer + security + git ≈ 15–20 dispatches; ×retries.

- **Builder** works against fixed failing tests + skill: constrained → `sonnet`.
- **Tester** writes the oracle; a missed boundary is invisible downstream → quality matters most of the producers → `opus` (or `inherit`).
- **Reviewer** must out-discern both producers (forge-verifier lesson) → `opus`.
- **Security**: checklist-driven against R6 table → `sonnet`; escalate to opus only if it misses planted issues in the F0 spike.
- **Git**: mechanical commands → `haiku`, `effort: low`.
- `fable` is an accepted alias; I have not verified its tier/cost — don't pick it until checked (claude-api reference).

## Options

**O1. Roster size**
- **A. Five agents as briefed** (tester, builder, reviewer, security, git).
- B. Six: add `qa-writer` for the manual QA script. Rejected: tester already holds the rule→test mapping; a separate agent is another context to brief and another file nobody wires (over-building guard).
- C. Four: fold security into reviewer. Saves a dispatch; loses the independent lens and the visible OWASP artefact the role spec rewards. Rejected unless limits bite.
- D. Split reviewer into requirements-reviewer + code-quality/slop-reviewer. Rejected for now: one fresh-context judge with two checklists is enough at this volume.

**O2. QA script author:** A. tester (after reviewer+security PASS, paths `docs/qa/**`) · B. main-session `/qa-script` skill · C. reviewer (needs write → breaks read-only). **Recommend A**; reviewer checks the script covers every rule ID of the node on the *next* gate, or orchestrator does a deterministic rule-ID coverage check.

**O3. Boundary enforcement:** A. prose only · B. tools only · **C. tools + agent_type PreToolUse hook + SubagentStop diff gate** (R3).

**O4. Skill sourcing:** A. install `dotnet/skills` plugins · **B. author, referencing dotnet/skills** · C. no skills, all in agent bodies (bloats agents, can't share between tester/builder).

**O5. Worktree entry:** **A. worktrees under `.claude/worktrees/<feature>`, session `EnterWorktree`** · B. keep `../wt/`, accept one approval prompt per feature · C. absolute paths passed in prompts (fragile, R4).

## Recommendation

O1-A, O2-A, O3-C, O4-B, O5-A. Planning and orchestration stay in the main session because: (1) workflows/subagents take **no user input mid-run**, and `/plan-dag` must ask David questions; (2) HITL sign-off sits between features; (3) only the main session can `EnterWorktree` into the feature worktree so agents inherit its cwd; (4) headless orchestration is ToS-barred on Max; (5) an orchestrating subagent would itself be a self-reporting layer between David and the verdicts.

## Decision — DECIDED 2026-10-17 (David)

- [x] **D1 → six agents:** tester, builder, reviewer, security, **investigator** (new: orchestration F, read-only, root cause → `findings.md`) and git. There's no qa-writer, no integration agent and no separate slop-reviewer.
- [x] **D2:** the tester writes the QA script into `docs/qa/` after PASS. The tester also adds the throwing signature stubs for red commits (testing O9).
- [x] **D3:** the reviewer owns code quality and slop. Its checklist includes SOLID as applied at this size (SRP, plus DI at real seams only) and the **rule of three** for extracting duplication.
- [x] **D4 models:** tester opus · builder sonnet · reviewer opus · security sonnet · **investigator opus** · git haiku.
- [x] **D5–D10:** accepted as recommended, with D7 amended to `.claude/worktrees/<plan>-<node>` (orchestration C1). The **git agent alone** posts PR comments.
- [x] **Architecture for agents:** builder, tester and reviewer load a contract-only `architecture.md` skill. **No agent reads `*.spec.md`**, so agents never see the rejected options.

## Contract — PENDING (DRAFT)

### Roster

| Agent | Role | Inputs (from orchestrator prompt) | Output | `tools` | `disallowedTools` | model / effort | `skills:` | Never |
|---|---|---|---|---|---|---|---|---|
| **tester** | Red: one failing test per rule/boundary; integration test + Postman request per endpoint; rule-violation examples; QA script after PASS | plan id, node id, rule IDs, api-contract excerpt, bug report (on retry) | commits `test: <RULE-ID> …` (red, compiling, failing for the right reason); JSON: tests added → rule IDs, failing-test names, QA script path | Read, Grep, Glob, Edit, Write, Bash | Agent, NotebookEdit, WebFetch, WebSearch | opus / high | xunit-testing, postman-collections, requirements-traceability | edit `src/**`; weaken/delete an existing assertion to fix a loop; mark tests `Skip`; write tests that pass before builder runs; push/branch/merge |
| **builder** | Green + refactor; outside-in stub controller (step 2); real behaviour replaces stub | same + failing test names | commits `feat:`/`refactor:` per step; JSON: files changed, tests now passing, `dotnet build -warnaserror` + `dotnet test` result | Read, Grep, Glob, Edit, Write, Bash | Agent, NotebookEdit, WebFetch, WebSearch | sonnet / medium | dotnet10-webapi | edit `tests/**` or the Postman collection; add packages not in architecture.spec; catch-and-swallow to go green; reference `.claude/` from src; push/branch/merge |
| **reviewer** | Fresh-context judge: requirements met, traceability, TDD evidence, anti-oracle, code quality/slop | plan/node, rule IDs, base SHA..HEAD, security verdict not shown (independence) | verdict JSON (below), first token `PASS`/`NEEDS_WORK` | Read, Grep, Glob, Bash | Edit, Write, NotebookEdit, Agent, WebFetch, WebSearch | opus / high | requirements-traceability, dotnet10-webapi (read for idioms), xunit-testing | modify anything; accept a claim it didn't reproduce (`dotnet test` itself); propose patches instead of findings; read builder/tester self-summaries as evidence |
| **security** | OWASP API Top 10 2023 + .NET checks on the diff | as reviewer | verdict JSON with OWASP IDs, severity P0/P1/P2 | Read, Grep, Glob, Bash | Edit, Write, NotebookEdit, Agent, WebFetch, WebSearch | sonnet / high | owasp-api-security | modify anything; mark an "Out" item silently skipped (must be stated); fail on out-of-scope auth |
| **git** | Branch + worktree create; rebase onto main; full suite in worktree; `gh pr create`; post-merge cleanup | op name + feature id; PASS verdict path (for PR op) | JSON per op: `{op, ok, branch, worktreePath, headSha, testSummary, prUrl}` | Read, Grep, Glob, Bash | Edit, Write, NotebookEdit, Agent, WebFetch, WebSearch | haiku / low | git-pr | merge; push to `main`; force-push; `--no-verify`; raise PR without PASS verdict file; resolve conflicts by editing code (returns conflict to orchestrator); remove a worktree with uncommitted work |

Push: requirements.md says "every push is David's action" — then `gh pr create` (which needs a pushed branch) must be David's or explicitly approved. **Conflict flagged in Q1.**

Verdict fields (reviewer/security; schema owned by orchestration.spec):
`verdict (PASS|NEEDS_WORK)`, `agent`, `plan_id`, `node_id`, `attempt`, `base_sha`, `head_sha`, `evidence[] {command, exit_code, summary}`, `findings[] {id, rule_id|owasp_id, severity, file, line, what, why, route_to (tester|builder)}`, `rules_covered[]`, `rules_missing[]`, `notes[]`.

Reviewer checklist headline: every node rule ID has ≥1 unit test at each boundary + integration test + Postman example; status codes match api-contract; red commit precedes green per rule (`git log --format`); no test assertion weakened after its red commit; no stubs left; slop checklist (below); architecture tests pass.

### Skills

| Skill | Kind | Owner/used by | Content scope (≤ ~200 lines) |
|---|---|---|---|
| dotnet10-webapi | preloaded | builder (reviewer reads) | Only what we build: controllers + `[ApiController]` ProblemDetails/`AddProblemDetails`, `TimeProvider` injection, EF Core SQLite/Azure SQL provider switch, `decimal` + single rounding helper, DTO≠entity, minimal-diff refactor rules, build with warnings-as-errors. Points to architecture.spec Contract — doesn't restate it |
| xunit-testing | preloaded | tester, reviewer | Naming, builders, plain `Assert`, `FakeTimeProvider`, `WebApplicationFactory` + SQLite in-memory, boundary-table pattern (`[Theory]`), "fails for the right reason" check, architecture-test lib usage. Points to testing.spec |
| postman-collections | preloaded | tester | Collection v2.1 structure per endpoint/rule, environment file, example responses for each violation, run via **Postman CLI** (not Newman), QA script template ref |
| owasp-api-security | preloaded | security | R6 table as checklist, grep commands per check, severity rubric, "stated out of scope" wording |
| requirements-traceability | preloaded | reviewer, tester | Rule ID ↔ test name convention, coverage check commands, TDD-order git checks, anti-oracle checks, **slop checklist**: unexplained abstractions/interfaces with one impl, dead code, comments restating code, speculative generality, unused usings/packages, invented requirements, catch-all exception handling, magic dates/money not via helpers, inconsistent naming vs domain language |
| git-pr | preloaded | git | Branch/commit/PR conventions (from orchestration.spec), worktree commands, rebase + full-suite recipe, refusal list |
| /orient, /save-context, /load-context-chain | main session | David/orchestrator | per context.spec |
| /plan-dag | main session | David/orchestrator | per planning.spec |
| /plan-feature, /plan-bug, /plan-investigation | main session, **spec only** | — | built on first need (requirements.md) |

Main-session skills that must ask David use default invocation (not `context: fork`). Agent-preloaded skills must **not** set `disable-model-invocation: true` (blocks preloading); set `user-invocable: false` to keep them out of David's `/` menu.

### Mechanical boundaries (handed to guardrails.spec)

| Rule | Mechanism |
|---|---|
| builder ≠ tests/ | PreToolUse(`agent_type==builder`, path ∈ `tests/**`) deny + SubagentStop diff gate |
| tester ≠ src/ | same, mirrored |
| reviewer/security read-only | no write tools + PreToolUse deny on mutating Bash (belt and braces) |
| no merge / no push to main | permissions.deny `Bash(git push*main*)`, `Bash(gh pr merge*)`, `Bash(git merge*)` + PreToolUse |
| no PR without PASS | PreToolUse on `gh pr create` requires `docs/trace/<plan>/<node>/verdict-*.json` PASS for head SHA |
| agents stay in worktree | session `EnterWorktree` isolation checks (built in) |

## Open questions

1. **Push vs PR (changes requirements.md):** "every push is David's action" contradicts "git agent raises the PR" (`gh pr create` needs the branch on the remote). Options: git agent pushes the *feature branch* only (permission `ask`, David approves each), or git agent prepares the PR command and David runs it.
2. **Worktree path (changes requirements.md §3 Feature loop step 2):** `../wt/F1-sell` → `.claude/worktrees/F1-sell` to avoid an unsuppressible approval prompt per entry. Accept?
3. Should reviewer/security have `Skill` removed from `tools`, so only preloaded skills shape them (no self-loaded extras)? Recommend yes.
4. Per-rule tester↔builder ping-pong (≈15–20 dispatches/feature) vs batching all rules of a node per dispatch (fewer dispatches, weaker visible TDD history). Orchestration decides; this spec assumes per rule.
5. Spike in F0: (a) workflow `agent()` inherits an `EnterWorktree`'d cwd; (b) `agent_type` value for project agents is exactly the `name`; (c) SubagentStop exit 2 re-prompts the agent with stderr; (d) Bash-write detection via diff gate works on Windows paths.
6. Who records `start_sha` per dispatch for the diff gate — SubagentStart hook (mechanical, preferred) or orchestrator prompt?
7. `fable` model tier/cost unverified — check before any role uses it.
8. Security sees reviewer's verdict or not? Recommend neither sees the other's (independent lenses), orchestrator merges.
