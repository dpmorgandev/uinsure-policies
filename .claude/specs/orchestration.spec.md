# orchestration.spec.md
Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Input: `docs/requirements.md` §3 (Feature loop, Planning model, Rules), §5, §6 items 3, 5, 7
Environment checked: Claude Code 2.1.302, git 2.49.0, gh 2.95.0, Windows 11, remote `origin` already set (github.com/dpmorgandev/uinsure-policies-api)

Scope: the feature loop as a state machine. It runs from picking a node to archiving it. It also covers the workflow-vs-skill choice, the verdict schema, the bug-report handoff, where loop state lives, the HITL mechanics, and the branch, commit and PR conventions.

## Dependencies

| Spec | What this spec assumes (not decided there yet) | If the assumption breaks |
|---|---|---|
| `planning` | Plan YAML holds node `status`/`attempts`/`branch`/`pr`, written only as feature-branch commits (its option A2). Node ids `F0a, F0b, F1…`, `N1…`. Attempts are counted **per node**, one per FAIL cycle. Attempt 3 is `/plan-investigation`, and it can recommend `amend-node` | The state section moves: the trace stays the receipt either way |
| `agents-skills` | Roster: `git`, `tester`, `builder`, `reviewer`, `security`. The orchestrator is the main session running a skill, **not** an agent. Each agent has a `.claude/agents/<name>.md` so that `agent_type` in hooks is its name | Without named agent types, hook-side identity is lost (see traceability) |
| `testing` | One full-suite command (`dotnet test <sln>`). A rule filter (`--filter "Rule=SELL-2"` on VSTest, or `--filter-trait` on xUnit v3/MTP). QA script at `docs/qa/<plan-id>-<node-id>.md` | Only the command strings in the gates change |
| `architecture` | The SQLite connection string is relative to the content root. Dev startup applies migrations or creates the DB | HITL DB isolation needs an explicit path override |
| `guardrails` | Enforces the hooks named here (no push to `main`, PR gate, path ownership, no `git add -A`) | This spec names gates. Guardrails implements them |
| `traceability` | Owns the file formats under `docs/trace/`. This spec only says which events must leave a receipt | — |

## Research

### R1. Running agents inside ONE shared, existing worktree (open item §6.5): the key finding
- **`isolation: worktree` is wrong here, as suspected.** Each subagent gets a *fresh* temporary worktree. It branches by default from the default branch, not from the parent's HEAD (`worktree.baseRef: "head"` changes that). It is auto-removed if unchanged. The tester and builder would never see each other's files. ([sub-agents](https://code.claude.com/docs/en/sub-agents), [worktrees](https://code.claude.com/docs/en/worktrees))
- **No `cwd` option exists**, neither in agent frontmatter nor in workflow `agent()` opts. The `agent()` opts are `label, phase, schema, model, effort, isolation:'worktree', agentType`. A subagent "starts in the main conversation's current working directory". `cd` inside it does not persist between Bash calls.
- **The mechanism that does work: move the *main session* into the worktree with `EnterWorktree({path})`.** [tools-reference](https://code.claude.com/docs/en/tools-reference): "Pass a `path` to switch into an existing worktree instead of creating a new one." After that:
  - Every subagent the session spawns starts there.
  - Claude Code **enforces isolation** for the session *and for every subagent it spawns*: it blocks Edit/Write to the main checkout, blocks Bash/PowerShell whose cwd resolves to the main checkout, and blocks git redirected into it (`git -C`, `GIT_DIR`, …).
  - This is §3's "main and David's checkout are never touched", enforced by the harness and not only by prose.
- **Caveats** (from the docs, so they are facts, not guesses):
  - A `path` outside `.claude/worktrees/` triggers an **approval prompt on every entry**, and a permission rule can't suppress it (only `bypassPermissions` can).
  - From inside a worktree session, `EnterWorktree` can only switch to paths **under `.claude/worktrees/`**. To move between features elsewhere, `ExitWorktree` first.
  - **PowerShell gets only the cwd check**, and **no check tracks what a shell command writes**: `cp`/redirects into the main checkout aren't refused. Guardrails needs a hook for this, and it matters on this Windows box, where PowerShell is the primary shell.
  - **`${CLAUDE_PROJECT_DIR}` stays at the main checkout.** Hook *commands* run main's scripts, while the hook's input `cwd` is the worktree. Hooks must write trail files under `cwd`'s repo root, never under `CLAUDE_PROJECT_DIR`.
  - Entering "takes … project configuration such as `CLAUDE.md` and settings to that location". So the harness in force is the **branch's** copy, but the hook *scripts* resolve via `CLAUDE_PROJECT_DIR` to main's copy.
  - **F0 bootstrap hazard:** a hook added on the F0 branch is registered in the worktree settings, but its script doesn't exist in main yet. The command then fails as a non-blocking error, and the trail **silently stays empty**. That is exactly agentkinOS's Task→Agent rename failure (see `trace-agent-activity.mjs` comments). Mitigation: in hook commands, resolve script paths from the input `cwd`, or accept that F0 is witnessed by git only.
- **Unverified, so a spike is required** (15 min, first task of F0):
  - (a) whether **workflow** agents launched after `EnterWorktree` also start in the worktree and inherit the checks
  - (b) whether `EnterWorktree` reloads `.claude/agents/` and settings mid-session
  - (c) `agent_type` values in PostToolUse for a workflow `agentType:` agent
- **Alternative, also documented:** David does `cd <worktree> && claude`, or `claude --worktree <name>` (which creates a `worktree-<name>` branch, so it's the wrong name for us). One session per feature then gives the same isolation with no approval prompt. The cost is a session restart per feature, which pairs naturally with `/load-context-chain`.

### R2. Workflow vs skill (open item §6.3) ([workflows](https://code.claude.com/docs/en/workflows), `/workflow-authoring`)
- **Workflow facts:**
  - `agent(prompt,{schema})` returns validated JSON, retrying up to 5 times.
  - **No mid-run user input.** The docs say: "For sign-off between stages, run each stage as its own workflow."
  - **The script has no filesystem or shell access**, and `Date.now()` is banned.
  - It runs in the background and can be resumed via `resumeFromRunId` **within the same session**. In a fresh session "Claude has no earlier run to relaunch" (a session resumed with `--resume` can replay).
  - Saved scripts live in `.claude/workflows/<name>.js` and run as `/<name>`. The script is a readable, diffable artifact.
- **Consequence 1:** in a workflow, every deterministic gate (`dotnet test`, `git diff --name-only`) must be run *by an agent* and reported back. That is a self-report, which is exactly the lesson from agentflow LESSONS ("an authoring agent's self-report … is worthless"). Hooks still fire inside agents, so gates can live in hooks, but the script itself can't verify anything.
- **Consequence 2:** the retry ladder's attempt 3 (investigation → David approves) and attempt 5 (escalate) both **end** a workflow run. The run boundary becomes the attempt boundary.
- **Subagent facts:**
  - A plain subagent's output can't be schema-forced. But **`SubagentStop` can block** (exit 2 / `decision:block`), and its input carries `agent_type`, `agent_transcript_path` and `last_assistant_message` ([hooks](https://code.claude.com/docs/en/hooks)).
  - So a SubagentStop hook can **validate the verdict JSON and refuse to let the reviewer stop** until it conforms. That gives a schema guarantee without workflows, and the hook also writes the verdict receipt outside the agent's turn.
- **Nesting:** main → subagent is 1 level (default cap 3). Reviewer and security run in parallel as two `Agent` calls in one message, so no workflow is needed for that fan-out.
- **Headless `claude -p` is ToS-barred on Max**, so everything stays interactive. Workflows *are* interactive-compatible, which makes them the only "scripted" option left.

### R3. Merge strategy for visible TDD history (GitHub behaviour, verified locally for trailers)
| Strategy | red/green/refactor commits on `main`? | SHAs | Trailers | `git log --first-parent main` |
|---|---|---|---|---|
| Merge commit (`--no-ff`) | **Kept** | **Unchanged** (trace SHAs stay valid) | Kept | One line per feature (the PR boundary) |
| Rebase and merge | Kept | **Rewritten** (GitHub always re-commits) | Kept | Flat, no feature boundary |
| Squash | **Destroyed** (one commit) | New | Lost unless copied | One line per feature, no TDD evidence |

Our own pre-PR rebase already makes the branch linear on top of `origin/main`. A merge commit on top of that gives semi-linear history: TDD steps visible, PR boundaries visible, and SHAs recorded in the trail still resolve. Repo settings ("Allow merge commits" only) are David's action.

### R4. Integration agent (open item §6.7): confirm, with two challenges
- **Confirmed: no separate agent.** One feature runs at a time, so the branch can only lag `origin/main` by merges David made. `git fetch origin` plus `git rebase origin/main` plus the full suite is mechanical. An agent adds judgement nobody needs. Rebasing onto **`origin/main`** (fetch only updates remote-tracking refs) means David's checkout is never touched. `git pull` on his `main` stays his.
- **Challenge 1: the requirements contradict themselves on push.** §3 Rules says "every push [is] David's action", but step 5 has the git agent "raise the PR", and `gh pr create` needs the branch pushed. One of these must change (see Options E).
- **Challenge 2: order.** Step 5 raises the PR *before* step 6 (QA + HITL). If HITL finds a defect, commits land after the integration receipt, so the rebase-and-test receipt must be **invalidated by any later commit** and re-run. Raising the PR as a **draft** and marking it ready after sign-off keeps "No PR without a PASS" meaningful.

### R5. HITL mechanics (open item §6.5)
- **Run from the worktree:** `dotnet run --project <wt>/src/<Api> --urls http://localhost:5080`. The trouble with `launchSettings.json` ports is that they are the same in every checkout, and a clash only happens if David also runs main at the same time. One fixed HITL port plus a Postman environment `baseUrl` is enough. Per-worktree ports would be over-building at one-feature-at-a-time.
- **SQLite:** `Data Source=policies.db` resolves against the content root. `dotnet run --project` sets that to the project dir *inside the worktree*, so every worktree gets its own DB file for free. `*.db` must be gitignored, and the DB is deleted or recreated at HITL start for a clean run. (Assumption: architecture spec. If the path is absolute, override it with `ConnectionStrings__Policies` env.)
- **VS 2026:** a worktree is just a folder holding a full checkout. Open `<wt>/Uinsure.Policies.slnx` with File › Open › Project/Solution. `.vs/` is per-folder, so settings don't bleed. Whether VS's Git window manages worktrees natively is **unverified**. It isn't needed.
- **Worktree location trade-off:** `.claude/worktrees/<plan>-<node>` gets no approval prompt and is Claude Code's default, but it sits physically inside David's checkout (gitignored). `../wt/<plan>-<node>` matches requirements.md, but needs approval on each `EnterWorktree` (arguably a fine "entering feature" checkpoint).

### R6. Prior art (agentflow_phase3, agentkinOS)
- **`pipeline.ts`:** `defaultWaitForGate = async () => ({ approved: true })`. The default gate *auto-approves*. This is the archetype of "a skipped step reads as PASS". Our gates must default to **BLOCKED**.
- **`fix-bugs-loop.ts`:**
  - iteration cap 5, runs "INSIDE a single shared `fixup` worktree" (the same problem as ours, solved there by the external TS orchestrator)
  - convergence detector: the same failure repeating means escalating early
  - flap detection: a bug that was fixed reappears
  - Windows MAX_PATH failures on `git worktree remove` (keep worktree paths short)
- **LESSONS:** "a quality gate that degrades to WARN when its measuring loop was SKIPPED is not a gate". The `the-turn` brief passed every gate because the craft loop never ran.
- **`verify-gate.mjs` + `track-read.mjs`:** a PreToolUse deny on flipping `passes:true` unless the evidence file was Read this session. That shape ports directly to the PR gate.

## Options

**A. Orchestration vehicle**
- **A1 Workflow per feature** (`.claude/workflows/feature.js`, args `{plan,node,attempt}`)
  - Pro: the loop is code. Schema'd verdicts. Runs in the background. A readable artifact.
  - Con: no shell in the script, so gates become agent self-reports. Every David touchpoint ends the run. Resume only within the session. The worktree cwd for workflow agents is unverified (R1a).
- **A2 `/feature` skill in the main session dispatching named subagents**
  - Pro: the orchestrator runs gates itself via Bash, outside any producer's turn. It can ask David mid-loop. Hooks give schema enforcement (SubagentStop) and receipts. It works with `EnterWorktree` as documented.
  - Con: the loop lives in prose, so the orchestrator can drift or skip steps (the shadow-skill risk). Results pass through main context.
- **A3 Hybrid:** a skill owns the state machine and the David touchpoints, and one workflow per attempt runs tester→builder→(reviewer∥security).
  - Pro: schema'd attempt.
  - Con: two mechanisms to explain in the interview, and gates are still inside agents for the attempt.

**B. Worktree entry**
- B1 `EnterWorktree({path})` from one long session
- B2 a fresh `claude` session started in the worktree, once per feature
- B3 prompts tell agents absolute paths (rejected: prose, unenforced)

**C. Worktree location:** C1 `.claude/worktrees/<plan>-<node>` · C2 `../wt/<plan>-<node>`

**D. Merge strategy:** D1 merge commit · D2 rebase-merge · D3 squash (rejected: destroys TDD evidence)

**E. Push/PR (resolves the R4 contradiction)**
- E1 The git agent prepares `pr-body.md` and prints the exact `git push -u origin <branch>` and `gh pr create --draft …` commands. David runs them.
- E2 David allows a push of `feature/**` only (a PreToolUse allowlist) and the git agent runs them.
- E3 PR after HITL only.

**F. Bug-report routing:** F1 every FAIL finding goes to the tester first (a red test reproduces it), then the builder · F2 straight to the builder · F3 F1 by default, F2 when the finding is `testable:false` with a reason

## Recommendation

- **A2 (skill + subagents + hooks)** for F0/F1, with the loop's *invariants* made mechanical:
  - verdict schema enforced at `SubagentStop`
  - receipts written by hooks or scripts, never by producers
  - a PR gate that checks receipts and defaults to BLOCKED

  The reason: the decisive requirement is "verify outside the agent's turn", and only the main session can run `dotnet test` itself. Workflows stay recorded as a considered option, with a concrete re-evaluation trigger: if the F0 run shows the orchestrator drifting from the skill, move the attempt body to A3.
- **B1 + C1**, conditional on the R1 spike. If the spike fails, B2. One long session in the worktree keeps `/save-context` continuity. C1 avoids an approval prompt per entry and lets `EnterWorktree` switch features without `ExitWorktree`. Name: `.claude/worktrees/<plan-id>-<node-id>` (short, for MAX_PATH).
- **D1 merge commit**, with the repo set to allow merge commits only.
- **E1 now.** It honours "every push is David's action" literally, and the cost is two commands per feature. Raise as **draft**, and mark ready after sign-off.
- **F3.**
- **Confirm no integration agent**, rebasing onto `origin/main`, with the receipt invalidated by later commits.

## Decision — DECIDED 2026-10-17 (David)
- [x] **A → A2:** the `/feature` skill runs in the main session and dispatches the named subagents. The orchestrator runs the gates itself via Bash and stops for David's two pauses (see planning). Hooks enforce verdict schemas and record receipts.
- [x] **B/C → B1 + C1:** worktrees go in `.claude/worktrees/<plan>-<node>`, entered via `EnterWorktree`. Spike in F0a. Branches are `feature/<plan>/<node>` or `bug/<plan>/<node>`.
- [x] **D → D1:** merge commits only. David sets the GitHub repo setting.
- [x] **E → G7 variant:** the git agent *runs* `git push -u origin <branch>` and `gh pr create --draft`. An `ask` rule prompts David for each one, even in bypass mode, and names the agent. Pushes to `main` are denied. The draft PR opens before HITL, and David marks it ready and merges after sign-off.
- [x] **Branching: `main` is the integration branch.** Feature and bug branches are cut from `main`. Before the PR, the git agent rebases onto `origin/main` and runs the full suite. There's no `develop` branch and no separate integration agent. Any later commit invalidates the rebase receipt. This also goes in the README.
- [x] **F → amended (David): investigate before build.**
  - On any FAIL, a **read-only investigator agent** diagnoses the root cause (rule, layer, files, why) and writes `findings.md`.
  - The **tester** writes a red test from the findings. It may skip this only if the finding is untestable, and must say why.
  - The **builder** receives the findings and the red test, then goes green. Reviewer and security re-run.
  - This adds a sixth agent to agents-skills.
- [x] **Retry ladder:**
  - Attempts 1–2: investigate → red → fix.
  - Attempt 3: escalate to a full `/plan-investigation`.
  - Attempt 5: stop and ask David.
  - A repeated finding fingerprint escalates early.
- [x] **HITL:** the API runs on fixed port 5080, with a SQLite DB per worktree, and the `.slnx` opens in VS 2026.
- [x] **PR contract (David):**
  - **PR body:** generated from the node's trace summary (rules covered, test counts, a `walkthrough.md` link), plus one paragraph David writes.
  - **Agent comments:** the **git agent posts every comment**, so there's one identity and one `ask` rule on `gh pr comment`. That's one comment each for the reviewer verdict, the security verdict (with OWASP IDs) and the rebase-and-test receipt.
  - **David's comments on an open PR:** `/feature` reads them with `gh pr view --comments`. Each one becomes a finding and enters the investigate → red → fix route on the same branch. The git agent replies to each with the commit SHA that fixed it.
  - **Only comments by `dpmorgandev` are acted on.** Everything else is ignored. This guards against prompt injection once the repo is public.
  - A comment on an already-merged PR becomes a `/plan-bug`.
- [x] requirements.md edits: apply them in the requirements update pass.

## Contract — PENDING (DRAFT outline)

### State machine (one node)
```text
            ┌────────────────────────────────────────────────────────────────────────┐
 PICK ──► SETUP ──► RED ──► GREEN ──► REVIEW(reviewer ∥ security) ──PASS──► INTEGRATE ──► PR_DRAFT ──► QA ──► HITL
 (next ready (git: fetch,  (tester:  (builder:  │ both verdicts must exist, be      (git: fetch,   (E1: David      (script   │
  node in DAG) worktree add, commits  commits    │ schema-valid and bound to the     rebase         pushes +         from     │ sign-off ──► MERGE_WAIT
              EnterWorktree) red)     green/     │ current diff fingerprint)         origin/main,   gh pr --draft)   testing  │   (David merges; marks
                                      refactor)  FAIL                                 full suite)                   spec)     │    PR ready first)
                                        ▲        ▼                                                                            │        │
                                        │   BUG_REPORT ── attempts++ ──┬─ attempts ≤ 2 ──► RED (different approach)           │        ▼
                                        │                              ├─ attempts = 3 ──► INVESTIGATE (/plan-investigation)  │    ARCHIVE
                                        │                              │                    └─ David picks: amend-node ─► RED │  (ExitWorktree; git:
                                        │                              ├─ attempts = 4 ──► RED (investigation's approach)     │  worktree remove,
                                        │                              └─ attempts = 5 ──► ESCALATED (stop; David decides)    │  branch -d after David
                                        └──────────────── HITL defect (pre-merge) = BUG_REPORT, source: david ◄───────────────┘  pulls; plan → archive)
 Any gate whose receipt is missing ──► BLOCKED (never PASS). Any commit after INTEGRATE invalidates it ──► INTEGRATE again.
```
- Transitions are recorded as events in `docs/trace/<plan>/<node>/loop.jsonl` (format: traceability spec). The **current state is derived from the last event**, never kept as a separate mutable field. Plan YAML `status/attempts` is updated by the orchestrator as a branch commit at SETUP, at each attempt and at PR_DRAFT (planning A2).
- **Resume after crash or compaction:** `/orient` reads `loop.jsonl` plus `git log origin/main..HEAD` with trailers. If they disagree, **git wins** (e.g. a green commit exists after the last logged event: the state is GREEN-done, and the event is backfilled with `source: reconciled`).
- **Who runs what:** the orchestrator (main session) runs every gate command itself. Producers' claims are never used as gate input.

### Gates (each writes a receipt; a missing receipt is BLOCKED)
| Gate | Check (run outside the producer's turn) | Receipt |
|---|---|---|
| after RED | rule-filtered tests **fail**. The diff touches only `tests/**`. The commit has `TDD: red` | `gate.red` |
| after GREEN | rule tests pass. Full build has warnings-as-errors. The diff touches only `src/**` | `gate.green` |
| REVIEW | 2 verdicts, schema-valid (SubagentStop hook), `diff_fingerprint` = current `git diff origin/main...HEAD \| git patch-id --stable` | `verdict.*` |
| INTEGRATE | rebase clean. Full suite (unit + integration + architecture) exit 0 on the post-rebase HEAD | `gate.integrate` with `head_sha` |
| PR | PreToolUse deny on `gh pr create`/`git push` unless the receipts above exist for the current HEAD (and E1: these are David's commands anyway) | `pr` event |
| HITL | QA script signed by David, `signed_sha` = HEAD | `signoff` |

### Verdict JSON schema (DRAFT; reviewer and security return exactly this as their final message)
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "additionalProperties": false,
  "required": ["schema_version","agent","plan_id","node_id","attempt","head_sha","diff_fingerprint","verdict","checked","findings"],
  "properties": {
    "schema_version": { "const": 1 },
    "agent":   { "enum": ["reviewer","security"] },
    "plan_id": { "type": "string", "pattern": "^(build|feat-\\d{3}|bug-\\d{3})$" },
    "node_id": { "type": "string", "pattern": "^(F\\d+[a-z]?|N\\d+)$" },
    "attempt": { "type": "integer", "minimum": 1, "maximum": 5 },
    "head_sha": { "type": "string", "pattern": "^[0-9a-f]{40}$" },
    "diff_fingerprint": { "type": "string" },
    "verdict": { "enum": ["PASS","FAIL"] },
    "checked": {
      "description": "What was examined: an empty list makes PASS invalid (hook rejects). Proves the loop ran.",
      "type": "array", "minItems": 1,
      "items": { "type": "object", "additionalProperties": false, "required": ["id","result"],
        "properties": { "id": { "type": "string", "description": "rule id (SELL-2) or OWASP id (API3:2023)" },
                        "result": { "enum": ["met","not-met","n/a"] },
                        "note": { "type": "string" } } } },
    "findings": {
      "type": "array",
      "items": { "type": "object", "additionalProperties": false,
        "required": ["id","rule_id","severity","title","evidence","expected","actual","testable"],
        "properties": {
          "id": { "type": "string", "pattern": "^[RS]\\d+$", "description": "R1.. reviewer, S1.. security; unique per verdict" },
          "rule_id": { "type": "string", "description": "requirements.md rule id, or OWASP API Top 10 2023 id" },
          "severity": { "enum": ["blocker","major","minor"] },
          "title": { "type": "string", "maxLength": 120 },
          "evidence": { "type": "array", "minItems": 1, "items": { "type": "object", "required": ["file"],
              "properties": { "file": { "type": "string" }, "line": { "type": "integer" }, "excerpt": { "type": "string" } } } },
          "expected": { "type": "string" },
          "actual": { "type": "string" },
          "testable": { "type": "boolean" },
          "suggested_test": { "type": "string", "description": "Required when testable: Given/When/Then + target test class" },
          "untestable_reason": { "type": "string", "description": "Required when testable=false" } } } }
  }
}
```
Hook-side semantic checks (beyond the schema):
- `verdict=PASS` ⇒ there are no `blocker`/`major` findings, and `checked` covers every rule id of the node (read from the plan YAML).
- `verdict=FAIL` ⇒ there is at least one finding.
- `head_sha`/`diff_fingerprint` must equal the hook's own computation, so a verdict about stale code is rejected.

### Bug report handoff (DRAFT)
The orchestrator renders one file per FAIL: `docs/trace/<plan>/<node>/bugs/A<attempt>.md`. It is generated from the verdict JSON, not rewritten by hand. It has these parts:
- a header with plan, node, attempt n of 5, source (`reviewer|security|david`), and the verdict receipt ids
- one section per finding: rule id · severity · expected · actual · evidence (file:line) · `suggested_test`
- a block for the **tester**: "write a failing test for each testable finding, commit `TDD: red`, with `Fixes-Finding: R1` trailer"
- a block for the **builder**: "make it green. Untestable findings listed with reasons"
- **Previous attempts:** one line each (from `loop.jsonl`), so attempts 2+ try a *different* approach
- a fingerprint `(rule_id, file, title-hash)`. If the same fingerprint was in the previous attempt, the orchestrator escalates to attempt 3 at once (agentflow convergence lesson).

### Branch, commit, PR conventions (DRAFT)
- **Branch:** `feature/<plan-id>/<node-id>`, e.g. `feature/build/F1`, `bug/bug-001/N1`. No slug, matching planning's stable-id rule. requirements.md's `feature/F1-sell` example changes. **Worktree:** `.claude/worktrees/<plan-id>-<node-id>`.
- **Commit subject:** `test(<rule>): red: <behaviour>` · `feat(<rule>): green: <behaviour>` · `refactor(<rule>): <what>` · `plan(<plan>/<node>): <from> → <to>` · `chore(trace): <node> trail`.
- **Trailers:** `Agent:`, `Plan:`, `Node:`, `Rule:`, `TDD: red|green|refactor`, optional `Fixes-Finding:`.
  - `Agent`/`Plan`/`Node` are enforced by a hook (PreToolUse on `git commit` denies a mismatch against `agent_type` and the branch name). See traceability.
  - Never commit-on-stop. Explicit `git add <paths>` only, never `-A` or `.`.
- **PR:** draft. The title is `<node-id>: <node title>`. The body is generated from the trail (traceability spec): rules → tests → commits table, attempts and verdicts, the QA script link, and how to run the API from the worktree.

### HITL step (DRAFT)
1. The orchestrator prints: the worktree path, `dotnet run --project <wt>/src/<Api> --urls http://localhost:5080`, the Postman env `baseUrl=http://localhost:5080`, the QA script path, and `dotnet test <wt>/<sln>`.
2. David opens `<wt>/Uinsure.Policies.slnx` in VS 2026 if he wants to read or debug, steps through the QA script and ticks it.
3. David replies with one of:
   - `sign-off`: the orchestrator records `signoff` with HEAD
   - `defect: …`: a BUG_REPORT with source david, back into the loop
4. David marks the PR ready and merges with a merge commit. Then he tells the orchestrator (or `/orient` detects it via `gh pr view`), and ARCHIVE runs.

## Open questions
1. **requirements.md contradiction:** "every push is David's" vs "git agent … raise the PR". Recommend E1 and rewording step 5 to "git agent prepares; David pushes and opens a draft PR".
2. **requirements.md order:** PR (step 5) comes before QA/HITL (step 6). Recommend a *draft* PR at step 5, ready after sign-off, plus the stated rule "any commit after integration re-runs integration".
3. **requirements.md:** worktree path `../wt/F1-sell` and branch `feature/F1-sell`. Recommend `.claude/worktrees/<plan>-<node>` and `feature/<plan>/<node>` (stable ids, per planning). Also add "agents enter the worktree via `EnterWorktree`; `isolation: worktree` is not used" to §3.
4. Spike R1a–c before F0 work starts. If workflow agents don't inherit the worktree, A1/A3 are ruled out on that alone.
5. F0 bootstrap: F0 builds the hooks that witness the loop. Does F0 run its own loop, witnessed only by git and commit trailers, with hook receipts starting at F1? Recommendation: yes, and say so in the F0 trail. Pretending otherwise would be the exact "empty trace reads as nothing happened" failure.
6. Is a `git` *agent* worth it, versus the orchestrator running these git commands itself? Its value is a narrow tool allowlist and an `agent_type` in the trail. That belongs to agents-skills.
7. Postman in HITL: does the orchestrator run the Postman CLI collection before handing to David (a cheap receipt), or is it David-only? This links to §6.1.
