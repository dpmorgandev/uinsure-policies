# guardrails.spec.md
Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Settles: requirements.md §4 row 7 (each §3 Rule → its enforcing mechanism, git hooks, secrets)
Environment checked: Claude Code 2.1.302 · Node v22.18.0 · Windows PowerShell 5.1 (no `pwsh`) · Git Bash (git 2.49.0, **no `jq`**) · .NET SDK 10.0.402 · gh 2.95.0 · no `gitleaks` · user settings `defaultMode: "auto"` + `skipDangerousModePermissionPrompt: true`

Principle: **every law needs a mechanical leg.** A rule that lives only in an agent prompt is a wish. A guard registered nowhere is inert (agentkinOS audit: 46–82% of declared machinery unwired).

## Dependencies

Researched in parallel, **not decided**. Assumptions made here (re-check when they settle):

| Spec | Assumption this spec makes | If it changes |
|---|---|---|
| agents-skills | Roster names (`agent_type` values): `tester`, `builder`, `reviewer`, `security`, `git`. Defined in `.claude/agents/*.md` (project scope, not plugin; plugin agents ignore `hooks`/`permissionMode`) | Update the role table in `guard-agent.mjs` |
| orchestration | Worktrees at **`.claude/worktrees/<plan>-<node>`** (both sibling specs lean this way, vs requirements.md's `../wt/`); main session `EnterWorktree`s, agents inherit cwd. Verdict JSON returned as reviewer/security final message | Path-normalisation in the guard must handle `../wt/` too (it will — it resolves relative to the worktree root, not the session root) |
| orchestration | Workflow `agent()` can pass `agentType: 'builder'` (verified in workflow-authoring reference), so per-agent hooks work for workflow agents too | If plain subagents are used instead: same mechanism, no change |
| traceability | Verdicts land at `docs/trace/<plan>/<node>/verdicts/A<n>-<reviewer|security>.json`, written **mechanically** by a SubagentStop hook | PR gate reads wherever the verdict actually lands |
| testing | Postman CLI in pre-push is undecided (§6.1). Red-step commits must still **compile** (pre-commit builds) | If Postman goes in, pre-push gains one step |
| context | SessionStart hook belongs to context.spec; it is listed in the manifest here only so "declared vs wired" covers it | — |

## Research

### R1. Hook input identifies the agent — verified (docs + changelog)
- Common input fields include `agent_id` ("Unique identifier for the subagent…") and `agent_type` ("Agent name… Present when the session uses `--agent` or the hook fires inside a subagent").
- "When a subagent calls a tool, tool events such as `PreToolUse` and `PostToolUse` fire the same configured hooks as in the main conversation, and the input carries the `agent_id` and `agent_type`." Changelog: "Added `agent_id` (for subagents) and `agent_type`… to hook events."
- **Absent in the main thread** (inferred: docs say "present when…", not always). So "no `agent_type`" = orchestrator/main session.
- `permission_mode` is also in the input (`"default"`, `"auto"`, `"bypassPermissions"`, …) — a hook can see the mode it's running under.
- **Not yet observed on this machine.** F0 must include a probe (a logging hook) confirming `agent_type` arrives for a custom subagent *and* for a workflow `agent({agentType})`.

### R2. Permission rules are session-global, not per-agent
- Settings `permissions.deny/ask/allow` apply "to the main conversation and to subagents". There is **no per-agent permissions block**.
- Agent frontmatter `tools`/`disallowedTools` work at **whole-tool** granularity: "An entry with a specifier, such as `Bash(git push *)`, still removes the whole tool." So frontmatter can make reviewer read-only (no Edit/Write), but **cannot** say "builder may Edit `src/` but not `tests/`".
- Consequence: "builder can't edit tests / tester can't edit src" **must** be a PreToolUse hook keyed on `agent_type` (global hook) or a frontmatter `hooks:` block in each agent file (scoped to that subagent's lifetime; needs workspace trust; `Stop` there becomes `SubagentStop`).
- Frontmatter `permissionMode` is **ignored** when the parent is in `bypassPermissions`, `acceptEdits` or `auto`: "the subagent runs in that same mode". So a subagent can't be made stricter than a bypass parent by mode — only by tools, deny rules and hooks.

### R3. What survives `bypassPermissions` (and `auto`, David's actual default) — verified in docs
| Mechanism | bypass | auto | Source |
|---|---|---|---|
| `permissions.deny` | **Holds** | Holds | "Deny rules block in every mode, including `bypassPermissions`." |
| `permissions.ask` | **Still prompts** | Still prompts | "Actions no mode auto-approves… Tools matched by an explicit ask rule". Changelog fixed compound commands bypassing ask rules in bypass |
| `permissions.allow` | No effect | Resolves | "Allow rules have no effect in `bypassPermissions`." |
| PreToolUse hook `deny` / exit 2 | **Holds** (hooks run before the permission flow in every mode; `permission_mode` is an input value) | Holds | hooks + permissions pages |
| PreToolUse hook `ask` | **Unverified** in bypass (docs confirm it forces a prompt in auto) | Prompts | → F0 probe |
| Protected paths (`.git`, `.claude`, `.husky`…) | **NOT protected** — "Allowed" | Classifier | permission-modes "Protected paths" table |
| Frontmatter `tools`/`disallowedTools` | Holds (tool not in context) | Holds | sub-agents |
| Stop / SubagentStop `decision: block` | Holds (not a permission) | Holds | hooks |
| Git hooks | Hold (outside Claude), **unless** `--no-verify` / `core.hooksPath` change | Hold | git |

**Key findings:**
1. **Bypass mode opens `.claude/` and `.git/` to writes.** An agent could edit `.claude/settings.json`, `.claude/hooks/*` or `.git/hooks/*` and disarm every guard. Only explicit `deny` rules + hooks close this.
2. **`ask` rules are the right tool for "every push is David's action"**: they prompt David even in bypass, and a background subagent's prompt "surfaces in your main session and names the subagent that is asking".
3. **Hook timeout fails open**: "A timed-out command hook doesn't block the tool call… don't count on a stalled hook to act as a gate." Hooks must be fast and must catch their own errors and emit `deny` (fail closed) rather than crash/hang.
4. `disableBypassPermissionsMode: "disable"` works from any scope ("a user can set it in their own settings to lock themselves out"). Option, not a recommendation — David's call.

### R4. Bash rules are a floor, not a boundary
- "A deny or ask rule covers the invocation Claude usually produces and isn't a security boundary around the program": `Bash(git push *)` doesn't catch `sh -c 'git push'` or `/usr/bin/git push`.
- Read/Edit deny rules **do** cover `cat`/`head`/`sed`/`tee` and `>`/`<` redirect targets, but **not** "a Python or Node script that opens files itself".
- Hook `if` field: "best-effort… use the permission system rather than a hook to enforce a hard allow or deny". When the parser can't tell, the hook **runs anyway** (over-matches, safe). Use `matcher` (tool name, exact string / `|` list) for routing; do the real check inside the script, not in `if`.
- Therefore every Bash-text rule gets a **second, non-textual leg**: git hooks (server of truth for commits/pushes) and diff-scope checks at SubagentStop (what actually changed, however it was written).

### R5. Path rule anchoring — the worktree trap
- `/path` anchors at the *primary working directory*; relative `path` at cwd; `//path` absolute; Windows paths normalise to `/c/...`; `//**/.env` matches on every drive.
- "In a worktree session, a shared rule such as `Edit(/src/**)` matches that worktree's own `src/`" — but behaviour after a mid-session `EnterWorktree` (vs `claude --worktree`) is **unverified**.
- Secret deny rules should therefore use **`//**/`** anchors so they hold in any checkout.
- **Trap (the enforce-boundaries lesson, generalised):** worktrees live under `.claude/worktrees/`. A naive `Edit(//**/.claude/**)` deny to protect the harness would **block every write in every worktree**. Harness protection must name files (`.claude/settings*.json`, `.claude/hooks/**`, `.claude/agents/**`, `.claude/skills/**`, `.claude/workflows/**`), never `.claude/**`. Same for hook path logic: resolve the path **relative to its worktree root** (strip `…/.claude/worktrees/<name>/`, or `git rev-parse --show-toplevel`), then test the first segment (`src/`, `tests/`). agentflow's `enforce-boundaries.sh` jailed to `$CLAUDE_PROJECT_DIR` — exactly the shape that blocks legit writes once work moves to a sibling `../wt/`.

### R6. Stop / SubagentStop
- Both can block (`decision: "block"` + `reason`, or exit 2). Input has `stop_hook_active` (true when already continuing because of a stop hook) — "check this value… to avoid blocking on a condition that will never resolve".
- SubagentStop input: `agent_id`, `agent_type`, `agent_transcript_path`, `last_assistant_message`. Matcher = agent type. So per-role exit gates are native: e.g. `builder` can't stop while `dotnet test` is red; `reviewer` can't stop until its final message parses as verdict JSON (orchestration R-note), and the hook writes the verdict file — a **mechanical receipt**, not self-report.
- Proven on the deleted scaffold: a Stop hook refusing to finish while `dotnet build -warnaserror` fails.
- Avoid commit-on-stop (requirements §5).

### R7. GitHub server-side backstop — verified: **not available now**
- `gh api repos/dpmorgandev/uinsure-policies-api/branches/main/protection` → **403 "Upgrade to GitHub Pro or make this repository public to enable this feature."** Rulesets endpoint: same 403.
- So while private: **no branch protection, no rulesets**. They become available the moment the repo goes public (submission) — too late to guard the build.
- The gh CLI is authenticated as David with **admin** (`permissions.admin: true`), so any agent with Bash can, by default, `gh repo edit --visibility public`, `gh pr merge`, `gh api -X PATCH`. Only client-side mechanisms stand in the way.
- Optional server-side leg: a **fine-grained PAT** for agent sessions (`GH_TOKEN` env) scoped to this repo with Contents RW + Pull requests RW and **no Administration** → changing visibility becomes server-impossible for agents. It does **not** stop a merge (merge needs only Contents/PR write).

### R8. Hook script language — measured on this machine
| Option | Cold start (measured) | Notes |
|---|---|---|
| **Node `.mjs`** | **0.08 s** | JSON native; agentkinOS's proven shape + `node:test` tests; Node already required by Postman CLI? (not necessarily) |
| PowerShell 5.1 | 0.36 s | No install; `ConvertFrom-Json`; 5.1 quirks (no `&&`, encoding); `"shell": "powershell"` field supported |
| Bash (Git Bash) | fast | **No `jq`** here → needs Python/regex JSON parsing (agentflow did fallbacks); Windows backslash paths; CRLF risk |
| C# file-based app (`dotnet run hook.cs`, .NET 10) | **93 s first run**, 0.32 s cached | On-stack and interview-friendly, but first-run compile would hit every fresh clone/worktree and a cache miss ≈ timeout → fail-open risk |

Git hooks are a separate question: git on Windows runs them in Git Bash's `sh`, and they only call `dotnet build/test` — **plain POSIX `sh`, no JSON**, LF via `.gitattributes` (`*.sh` already LF; hooks have no extension → add `.githooks/* text eol=lf`).

### R9. Prior art
- **agentkinOS** `tool-allowlist.mjs`: per-agent deny-by-default keyed on `AGENTKINOS_AGENT` **env var** — Claude Code never sets that; `agent_type` in the hook input is the native replacement. Its "no identity → main session → out of jurisdiction → allow" posture is the right one.
- agentkinOS `enforce-boundaries.mjs`: Bash write-target extraction (redirects, `cp/mv/tee/dd/…`) — worth porting as the Bash leg of the per-agent path rule, honestly labelled a floor.
- agentkinOS `detect-loop.mjs`: denies 3rd identical call; fails open on corrupt state. `guards-manifest.json` + `guards-check.mjs` (121 lines): bidirectional declared↔wired check, `failMode` per guard, run in CI.
- agentflow: `.sh` hooks needing jq/python fallbacks; `commit-on-stop.mjs` (rejected); `verify-gate.mjs` (evidence-read gate) — the shape of "no PASS → no PR".

## Options

**O1. Per-agent path rules (builder↛tests, tester↛src)**
- A. One global `guard-agent.mjs` PreToolUse hook (matcher `Edit|Write|NotebookEdit|Bash`), role table keyed on `agent_type`, **allowlist** per role.
- B. A `hooks:` block in each agent's frontmatter calling a shared script with the role as an argument.
- C. Prompt only (rejected: no mechanical leg).

A is one file and one test suite, visible in `settings.json`. B co-locates the rule with the agent but scatters wiring, and needs workspace trust.

**O2. Second leg for path rules**: A. SubagentStop diff-scope check (`git diff --name-only <start-sha>` + untracked, against the role's allowlist; start SHA recorded by a SubagentStart hook per `agent_id`) · B. none.

**O3. Reviewer/security read-only**: A. frontmatter `tools: Read, Grep, Glob` (no Bash, no Edit/Write); test evidence handed to them by the orchestrator · B. allow Bash for `dotnet test`, plus a hook denying write-ish Bash · C. allow Bash with no hook.

**O4. Push / PR / merge / visibility**
- A. `ask` on `git push` and `gh pr create` (David approves each, even in bypass). `deny` on merge, visibility, `gh api`, force-push and `--no-verify`. `guard-bash.mjs` catches wrapped forms. pre-push refuses `refs/heads/main`.
- B. `deny` on all pushes; David pushes by hand.
- C. Add A plus the fine-grained PAT (R7).

**O5. PR gate**: A. `guard-bash.mjs` on `gh pr create` denies unless the latest reviewer **and** security verdict files for the current node are `PASS` and newer than HEAD's commit · B. orchestrator prompt only.

**O6. Build gate**: A. Stop hook running `dotnet build -warnaserror` **in the active worktree** (cwd), capped by `stop_hook_active` + 3-block counter · B. plus SubagentStop for `builder` running `dotnet test` (green required), `tester` running `dotnet build` (red tests must compile).

**O7. Harness tamper protection**: A. `deny` Edit/Write on named harness files (R5) for everyone; David edits the harness himself or flips the rule for F0 · B. hook-only: deny for subagents, allow main thread.

**O8. Hook language**: A. Node `.mjs` · B. PowerShell · C. bash · D. C# file-based app.

**O9. Guards manifest**: A. full agentkinOS manifest + `guards-check.mjs` · B. **the Contract table below is the manifest**, plus one `node:test` file that (i) asserts every script in `.claude/hooks/` is referenced in `settings.json` and vice versa, (ii) feeds fixture payloads to each guard and asserts allow/deny · C. none.

**O10. Loop / repetition breaker**: A. port `detect-loop.mjs` · B. no generic breaker; rely on the retry ladder (orchestration) + Stop-hook block cap · C. a dispatch counter hook (PreToolUse `Agent`) denying a 6th tester/builder dispatch per node.

## Recommendation

- **O1-A + O2-A.** A global hook keyed on `agent_type` with per-role **allowlists**:
  - builder: `src/**`, `*.sln[x]`, `Directory.*.props`
  - tester: `tests/**`
  - git: nothing via Edit/Write
  - reviewer, security: nothing
  - unknown subagent type: deny writes
  - main thread: allowed (out of jurisdiction)

  Paths resolve relative to the worktree root (R5). The SubagentStop diff-scope check catches what the text-level hook can't (interpreter writes, `sh -c`), so the rule rests on what git says changed. Two independent legs, one cheap.
- **O3-A.** Reviewer/security get no Bash. Read-only by construction is stronger than read-only by hook. The orchestrator passes them the test output, so they don't run tests themselves. *Trade-off:* the reviewer can't run tests independently, so the fresh-context evaluator trusts a run it didn't make. Mitigation: the git agent's full-suite run happens before the PR regardless.
- **O4-A** now; **C (PAT)** if David wants a server-side leg against "make public". It costs 5 minutes.
- **O5-A.** The verdict files are hook-written receipts (R6), so the gate reads mechanical evidence.
- **O6-A+B.** Build on Stop is proven. The builder-green and tester-compiles exits are cheap, and they make "green" a fact rather than a claim.
- **O7-A**, with the F0 caveat: the harness *is* F0's deliverable. F0 runs with the harness-deny rules written **last**, as F0's final node, so they guard F1 onwards. Main-thread edits to the harness after F0 go through David.
- **O8-A Node `.mjs`** for Claude hooks: 80 ms, JSON native, proven, testable with `node:test` and no npm deps. Use plain `sh` for git hooks. PowerShell is the fallback, but it's 4–5× slower per call on every Edit. C# is tempting on-stack but its 93 s cold compile means fail-open by timeout. *Public-repo framing:* `.claude/` is tooling, like `.github/`. One sentence in the README covers it.
- **O9-B.** At ~6 guards a separate manifest is a second source of truth to drift. The Contract table plus a self-test that checks wiring keeps the "a guard registered nowhere is inert" lesson without the machinery.
- **O10-B (+ C if orchestration wants it).** A generic 3-identical-calls breaker earns nothing at this size and has a corrupt-state fail-open. The real loop risk is the stop-hook loop (capped) and the feature retry ladder, which belongs to orchestration. A dispatch counter is the mechanical leg of "attempt 5 escalates", but only if orchestration doesn't already enforce it in a workflow script.

## Decision — DECIDED 2026-10-17 (David)

- [x] **D1, D2, D3, D7, D8, D9, D10:** accepted as recommended, in a block.
  - D1, D2: a global `agent_type` path hook, plus a SubagentStop diff-scope check.
  - D3: reviewer and security get only Read, Grep and Glob, and the orchestrator runs builds and tests.
  - D7: Node `.mjs` for Claude hooks and `sh` for git hooks.
  - D8: a manifest table plus a wiring self-test.
  - D9: no generic loop breaker beyond the retry ladder.
  - D10: bypass mode stays allowed.
- [x] **D4 → ask.** The git agent runs push, PR creation and PR comments. `ask` rules on `git push *`, `gh pr create *` and `gh pr comment *` mean David approves each one. Pushing to `main` is denied.
- [x] **D5 → deny rules only, with no separate PAT.** For everyone, and holding in bypass mode, deny:
  - `gh repo edit|delete|create *`, raw `gh api *` and `gh auth *`
  - `git push --force*` and `git push * main`
- [x] **D6 → the harness lock.**
  - A PreToolUse hook **denies any subagent** (any `agent_type` set) from writing harness paths anywhere, *including the copies inside worktrees*: `**/.claude/{agents,skills,hooks,specs,workflows}/**`, `**/.claude/settings*.json`, `**/.githooks/**`, `.git/**` and `CLAUDE.md`.
  - The **main session** may edit them, but an `ask` rule prompts David each time.
  - `.claude/worktrees/**` is exempt for everything outside those harness paths, and `docs/` is unaffected.
  - Live from the end of F0a.
- [x] **D11:** the main session commits docs, specs and harness work directly to `main` until F0b starts. From F0b, a hook denies `git commit` on `main`, so everything goes through branch + PR.
- [x] **D12:** Postman isn't in any hook, so it's manual only (testing O8).
- [x] **Added (David): OWASP for the harness.** Write a mapping of the OWASP Top 10 for LLM Applications 2025 to our guards, and the agentic-applications list too once its version is verified. It must cover at least:
  - **LLM01 prompt injection:** PR comments (only `dpmorgandev` is acted on), repo text and tool output.
  - **LLM06 excessive agency:** deny and ask rules, and the harness lock.
  - **LLM02 sensitive information disclosure:** the privacy check before snapshots or docs reach a public repo.
  - **Supply chain:** no third-party skills or plugins.

## Contract — PENDING (DRAFT)

Files: `.claude/settings.json` (permissions + hook wiring) · `.claude/hooks/guard-agent.mjs` · `guard-bash.mjs` · `gate-stop.mjs` · `gate-subagent.mjs` · `.claude/hooks/__tests__/guards.test.mjs` · `.githooks/pre-commit` · `.githooks/pre-push` (activated with `git config core.hooksPath .githooks`; worktrees share the repo config). All hooks: read stdin JSON, **catch every error → emit deny/block (fail closed)**, finish < 2 s except the build gates (`timeout: 300`).

| # | Rule (§3 / spec) | Mechanism(s) | File | Fail mode | How it's tested |
|---|---|---|---|---|---|
| G1 | Builder cannot edit tests | (a) PreToolUse `Edit\|Write\|NotebookEdit\|Bash` → `agent_type=builder` allowlist; Bash leg parses write targets (floor) (b) SubagentStop `builder`: diff since start-SHA ∩ `tests/**` ≠ ∅ → block | guard-agent.mjs, gate-subagent.mjs | closed (unparseable/unknown → deny) | fixture: builder Edit `tests/X.cs` → deny; `src/X.cs` → allow; worktree path `…/.claude/worktrees/build-F1/tests/…` → deny; F0 live probe |
| G2 | Tester cannot edit `src/` | same, mirrored allowlist `tests/**` | same | closed | mirrored fixtures |
| G3 | Reviewer & security read-only | frontmatter `tools: Read, Grep, Glob`; G1 hook denies any write for these types (belt) | `.claude/agents/{reviewer,security}.md`, guard-agent.mjs | closed | self-test parses frontmatter: no Edit/Write/Bash; fixture write → deny |
| G4 | Verdict is a receipt | SubagentStop `reviewer\|security`: `last_assistant_message` must parse as verdict JSON else block; hook writes `docs/trace/<plan>/<node>/verdicts/…` | gate-subagent.mjs | closed (bad JSON → block, cap 3) | fixture: malformed → block; valid → file written |
| G5 | No PR without PASS | `ask` rule `Bash(gh pr create *)`; guard-bash.mjs denies `gh pr create` unless latest reviewer+security verdicts for current node = PASS and newer than HEAD | settings.json, guard-bash.mjs | closed (no verdict → deny) | fixture: FAIL verdict → deny; stale PASS → deny; fresh PASS → no decision (ask rule prompts David) |
| G6 | No push to `main` | deny `Bash(git push * main*)`, `Bash(git push --force*)`, `Bash(git push -f*)`; guard-bash.mjs catches `HEAD:main`, `sh -c`; **pre-push** rejects any ref to `refs/heads/main` | settings.json, guard-bash.mjs, .githooks/pre-push | closed | fixtures; manual `git push origin HEAD:main` in a scratch clone → rejected |
| G7 | Every push is David's | `ask` rule `Bash(git push *)` (prompts even in bypass, R3) | settings.json | n/a (prompt) | F0 probe in bypass + auto: prompt appears, names subagent |
| G8 | Never merge (David merges) | deny `Bash(gh pr merge *)`, `Bash(git merge * main*)` in worktrees; guard-bash.mjs for wrapped forms | settings.json, guard-bash.mjs | closed | fixture |
| G9 | Repo stays private | deny `Bash(gh repo edit *)`, `Bash(gh repo delete *)`, `Bash(gh api *)`, `Bash(gh auth *)`, `Bash(gh secret *)`; guard-bash.mjs `visibility`; optional PAT (D5) | settings.json, guard-bash.mjs | closed | fixture; with PAT: `gh repo edit --visibility public` → 403 |
| G10 | Build gate on Stop | Stop: if `stop_hook_active` & 3 consecutive blocks → allow with loud `BUILD RED` reason; else `dotnet build -warnaserror` in cwd (worktree) → fail = block | gate-stop.mjs | **open after cap** (documented: avoids infinite loop) | fixture with a broken csproj in temp dir; cap counter test |
| G11 | Builder exits green / tester exits compiling | SubagentStop `builder`: `dotnet test` green; `tester`: `dotnet build` ok (+ new test fails, if testing.spec wants an anti-oracle check) | gate-subagent.mjs | closed (cap 3, then escalate message) | fixture repos (tiny sln) |
| G12 | Secrets never read/written | deny `Read`/`Edit` `//**/.env`, `//**/.env.*`, `//**/*.pfx`, `//**/*.pem`, `//**/*.key`, `//**/appsettings.*.local.json`, `//c/Users/*/AppData/Roaming/Microsoft/UserSecrets/**`; `.gitignore` same; pre-commit regex scan of staged diff (`Password=`, `AccountKey=`, `-----BEGIN .*PRIVATE KEY`, `ghp_`, `github_pat_`) | settings.json, .gitignore, .githooks/pre-commit | closed | self-test: `cat .env` denied (Bash file-command coverage, R4); staged fake key → commit rejected |
| G13 | Harness can't be disarmed by agents | deny Edit/Write `/.claude/settings.json`, `/.claude/settings.local.json`, `//**/.claude/hooks/**`, `//**/.claude/agents/**`, `//**/.githooks/**`, `//**/.git/hooks/**`, `//**/.git/config` — **never** `.claude/**` (worktrees live there); deny `Bash(git * --no-verify*)`, `Bash(git commit -n*)`, `Bash(git config *)`, `Bash(git -c *)` | settings.json, guard-bash.mjs | closed | fixture: Edit `.claude/worktrees/x/src/A.cs` → **allowed** (regression for the enforce-boundaries lesson); Edit `.claude/hooks/a.mjs` → deny |
| G14 | pre-commit: build | `dotnet build -warnaserror` (solution at worktree root) | .githooks/pre-commit | closed (non-zero exit aborts commit) | commit with a compile error → rejected |
| G15 | pre-push: tests | `dotnet test` (unit + integration + architecture); refuse `refs/heads/main` (G6); Postman per D12 | .githooks/pre-push | closed | push with a red test → rejected |
| G16 | Declared = wired | `guards.test.mjs`: every `.claude/hooks/*.mjs` referenced in settings.json and vice versa; every Contract row's fixture passes; run in pre-push | __tests__/guards.test.mjs | closed | itself; run once with a hook unwired → fails |
| G17 | Hooks active on clone | `core.hooksPath` set by the F0 setup step and checked by `/orient` (context.spec); a guard that isn't installed is inert | setup note, /orient | — | `/orient` reports `core.hooksPath` |

Out of contract (deliberately): a generic detect-loop (O10-B); `disableBypassPermissionsMode` (D10); sandboxing (not on Windows native: macOS/Linux/WSL2 only).

## Open questions

1. **`agent_type` in practice** (R1): confirm with the F0 probe that it arrives for project subagents **and** for workflow `agent({agentType})`. If it doesn't for workflows, G1/G2's first leg falls back to frontmatter hooks (O1-B); the diff-scope leg is unaffected.
2. **Hook `ask` in bypass** (R3): undocumented. G5 relies on the `ask` *rule*, not hook `ask`, for exactly this reason. Probe it anyway.
3. **Which `settings.json` governs after `EnterWorktree`?** The main checkout's copy (desirable: a feature branch can't change its own guards) or the worktree's? Probe in F0. If it's the worktree's, G13 matters even more.
4. **Contradiction for requirements.md §3:** "every push is David's action" vs feature loop step 5 "git agent… raises the PR" (which needs a push). The rec. (G7 `ask`) reconciles them: the agent *proposes* the push and David *approves* it. Reword §3 to "Every push and PR needs David's approval (ask rule); merging and making the repo public are David's alone."
5. **requirements.md §3 should add:** "Guards hold in bypass and auto mode: deny rules, ask rules, hooks and git hooks. Allow rules and protected-path prompts don't." Also "no branch protection while private (GitHub Free, verified 403)".
6. Builder allowlist beyond `src/**`: does F0's builder need root files (`.slnx`, `Directory.Build.props`, `global.json`, `.editorconfig`)? Who creates the test projects' csproj (tester, under `tests/`)?
7. Does the main thread (orchestrator) write `docs/plans`, `docs/trace`, `docs/context` in the worktree or in David's checkout? requirements says agents never touch David's checkout. G-rules leave the main thread unrestricted, so this is orchestration/traceability's call.
8. Stop-gate cost: `dotnet build` on every main-session Stop (including the analysis and chat turns) costs ~5–15 s each. Should it skip when `git status` shows no `.cs`/`.csproj` changes? (rec. yes)
