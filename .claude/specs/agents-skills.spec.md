# agents-skills.spec.md

Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Settles: requirements.md §4 row 5 (roster, skills, why planning/orchestration aren't agents)

## Dependencies

**Upstream (all decided 2026-10-17; rows below were the research-time assumptions, superseded where the Contract says otherwise):**

| Spec | What this spec assumes until it's decided |
|---|---|
| architecture | `src/` holds Api, Services, Persistence, Domain; `tests/` holds Domain.Tests, Services.Tests (if needed), Api.Tests, Architecture.Tests + `postman/`. Rules live in the rich domain, not in controllers |
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

`agent_type` = the agent's name (e.g. `builder`). Absent for main-thread calls. *(The F0a spike confirmed `agent_type` = `name`, and found it sometimes empty on subagent events, so a subagent is identified by `agent_id`; see guardrails' F0a spike rulings.)* Settings-file hooks apply inside subagents. Deny = exit 2 (stderr to Claude; overrides even an "allow" JSON) or `permissionDecision: "deny"`. Frontmatter `hooks:` also work (scoped to that agent's lifetime; project ones need workspace trust; `Stop` → `SubagentStop`). `cwd` in hook input follows the worktree; `${CLAUDE_PROJECT_DIR}` does **not** (stays at the launch checkout) — hook scripts must resolve paths against `cwd`.

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
- **Implication for requirements.md:** worktree path `../wt/F1-sell` → **`.claude/worktrees/<plan>-<node>`** (e.g. `.claude/worktrees/build-F1`; branch `feature/<plan>/<node>`, created by git agent with `git worktree add`; orchestration C1). Add `.claude/worktrees/` to `.gitignore`. Alternative (keep `../wt/`): David approves one prompt per feature — acceptable, honest HITL, but noisier.
- Secondary: an agent can also be told absolute paths, but Bash cwd still resolves to the session cwd and `cd` doesn't persist — fragile. Not recommended.
- **Unverified:** whether workflow `agent()` subagents inherit a worktree the session entered *after* launch. Docs say subagents do; workflows run "in an isolated environment" — assume yes, **spike in F0a**.
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
- [x] **Final rulings (David, 2026-10-17):**
  - OQ9: the investigator works from the review packet only, with no shell. Revisit if F1 investigations stall.
  - Write scopes (guardrails G1): the builder may create the root build files (`global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `.gitattributes`, the `.slnx`); the tester creates the `tests/*.csproj` files.
  - David's steers (Pause 1, Pause 2, PR comments) reach the investigator as `D<n>` and don't count as attempts (orchestration).

## Contract

Binding. The six agents live in `.claude/agents/<name>.md` (project scope); `name` = `agent_type` in hooks = the `Agent:` trailer. Skills live in `.claude/skills/<name>/SKILL.md`. All are written by us; no third-party skills or plugins are installed.

### Universal rules (every agent)

- **No agent spawns agents** and **no agent loads skills at runtime**: `Agent` and `Skill` are absent from every `tools` list and repeated in `disallowedTools`. An agent's knowledge is its body plus its preloaded `skills:`.
- **Agents never read `*.spec.md`** (or anything under `.claude/specs/`). The specs hold rejected options; agents get the contract only, via skills. Enforced by guardrails: PreToolUse denies Read/Grep/Glob on those paths and Bash commands naming them, for any call with `agent_id` set (a subagent). The deny matches explicit `path` arguments and Glob patterns. A repo-root `.ignore` hides `.claude/specs/` from path-less Grep only (F0a spike), so it is a convenience, not a guard.
- **Harness lock** (guardrails): no agent writes `**/.claude/{agents,skills,hooks,specs,workflows}/**`, `**/.claude/settings*.json`, `**/.githooks/**`, `.git/**` (by file tool) or `CLAUDE.md`.
- Agents read `docs/requirements.md` rule rows by ID; the orchestrator's prompt gives plan id, node id, attempt, task and rule IDs, never spec text.
- Every agent's **final message is one JSON object** (schemas below). Producer JSON (tester, builder, git) is a report only: gates are run by the orchestrator, never taken from it. Reviewer, security and investigator JSON is schema-checked by a SubagentStop hook, which blocks the stop until it conforms. The block reason asks for the complete JSON to be restated, because only the last message survives a re-prompt.
- Commits carry the trailers `Agent`, `Plan`, `Node`, `Rule`, `TDD` (+ `Fixes-Finding`) per orchestration; explicit `git add <paths>` only.

### Roster

| Agent | Role | Inputs | Output (final JSON) | `tools` | `disallowedTools` | model / effort | `skills:` | Write scope (hook-enforced) | Never |
|---|---|---|---|---|---|---|---|---|---|
| **tester** | Red. Writes the node's **full acceptance suite** (every success and failure row of its rules) in the red step, unit tests per rule/boundary, Postman requests; adds throwing signature stubs so red compiles; on the failure route writes a red test per testable finding; writes the QA script after PASS | plan, node, attempt, task, rule IDs; on retry the findings file path | commits `test(<rule>): red: …`; `{agent, step: red\|finding-red\|qa, commits[], tests[{name, file, rule_id}], stubs[{file, member}], qa_script, untestable[{finding_id, reason}]}` | Read, Grep, Glob, Edit, Write, Bash | Agent, Skill, NotebookEdit, WebFetch, WebSearch | opus / high | architecture, tdd-outside-in, requirements-trace | `tests/**` (incl. `tests/postman/**` and the `tests/*/*.csproj` files, which the tester creates), `docs/qa/**`; in `src/**` only member signatures whose body is `throw new NotImplementedException()` | weaken, delete or `Skip` an existing assertion; write a test that passes before green; random test data; skip a finding's test without a stated reason; any non-stub `src/` change; push, branch, merge |
| **builder** | Outside-in: the stub controller, then green per layer (domain → services → persistence → api), then refactor | plan, node, attempt, task, rule IDs, failing test names; on retry the findings path and the red test | commits `feat(<rule>): green: …` / `refactor(<rule>): …`; `{agent, step: stub\|green\|refactor, layer, commits[], files[], rule_ids[]}` | Read, Grep, Glob, Edit, Write, Bash | Agent, Skill, NotebookEdit, WebFetch, WebSearch | sonnet / medium | architecture, dotnet10-webapi, tdd-outside-in, code-quality | `src/**`; root build files `*.slnx`, `global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `.gitattributes` | edit `tests/**` or Postman files; add a package the architecture skill doesn't list; `DateTime.Now/UtcNow/Today`, `Math.Round`/`decimal.Round`; catch-and-swallow to go green; leave a `NotImplementedException`; push, branch, merge |
| **reviewer** | Fresh-context judge: rules and acceptance met, TDD order, anti-oracle, code quality (SOLID at this size, rule of three), slop | plan, node, attempt, rule IDs; the review packet path (diff, trailer log, gate receipts, written by the orchestrator under the node's trace dir). Security's verdict is not shown | verdict JSON v1, `agent: reviewer`, finding ids `R1..` | Read, Grep, Glob | Edit, Write, NotebookEdit, Bash, PowerShell, Agent, Skill, WebFetch, WebSearch | opus / high | architecture, tdd-outside-in, requirements-trace, code-quality, verdict-format | none | modify anything; treat producer JSON or commit messages as evidence (only the packet and the code); propose patches instead of findings; PASS with an unchecked node rule |
| **security** | OWASP API Security Top 10 **2023** + .NET checks (R6) on the diff | as reviewer. Reviewer's verdict is not shown | verdict JSON v1, `agent: security`, ids `S1..`, `rule_id` = OWASP id (e.g. `API3:2023`) | Read, Grep, Glob | as reviewer | sonnet / high | owasp-api-security, verdict-format | none | modify anything; skip an "Out" item silently (`checked` row `n/a` + note); fail the node on out-of-scope auth (API1/2/5) |
| **investigator** | Root cause of a FAIL (reviewer, security, a gate, or a David PR comment): rule, layer, files, why, fix direction. Diagnoses only | plan, node, attempt, the verdict/comment/gate-receipt paths, the review packet path | findings JSON (below); the orchestrator renders it to `findings.md` | Read, Grep, Glob | as reviewer | opus / high | architecture, root-cause | none (read-only; it does not write `findings.md` itself) | modify anything; prescribe code; merge two findings without saying so; claim a cause without file:line evidence |
| **git** | Branch + worktree, rebase onto `origin/main` + full suite, push, draft PR, **all PR comments**, cleanup, archive | `op` + plan, node; per op: PR body file, verdict/receipt paths, finding ids, fix SHAs | `{agent, op, ok, branch, worktree, base_sha, head_sha, test_summary{exit_code, passed, failed}, pr_url, comments[{id, author, url, body}], comment_ids[], conflict_files[]}` | Read, Grep, Glob, Bash | Edit, Write, NotebookEdit, PowerShell, Agent, Skill, WebFetch, WebSearch | haiku / low | git-workflow | none by file tool; git operations only | merge; push to `main`; `--force*`; `--no-verify`; `git add -A`/`.`; resolve a conflict by editing (returns `conflict_files`); remove a worktree with uncommitted work; return or act on a comment not authored by `dpmorgandev`; `gh repo\|api\|auth` |

`git` ops: `setup` (fetch, `git worktree add .claude/worktrees/<plan>-<node> -b feature|bug/<plan>/<node> origin/main`), `integrate` (fetch, rebase, full suite), `pr` (`git push -u`, `gh pr create --draft --body-file`), `comment` (post the reviewer verdict, the security verdict and the rebase receipt, one comment each), `read-comments`, `reply` (one comment per addressed comment, naming the fixing SHA), `cleanup` (worktree remove, `branch -d` after David merges), `archive` (`chore/archive-<plan-id>` branch, David merges). `push`, `gh pr create` and `gh pr comment` are `ask` rules, so David approves each, bypass mode included.

### Agent frontmatter template (`.claude/agents/<name>.md`)

```markdown
---
name: builder
description: >-
  Green and refactor for one plan node, outside-in. Dispatched by /feature only.
tools: Read, Grep, Glob, Edit, Write, Bash
disallowedTools: Agent, Skill, NotebookEdit, WebFetch, WebSearch
model: sonnet
effort: medium
skills:
  - architecture
  - dotnet10-webapi
  - tdd-outside-in
  - code-quality
---
<!-- body: role · inputs · steps · output JSON · never-list. ≤ 120 lines. -->
```

Not set, on purpose: `permissionMode` (overridden by the session mode, R1), `isolation` (wrong worktree, R4), `mcpServers`, `hooks` (all hooks are settings-level, so one manifest), `memory`, `background`.

### Skills

Agent-preloaded skills set `user-invocable: false` and never `disable-model-invocation: true` (that blocks preloading). Main-session skills use default invocation, no `context: fork`. Skills carry contract content in their own words; they never link to or quote a spec file.

| Skill | Kind | Owner agents | Content outline |
|---|---|---|---|
| `architecture` | preloaded | tester, builder, reviewer, investigator | **Contract only, no options or rationale.** Supporting files, read on demand: `signatures.md` (architecture C3 signatures) and `api.md` (the api-contract Contract: routes, rule × status matrix, payloads, real-time table). *David, 2026-10-17: this is how the API contract reaches agents, since no skill carried it.* The four projects and their references (Services has no EF); where rules live (`Policy.Sell`, `policy.Cancel/Renew/RecordClaim`, `RefundCalculator`); `IPolicyRepository` (`GetByReference`, `Add`, `SaveChanges`), no generic repo/UoW; `UkCalendar.Today(TimeProvider)`, domain takes `DateOnly today`; `Money.Round` only; banned APIs; status-code map (400 shape / 422 rules / 409 state / 404) and the RFC 9457 body; `Guid Version` concurrency → 409; the allowed package list; the not-built list (events, caching, CQRS/MediatR, AutoMapper, auth, FluentValidation) as one line, no reasons |
| `dotnet10-webapi` | preloaded | builder | Only what we build: `[ApiController]` + DataAnnotations, `UnmappedMemberHandling.Disallow`, the unknown-query-parameter filter, enums as names, DTO ≠ entity, ProblemDetails mapping, `TimeProvider` injection, EF Core SQLite/SQL Server switch, health checks, structured logging without PII, warnings-as-errors build, minimal-diff refactor |
| `tdd-outside-in` | preloaded | tester, builder, reviewer | The outside-in order (acceptance suite red → stub controller → green per layer → refactor); red = compiles and fails for the right reason; throwing-stub rule; test conventions (sentence names, builders with valid defaults, plain `Assert`, `[Trait("Rule", …)]`, `WebApplicationFactory` + SQLite in-memory + `FakeTimeProvider`, `[Theory]` boundary tables); Postman v2.1 request naming by rule ID; QA script template (`docs/qa/<plan>-<node>.md`) |
| `requirements-trace` | preloaded | tester, reviewer | Rule ID ↔ test trait convention; every node rule has a success and failure row; red commit precedes green per rule (from the packet's trailer log); no assertion weakened after its red commit; no stub left; status codes match the architecture map |
| `code-quality` | preloaded | reviewer, builder | SOLID at this size: SRP, DI only at real seams (repository, `TimeProvider`), no interface with one implementation otherwise; **rule of three** before extracting duplication; slop list: dead code, comments restating code, speculative generality, unused usings/packages, invented requirements, catch-all handlers, magic dates/money, naming off the domain language |
| `verdict-format` | preloaded | reviewer, security | The verdict JSON schema v1 and its PASS/FAIL semantics, byte-identical to orchestration.spec's block (the wiring self-test diffs the two) |
| `owasp-api-security` | preloaded | security | The R6 table as a checklist (In/Out per item, `n/a` wording for Out), the grep per check, the .NET extras (raw SQL, `decimal`, vulnerable packages, secrets in the diff), severity rubric (P0/P1/P2, per orchestration's verdict schema) |
| `root-cause` | preloaded | investigator | Work only from the packet, verdict and receipts; locate the layer, name one cause per finding with file:line, distinguish symptom from cause, fix direction without code, decide testable vs untestable (reason required), the findings JSON |
| `git-workflow` | preloaded | git | Branch/worktree naming, each op's exact commands, rebase + full suite recipe, PR body = the orchestrator's body file (trace summary + David's paragraph), comment posting, `read-comments` filters **in the command** (`gh pr view --json comments --jq` on `author.login == "dpmorgandev"`) so other authors' text never enters context, reply-with-SHA format, refusal list |
| `feature` | main session | orchestrator (David invokes) | The loop: per orchestration.spec Contract |
| `orient`, `save-context`, `load-context-chain` | main session | David / orchestrator | Per context.spec Contract |
| `plan-dag`; `plan-feature`, `plan-bug`, `plan-investigation` | main session | David / orchestrator | Per planning.spec Contract (`plan-dag` built in F0a, the rest on first need) |

The stale `decide` and `tdd` skills are not rebuilt (R7; decisions live in specs).

**Line caps** (checked by the guardrails wiring self-test): agent body ≤ 120 lines; preloaded skill body ≤ 200 lines; `architecture` ≤ 150; main-session skill body ≤ 250; `description` ≤ 3 lines.

### CLAUDE.md (repo root)

Every agent sees it: Claude Code loads it into the main session and into each dispatched subagent. So it follows the skill rule: **contract only**, no rationale, no rejected options, and no link to, quote of or `@import` of any spec.

- **Content, in this order:** what the project is, with a pointer to `docs/requirements.md`; the session ritual (`/orient` first, `/save-context` at node boundaries); the plan and loop skills (`/plan-dag`, `/feature`, `/plan-feature`, `/plan-bug`, `/plan-investigation`); the hard rules (commit identity and trailers, no push to `main`, David merges, nothing private in a public repo, agents never read `.claude/specs/`, the harness lock); the repository layout; the build and test commands.
- **Cap:** ≤ 80 lines, checked by the guardrails wiring self-test.
- **Authorship:** written by the main session in F0a; locked by the harness lock (G4) from the end of F0a.
- **Spike in F0a:** a dispatched subagent has `CLAUDE.md` loaded (open question 5f).

### Verdict JSON (reviewer, security)

**orchestration.spec's Verdict JSON schema v1 is canonical, verbatim** (David, 2026-10-17). This spec doesn't fork it or restate its fields. The `verdict-format` skill carries the block byte-for-byte, and the wiring self-test diffs the two. Key points for the agent bodies:
- `verdict` is `PASS|FAIL`.
- Coverage is `rules_covered[]` and `rules_missing[]`.
- Findings are `R<n>` (reviewer) or `S<n>` (security), with `rule_id`, severity `P0|P1|P2`, `file`/`line`, `what`, `why` and `route_to`.
- Evidence is the orchestrator's gate receipts (`gate_refs`), because reviewer and security run no commands.
- The `owasp-api-security` severity rubric uses `P0|P1|P2`.

### Findings JSON (investigator)

**orchestration.spec's Investigator findings schema v1 is canonical, verbatim**, and the SubagentStop hook validates it. Its findings are `I<n>`, each with `rule_id`, `layer`, `files[]`, `root_cause`, `fingerprint`, `testable`, and either `suggested_test` or `untestable_reason`. It also carries `source`, `source_ref` and `previous_attempts[]`. The retry ladder's early escalation reads `fingerprint`. `testable=false` requires an `untestable_reason`. Every `root_cause` cites file:line.

*Superseded (David, 2026-10-17):* an earlier field list here (`checked[]`, `blocker|major|minor`, `evidence[]`, `causes[]`) claimed to match orchestration but never did. orchestration's blocks win.

### Mechanical boundaries (scripts owned by guardrails.spec)

| Rule | Mechanism |
|---|---|
| per-agent write scope (roster column) | one PreToolUse hook keyed on `agent_type` + SubagentStop diff-scope check (start SHA recorded by SubagentStart per `agent_id`) |
| tester `src/` stubs only | the diff check rejects any added `src/` member body other than `throw new NotImplementedException()` |
| reviewer / security / investigator read-only | no write or shell tools (allowlist) + PreToolUse deny on any write by those `agent_type`s |
| no spawning, no runtime skills | `Agent`, `Skill` absent from `tools` |
| agents never read specs | PreToolUse deny, matching `path` arguments and Glob patterns; `.ignore` is a convenience only (Universal rules) |
| no merge, no push to `main`, no force | permissions deny (incl. `gh pr merge *`) + PreToolUse; `ask` on push / `gh pr create` / `gh pr comment` |
| no PR without receipts | PreToolUse on `gh pr create` (guardrails G14) denies unless the latest reviewer and security verdicts are PASS with `head_sha` = HEAD |
| comments from `dpmorgandev` only | filter inside the `read-comments` command (git-workflow) + the orchestrator drops any returned comment by another author |
| agents stay in the worktree | session `EnterWorktree` isolation (built in) |

## Open questions

1. ~~Push vs PR~~ **Resolved** (orchestration E): the git agent runs `git push -u` and `gh pr create --draft` behind `ask` rules.
2. ~~Worktree path~~ **Resolved:** `.claude/worktrees/<plan>-<node>` (orchestration C1).
3. ~~Remove `Skill` from reviewer/security~~ **Resolved:** removed from all six.
4. ~~Per-rule tester ↔ builder cycles vs batching per node.~~ **Resolved (David, 2026-10-17, planning.spec amendment):** batched per layer. Each node's explicit tasks run a tester red then a builder green per layer, and each task names the rule ids it covers.
5. Spike in F0a: (a) workflow `agent()` inherits an `EnterWorktree`'d cwd (moot unless workflows are used); (b) `agent_type` = `name` exactly; (c) SubagentStop exit 2 re-prompts the agent with stderr; (d) diff-scope check on Windows paths; (e) the Grep tool honours `.ignore`; (f) a dispatched subagent has `CLAUDE.md` loaded. **Resolved (F0a t3–t5)**, with receipts in `docs/trace/build/F0a/spikes.md`.
   - (a) Not run: workflows aren't used.
   - (b) Yes.
   - (c) Yes, but only the last message survives.
   - (d) Deferred to the G3 fixtures (t13/t16).
   - (e) Only for path-less Grep.
   - (f) Yes: the main checkout's project `CLAUDE.md` plus the user-level one, even after `EnterWorktree`.
6. ~~Who records `start_sha`~~ **Resolved:** a SubagentStart hook (guardrails O2-A).
7. ~~`fable`~~ **Resolved (moot):** no role uses it.
8. ~~Security sees the reviewer's verdict~~ **Resolved:** neither sees the other's; both run in parallel.
9. Resolved (David, 2026-10-17): the review packet only, with no shell, as the Contract says. Revisit if F1 investigations stall.
10. Orchestration.spec to adopt the findings JSON and the investigator route (see "Differences to reconcile"). **(open, orchestration)**
