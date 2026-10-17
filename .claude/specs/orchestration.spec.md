# orchestration.spec.md
Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Input: `docs/requirements.md` §3 (Feature loop, Planning model, Rules), §5, §6 items 3, 5, 7
Environment checked: Claude Code 2.1.302, git 2.49.0, gh 2.95.0, Windows 11, remote `origin` already set (github.com/dpmorgandev/uinsure-policies-api)

Scope: the feature loop as a state machine. It runs from picking a node to archiving it. It also covers the workflow-vs-skill choice, the verdict schema, the bug-report handoff, where loop state lives, the HITL mechanics, and the branch, commit and PR conventions.

## Dependencies

| Spec | What this spec assumes (not decided there yet) | If the assumption breaks |
|---|---|---|
| `planning` | Plan YAML holds node `status`/`attempts`/`branch`/`pr`, written only as feature-branch commits (its option A2). Node ids `F0a, F0b, F1…`, `N1…`. Attempts are counted **per node**, one per FAIL cycle. Attempt 3 is `/plan-investigation`, and it can recommend `amend-node` | The state section moves: the trace stays the receipt either way |
| `agents-skills` | Roster (six, per its D1): `git`, `tester`, `builder`, `reviewer`, `security`, `investigator`. The orchestrator is the main session running a skill, **not** an agent. Each agent has a `.claude/agents/<name>.md` so that `agent_type` in hooks is its name | Without named agent types, hook-side identity is lost (see traceability) |
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
  - Entering "takes … project configuration such as `CLAUDE.md` and settings to that location". So the harness in force is the **branch's** copy, but the hook *scripts* resolve via `CLAUDE_PROJECT_DIR` to main's copy. **Corrected by the F0a spikes (t3, t4):** the opposite holds. The main checkout's settings, hook scripts, agents and `CLAUDE.md` govern inside the worktree, and the worktree copies are inert. The bootstrap hazard below therefore doesn't arise: a hook added on a branch isn't registered at all until it's in the main checkout's files.
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
- [x] **Final rulings (David, 2026-10-17):**
  - OQ7: a Postman receipt before PAUSE 2. The orchestrator runs the node's real-time-runnable Postman folder with the Postman CLI against the worktree API on port 5080 and records pass/fail in `loop.jsonl`. David still steps through manually, and Postman stays out of every git hook. It depends on an F0a check that the CLI runs a local collection without login; if that fails, Postman is David-only.
  - OQ8: only reviewer, security or gate FAILs count as attempts. David's steers at PAUSE 1, PAUSE 2 and in PR comments don't increment the attempt; they still go investigate → red → fix, as findings `D1`, `D2`… in `findings/D<n>.md`.
  - Accepted as proposed: the stub-controller commit carries `TDD: stub`, and the PR isn't raised while David's paragraph is empty.
- [x] **F0a spike rulings (David, 2026-10-17)**, recorded in full in guardrails' Decision:
  - B1 + C1 stand, and `EnterWorktree` carries subagents (t3).
  - The harness in force is the main checkout's: settings and agents reload live from its working tree, and `CLAUDE.md` is read at session start. So `/feature` runs from a session launched in the main checkout.
  - The verdict gate's block reason asks for the complete verdict JSON to be restated.
  - The Postman receipt is viable: the CLI runs a local collection without login, and exits non-zero on a failing assertion (t6).
- [x] **Rulings (David, 2026-10-17, F0a t11):**
  - A plan-status commit whose diff is only `docs/plans/**` (such as `hitl → done` after sign-off) doesn't invalidate `gate.integrate`, for the same reason `chore(trace)` doesn't.
  - DAG node states are planning's (`in_progress`, `paused_acceptance`, `hitl`, `investigating`, `escalated`, `done`, `cut`). This table's earlier `in-progress`/`in-review` are aligned to them.
  - F5–F7 `builder / write` tasks are the builder's, with its write scope widened by node kind (guardrails G1).

## Contract

Binding for `/feature`. The skill (`.claude/skills/feature/SKILL.md`) restates this section as executable steps; agents never read this spec.

### Invocation and identifiers
- `/feature <plan-id> <node-id>`, e.g. `/feature build F1`. It runs in the **main session** (the orchestrator, not an agent). The node must be `ready` in `docs/plans/active/<plan-id>.dag.yaml` (all `depends_on` merged), else it refuses. The orchestrator walks the node's explicit `tasks` in order (planning.spec read algorithm), and each state below maps to one or more of those tasks.
- Branch `feature/<plan-id>/<node-id>` (or `bug/<plan-id>/<node-id>` for a `bug-NNN` plan). Worktree `.claude/worktrees/<plan-id>-<node-id>`. Trail dir `docs/trace/<plan-id>/<node-id>/` (layout owned by traceability).
- F0a is built by David in-session and does **not** run this loop; F0b is the first `/feature` run.
- `/feature` runs in a session **launched in the main checkout**, because the harness in force is the main checkout's (F0a spike rulings). Settings and agents reload live from its working tree; a `CLAUDE.md` change needs a new session.

### State machine (one node)
Every step ends in its gate. The orchestrator runs each gate itself via Bash and appends the result to `loop.jsonl`. **A gate with no receipt is BLOCKED, never PASS.** A failed gate on steps 2–6 goes to step 8 (FAIL route).

| # | State | Agent | Action | Writes | Gate (orchestrator, outside the producer's turn) |
|---|---|---|---|---|---|
| 1 | SETUP | git | `git fetch origin`; `git worktree add --no-track .claude/worktrees/<p>-<n> -b <branch> origin/main` (the branch never tracks `main`). Orchestrator then calls `EnterWorktree({path})` and commits plan status `pending → in_progress`, `attempt: 1` | worktree, branch, `plan(<p>/<n>): ready → in-progress` commit | branch name and worktree path match; `git rev-parse --show-toplevel` = worktree; plan commit present |
| 1b | SCAFFOLD (optional) | builder | Only a node that adds projects: the `.slnx`, root build files and `src/*/*.csproj`, no `.cs` | `build(<node>): scaffold` commit, `TDD: scaffold` | each new `src/` project builds; diff ⊆ root build files + `src/*/*.csproj`; no `.cs` in the diff; the `.slnx` lists the node's projects |
| 2 | RED | tester | Full acceptance suite for every node rule (each success and failure row), unit tests per rule, Postman requests; `src/` signatures whose bodies are only `throw new NotImplementedException()` | `test(<rule>): red: …` commits, `TDD: red` | build passes; rule-filtered tests **fail**; diff ⊆ `tests/**` + throw-only `src/` signatures (hook); every node rule has a `[Trait("Rule",…)]` test |
| 3 | STUB | builder | Stub controller: real routes and contracts, canned responses | `feat(<node>): stub controller` commit, `TDD: stub` | build passes; app starts; routes answer (smoke); acceptance suite still red; diff ⊆ `src/Uinsure.Policies.Api/**` |
| 4 | **PAUSE 1** | — (David) | Orchestrator prints the HITL block (below). David calls the stub in Postman and reads the tests. Replies `go` or `change: …` | `pause1` event | David's `go`. `change:` → a **steer** `D<n>` through step 8 with `source: david` (no attempt++) |
| 5 | GREEN | builder | One dispatch per layer: domain → services → persistence → api; then optional refactor | `feat(<rule>): green: <layer> …` per layer, `refactor(<rule>): …`; `TDD: green\|refactor` | after each layer: build passes and that layer's rule tests pass; diff ⊆ `src/**`; after api: acceptance suite green |
| 6 | GATES | orchestrator | `dotnet build -warnaserror`; full `dotnet test`; rule-coverage check; diff-scope check (`git diff --name-only origin/main...HEAD` vs the node's allowed paths); no `NotImplementedException` left in `src/` | `gate.*` events | all exit 0 |
| 7 | REVIEW | reviewer ∥ security | Two `Agent` calls in one message; neither sees the other's verdict. Inputs: plan/node, rule ids, `base_sha..head_sha`, the gate receipts | verdict JSON (SubagentStop hook validates and writes `verdicts/<A<n>\|D<n>>-<agent>.json`, traceability) | both verdicts present, schema-valid, `head_sha`/`diff_fingerprint` = current; both PASS → 9, any FAIL → 8 |
| 8 | FAIL route | investigator → tester → builder | On a reviewer, security or gate FAIL: attempt++ (plan commit), findings `findings/A<n>.md`. On a David **steer** (PAUSE 1 `change:`, PAUSE 2 `defect:`, a PR comment): **no attempt++**, the next steer id `D<n>`, findings `findings/D<n>.md`, and a `steer` event. Investigator returns findings JSON (rendered to that file). Tester writes one red test per testable finding (`Fixes-Finding:` trailer), or records the untestable reason. Builder goes green with the findings + red test. Then back to 6 | `findings/A<n>.md` or `findings/D<n>.md`, `test(<rule>): red: fix <finding>`, `fix(<rule>): …` | red test fails before the fix, passes after; then steps 6–7 re-run. Ladder below |
| 9 | QA | tester | QA script, steps tagged `[RULE-ID]`, sign-off block | `docs/qa/<p>-<n>.md` commit | every node rule appears in ≥1 step |
| 10 | INTEGRATE | git | `git fetch origin`; `git rebase origin/main`; full suite on the post-rebase HEAD. A conflict is returned to the orchestrator, never resolved by editing code | `gate.integrate {head_sha, test summary}` | rebase clean; full suite exit 0 on HEAD. **Any later commit invalidates it**, except a `chore(trace)` commit (diff ⊆ `docs/trace/**`) or a plan-status commit (diff ⊆ `docs/plans/**`, e.g. `hitl → done`) |
| 11 | TRACE | orchestrator (render script) → git | Render `walkthrough.md` (every commit in order, one line each), node `README.md`, requirements matrix; git commits all trail files once | `chore(trace): <node> trail` | completeness check passes (fails loudly on a missing link) |
| 12 | PR | git | `git push -u origin <branch>`, `gh pr create --draft` with the body template. Each command is an `ask` prompt to David | `pr {url}` event; plan commit `in_progress → hitl` before push | PreToolUse PR gate (guardrails G14): the latest reviewer and security verdicts are PASS with `head_sha` = HEAD, plus `gate.integrate` for the current HEAD; David's "From David" paragraph is non-empty (the PR waits for it); push to `main` denied |
| 13 | VERDICTS | git | Posts three comments: reviewer verdict, security verdict, rebase receipt (`ask` on `gh pr comment`) | `pr.comment` events | three comments exist (`gh pr view --comments`) |
| 14 | **PAUSE 2** | orchestrator, then David | **Postman receipt first:** the orchestrator starts the API from the worktree on port 5080 and runs the node's real-time-runnable Postman folder with the Postman CLI (`postman collection run tests/postman/Uinsure.Policies.postman_collection.json -e tests/postman/local.postman_environment.json -i "<node folder>"`), then stops the API. Then HITL sign-off with `walkthrough.md`, the QA script and Postman, which David still steps through manually. David replies `sign-off` or `defect: …` | `gate.postman {cmd, exit, ok, head}` event; `signoff {sha}` | the `gate.postman` result is printed in the HITL block (a fail is shown, not hidden; David decides). `signed_sha` = HEAD. `defect:` → a steer `D<n>` through step 8 with `source: david` (no attempt++) |
| 15 | PR COMMENTS | orchestrator → FAIL route → git | `gh pr view --comments --json comments`; only `author.login == dpmorgandev` and not yet answered. Each comment becomes a steer `D<n>` (`source: pr-comment`, comment id, no attempt++) through step 8 on the same branch; then 6–7, 10, 11 (trail re-render), git pushes (`ask`) and replies with the fix SHA | `pr.comment.fixed {comment_id, sha}` | each acted-on comment has a reply naming a SHA on the branch; new commits re-ran INTEGRATE and re-require PAUSE 2 |
| 16 | MERGE | — (David) | David marks the PR ready and merges with a merge commit (repo allows merge commits only) | `merged {sha}` (detected via `gh pr view --json state,mergeCommit`) | PR state `MERGED` |
| 17 | ARCHIVE | orchestrator, git | `ExitWorktree`; git removes the worktree (refuses if uncommitted work) and deletes the local branch after David pulls; plan status `done` and archive via `chore/archive-<id>` (planning); lesson if attempt ≥ 3, escalation or cut | `archived` event | worktree gone; plan shows `done` on `main` |

A comment on an already merged PR is not handled here: it becomes a `/plan-bug`.

### Retry ladder (attempts counted per node, one per FAIL cycle)
Only reviewer, security or gate FAILs count. David's steers (`D<n>`: PAUSE 1, PAUSE 2, PR comments) take the step 8 route but never increment `attempt`.
| Attempt | Route |
|---|---|
| 1–2 | step 8: investigate → red → fix |
| 3 | stop the loop; open `/plan-investigation` (`inv-NNN`). David approves its outcome (continue, `amend-node`, or cut); attempt 4 follows the investigation's approach |
| 4 | step 8 with the investigation's findings |
| 5 | **ESCALATED**: stop and ask David. No further automatic attempt |
| any | a FAIL finding fingerprint `(rule_id, file, sha1(what))` equal to one from the previous attempt jumps straight to attempt 3 |

### Verdict JSON schema v1 (reviewer and security, final message)
The SubagentStop hook blocks the agent from stopping until its final message validates. Its block reason names the errors and asks for the **complete** verdict JSON to be restated, because after a re-prompt only the agent's last message reaches the orchestrator (F0a spike). Aligned with agents-skills' field list, with two changes: `verdict` is `PASS|FAIL` (not `NEEDS_WORK`), and reviewer/security run no commands, so evidence is the orchestrator's gate receipts (`gate_refs`), not their own.
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object", "additionalProperties": false,
  "required": ["schema_version","agent","plan_id","node_id","attempt","base_sha","head_sha","diff_fingerprint","verdict","gate_refs","rules_covered","rules_missing","findings"],
  "properties": {
    "schema_version": { "const": 1 },
    "agent":   { "enum": ["reviewer","security"] },
    "plan_id": { "pattern": "^(build|feat-\\d{3}|bug-\\d{3})$" },
    "node_id": { "pattern": "^(F\\d+[a-z]?|N\\d+)$" },
    "attempt": { "type": "integer", "minimum": 1, "maximum": 5 },
    "base_sha": { "pattern": "^[0-9a-f]{40}$" },
    "head_sha": { "pattern": "^[0-9a-f]{40}$" },
    "diff_fingerprint": { "type": "string", "description": "git diff base...head | git patch-id --stable" },
    "verdict": { "enum": ["PASS","FAIL"] },
    "gate_refs": { "type": "array", "minItems": 1, "items": { "type": "string" }, "description": "loop.jsonl gate events read" },
    "rules_covered": { "type": "array", "items": { "type": "string" }, "description": "rule ids (reviewer) or OWASP ids e.g. API3:2023 (security) checked and met" },
    "rules_missing": { "type": "array", "items": { "type": "string" } },
    "findings": { "type": "array", "items": {
      "type": "object", "additionalProperties": false,
      "required": ["id","rule_id","severity","file","what","why","route_to"],
      "properties": {
        "id": { "pattern": "^[RS]\\d+$" },
        "rule_id": { "type": "string", "description": "requirements.md id or OWASP API Top 10 2023 id" },
        "severity": { "enum": ["P0","P1","P2"] },
        "file": { "type": "string" }, "line": { "type": "integer" },
        "what": { "type": "string", "maxLength": 200 }, "why": { "type": "string" },
        "route_to": { "enum": ["tester","builder"] } } } },
    "notes": { "type": "array", "items": { "type": "string" } }
  }
}
```
Hook-side semantic checks: `PASS` ⇒ no P0/P1 findings, `rules_missing` empty, and `rules_covered` ⊇ the node's rule ids (reviewer) or the stated OWASP checklist (security). `FAIL` ⇒ ≥ 1 finding. `head_sha` and `diff_fingerprint` must equal the hook's own computation.

**Investigator findings (schema v1, final message; read-only agent, the hook renders `findings/A<n>.md`, or `findings/D<n>.md` for a steer):**
`{schema_version:1, plan_id, node_id, attempt, source: reviewer|security|gate|david|pr-comment, source_ref (verdict finding ids, gate event, or PR comment id), findings[{id: "I<n>", rule_id, layer: domain|services|persistence|api|tests, files[], root_cause, fingerprint, testable, suggested_test (Given/When/Then + test class, when testable), untestable_reason (when not)}], previous_attempts[] (one line each, from loop.jsonl)}`. The rendered file adds a tester block and a builder block so attempt n+1 tries a different approach.

### Commits
- Subjects: `test(<rule>): red: …` · `feat(<rule>): green: …` · `feat(<node>): stub controller` · `fix(<rule>): …` · `refactor(<rule>): …` · `plan(<p>/<n>): <from> → <to>` · `chore(trace): <node> trail`.
- Trailers `Agent`, `Plan`, `Node`, `Rule`, `TDD: scaffold|red|stub|green|refactor`, optional `Fixes-Finding`. A hook checks `Agent/Plan/Node` against `agent_type` and the branch. Explicit `git add <paths>` only; never commit-on-stop.

### Branching
- **`main` is the integration branch.** There is no `develop` branch and no integration agent. Feature and bug branches are cut from `origin/main`, rebased onto `origin/main` at step 10, and merged by David with a merge commit, so TDD commits and their SHAs survive on `main` and `git log --first-parent main` shows one line per feature. David's own checkout is never touched (fetch only updates remote-tracking refs). The README states this.

### PR body template (generated at step 12; David adds one paragraph)
```markdown
## <node-id>: <node title>
Plan `<plan-id>` · rules: SELL-1, SELL-2, … · attempts: <n>

<!-- David: one paragraph in his own words; `gh pr create` waits until it's written -->

| Rule | Tests | Red commit | Green commit |
|---|---|---|---|
| SELL-2 | 6 (4 unit, 2 integration) | abc1234 | def5678 |

Tests: <passed>/<total> on <head_sha> after rebase onto origin/main <main_sha>.
Verdicts: reviewer PASS · security PASS (posted as comments below).
Walkthrough: docs/trace/<plan>/<node>/walkthrough.md · QA script: docs/qa/<plan>-<node>.md

Run it: `dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080`
_Generated from the trace by /feature; the paragraph above is David's._
```

### PR comment templates (all posted by the git agent)
- **Reviewer / security verdict:** `**<Agent> verdict: PASS|FAIL** (attempt <n>, head <short_sha>)` · checked: `<rules_covered>` · missing: `<rules_missing or none>` · findings table `id | rule | severity | file:line | what` (or "none") · `Receipt: docs/trace/<p>/<n>/verdicts/<round>-<agent>.json`.
- **Rebase receipt:** `**Rebase + full suite: PASS** · rebased onto origin/main <main_sha> · HEAD <head_sha> · <passed>/<total> tests (unit, integration, architecture) · command: <cmd> · exit 0`.
- **Fix reply (to a dpmorgandev comment):** `Fixed in <sha> (steer D<n>): <one line>. Red test: <test name> or untestable: <reason>.`

### HITL environment
- Printed by the orchestrator at both pauses: the worktree path, `dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080` (run from the worktree), Postman environment `baseUrl=http://localhost:5080`, the QA script path, and `dotnet test`.
- **Port 5080**, fixed (one feature at a time). **SQLite DB per worktree:** the relative `Data Source` resolves under the worktree's content root; `*.db` is gitignored and recreated at each pause. David may open the worktree's `Uinsure.Policies.slnx` in VS 2026.
- **Postman:** David steps through it manually at both pauses. Before PAUSE 2 the orchestrator also runs the node's real-time-runnable folder (the node's folder; requests that need a fake clock aren't in it, per api-contract) as the `gate.postman` receipt. Postman is in no git hook. **Depends on an F0a check** that the Postman CLI runs a local collection without login; if that check fails, there's no receipt (`gate.postman(skipped)`) and Postman is David-only.

### Resume across sessions
- State is **derived**, never stored as a mutable field: the last `loop.jsonl` event, reconciled with `git log origin/main..HEAD` trailers. If they disagree, **git wins**, and the missing event is backfilled with `source: reconciled`.
- `/orient` (SessionStart hook + snapshot in the branch's `docs/context/`) names the plan, node, branch, attempt and next action. `/feature <p> <n>` re-run on a node that has a worktree calls `EnterWorktree({path})` instead of SETUP and continues at the derived state.
- A pause survives a session end: re-running `/feature` at PAUSE 1/2 re-prints the HITL block and waits. A gate result from a previous session is reused only if its `head` equals the current HEAD; otherwise the gate re-runs.

## Open questions
1. ~~Push vs PR contradiction~~ **Resolved** (Decision E): the git agent runs push and `gh pr create --draft` under `ask`.
2. ~~PR before HITL~~ **Resolved**: draft PR before PAUSE 2, ready after sign-off; any later commit re-runs INTEGRATE.
3. ~~Worktree and branch names~~ **Resolved**: `.claude/worktrees/<plan>-<node>`, `feature/<plan>/<node>`; requirements.md updated in the requirements pass.
4. ~~Spike R1a–c in F0a.~~ **Resolved (F0a t3/t4):** `EnterWorktree` carries subagents, so there's no B2 fallback. (a) Workflow agents weren't tested, because workflows aren't used. (b) `EnterWorktree` doesn't switch agents or settings to the worktree's copies; they come from the main checkout, and reload live from its working tree. (c) `agent_type` is the project agent's name.
5. ~~F0 bootstrap~~ **Resolved**: F0a is built in-session with no loop, witnessed by git and trailers; F0b is the first loop run with hook receipts.
6. ~~Git agent worth it~~ **Resolved** (agents-skills D1): yes, six agents.
7. Resolved (David, 2026-10-17): before PAUSE 2 the orchestrator runs the node's real-time-runnable Postman folder via the Postman CLI against the worktree API (port 5080) and records `gate.postman` in `loop.jsonl`. David still steps through manually; Postman is in no git hook. It depends on an F0a check that the CLI runs a local collection without login, else David-only (step 14, HITL environment).
8. Resolved (David, 2026-10-17): only reviewer, security or gate FAILs count as attempts. David's steers at PAUSE 1, PAUSE 2 and in PR comments don't increment the attempt; they go investigate → red → fix as `D1`, `D2`… with `findings/D<n>.md` (steps 4, 8, 14, 15; retry ladder).
