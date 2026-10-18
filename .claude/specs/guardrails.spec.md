# guardrails.spec.md
Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Settles: requirements.md §4 row 7 (each §3 Rule → its enforcing mechanism, git hooks, secrets)
Environment checked: Claude Code 2.1.302 · Node v22.18.0 · Windows PowerShell 5.1 (no `pwsh`) · Git Bash (git 2.49.0, **no `jq`**) · .NET SDK 10.0.402 · gh 2.95.0 · no `gitleaks` · user settings `defaultMode: "auto"` + `skipDangerousModePermissionPrompt: true`

Principle: **every law needs a mechanical leg.** A rule that lives only in an agent prompt is a wish. A guard registered nowhere is inert (agentkinOS audit: 46–82% of declared machinery unwired).

## Dependencies

Researched in parallel, **not decided**. Assumptions made here (re-check when they settle):

| Spec | Assumption this spec makes | If it changes |
|---|---|---|
| agents-skills | Roster names (`agent_type` values): `tester`, `builder`, `reviewer`, `security`, `investigator`, `git`. Defined in `.claude/agents/*.md` (project scope, not plugin; plugin agents ignore `hooks`/`permissionMode`) | Update the role table in `guard-agent.mjs` |
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

### R-AI. OWASP for AI applications (checked 2026-10-17)
- **History: OWASP Top 10 for LLM Applications 2025.** Superseded by the 2026 list below; kept for the record. OWASP GenAI Security Project, dated 17 Nov 2024 on the resource page. IDs are `LLMnn:2025`:
  - LLM01 Prompt Injection
  - LLM02 Sensitive Information Disclosure
  - LLM03 Supply Chain
  - LLM04 Data and Model Poisoning
  - LLM05 Improper Output Handling
  - LLM06 Excessive Agency
  - LLM07 System Prompt Leakage
  - LLM08 Vector and Embedding Weaknesses
  - LLM09 Misinformation
  - LLM10 Unbounded Consumption

  Sources: https://genai.owasp.org/llm-top-10/ and https://genai.owasp.org/resource/owasp-top-10-for-llm-applications-2025/. The 2026 PDF's revision history lists "2024-11-18 Version 2025 Release", so the edition is labelled "2025", not "v2.0".
- **OWASP Top 10 for LLM Applications 2026** (the Contract maps this one). Resource page "OWASP GenAI LLM Top 10 2026", dated 3 Aug 2026: https://genai.owasp.org/resource/owasp-genai-llm-top-10-2026/. IDs and names verified 2026-10-17 from the official PDF (the page's Download link), whose cover reads "Version 2026"; "v1.0" appears only in an image filename. IDs are `LLMnn:2026`:
  - LLM01 Prompt Injection
  - LLM02 Sensitive Information Disclosure
  - LLM03 Excessive Agency
  - LLM04 Supply Chain
  - LLM05 Data and Model Poisoning
  - LLM06 Unbounded Consumption
  - LLM07 Misinformation
  - LLM08 Hidden Context Exposure (System Prompt Leakage, broadened)
  - LLM09 Vector and Embedding Weaknesses
  - LLM10 Improper Output Handling
- **OWASP Top 10 for Agentic Applications for 2026.** That's the exact title, dated 9 Dec 2025, at https://genai.owasp.org/resource/owasp-top-10-for-agentic-applications-for-2026/. The official PDF's cover reads "Version 2026 · December 2025". IDs are `ASInn:2026`. Names verified 2026-10-17 from that PDF's table of contents:
  - ASI01 Agent Goal Hijack
  - ASI02 Tool Misuse and Exploitation
  - ASI03 Identity and Privilege Abuse
  - ASI04 Agentic Supply Chain Vulnerabilities
  - ASI05 Unexpected Code Execution (RCE)
  - ASI06 Memory & Context Poisoning
  - ASI07 Insecure Inter-Agent Communication
  - ASI08 Cascading Failures
  - ASI09 Human-Agent Trust Exploitation
  - ASI10 Rogue Agents

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
  - A PreToolUse hook **denies any subagent** (any call with `agent_id`; see the F0a spike rulings) from writing harness paths anywhere, *including the copies inside worktrees*: `**/.claude/{agents,skills,hooks,specs,workflows}/**`, `**/.claude/settings*.json`, `**/.githooks/**`, `.git/**` and `CLAUDE.md`.
  - The **main session** may edit them, but an `ask` rule prompts David each time.
  - `.claude/worktrees/**` is exempt for everything outside those harness paths, and `docs/` is unaffected.
  - Live from the end of F0a.
- [x] **D11:** the main session commits docs, specs and harness work directly to `main` until F0b starts. From F0b, a hook denies `git commit` on `main`, so everything goes through branch + PR.
- [x] **D12:** Postman isn't in any hook, so it's manual only (testing O8).
- [x] **Added (David): OWASP for the harness.** Write a mapping of the OWASP Top 10 for LLM Applications 2026 to our guards, together with the OWASP Top 10 for Agentic Applications for 2026. It must cover at least:
  - **LLM01 prompt injection:** PR comments (only `dpmorgandev` is acted on), repo text and tool output.
  - **LLM03 excessive agency:** deny and ask rules, and the harness lock.
  - **LLM02 sensitive information disclosure:** the privacy check before snapshots or docs reach a public repo.
  - **Supply chain:** no third-party skills or plugins.
- [x] **Final rulings (David, 2026-10-17):**
  - OQ3: the builder may create the root build files (`global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt`, `.gitattributes`, the `.slnx`); the tester creates the `tests/*.csproj` files (G1).
  - OQ4: `Bash(gh pr merge *)` is denied for everyone. David always merges in the GitHub UI (G5).
  - OQ5: PR gate yes. `guard-bash.mjs` denies `gh pr create` unless the node's latest reviewer and security verdicts are PASS with `head_sha` = HEAD (G14).
  - OQ6: two privacy denylists: the committed `.claude/hooks/privacy-denylist.txt` (generic patterns only) and an optional, gitignored `.githooks/privacy-denylist.local.txt` (private terms). Both consumers read both (G10).
  - OQ7: the OWASP mapping is re-keyed to the OWASP LLM Top 10 2026 plus the Top 10 for Agentic Applications for 2026; the 2025 list stays in Research as history.
  - Accepted as proposed: hooks block `--no-verify` and hook-path overrides for everyone (G11).
- [x] **F0a spike rulings (David, 2026-10-17).** Receipts are in `docs/trace/build/F0a/spikes.md`.
  - **Harness in force.** Inside a worktree, the main checkout's `settings.json`, hook scripts (via `CLAUDE_PROJECT_DIR`), agents and `CLAUDE.md` govern, and the worktree copies are inert. Settings and agents reload live from the main checkout's working tree; `CLAUDE.md` is read at session start (corrected 2026-10-17 after agents appeared and disappeared mid-session). A branch's harness change therefore takes effect once it's in the main checkout's files; a `CLAUDE.md` change also needs a new session. An uncommitted edit in the main checkout changes a running loop at once, which is why the main session's harness edits prompt (G4). G4's lock on worktree copies stays, as belt-and-braces.
  - **Identity, split rule.**
    - A call with `agent_id` is a subagent; no `agent_id` means the main session.
    - The role is `agent_type`, which is the agent file's `name:`.
    - A subagent PreToolUse with an empty or unknown `agent_type` is **denied**.
    - A SubagentStart or SubagentStop with an empty `agent_type` (Claude Code's internal helper) is **allowed and logged**, never blocked.
    - *Why:* `agent_type` was empty on some subagent events in every spike run, so "no `agent_type` = main" would let those calls through as the main session.
  - **Stop gates restate in full.** A SubagentStop block reason asks for the **complete** final JSON to be restated. After a re-prompt, only the agent's last message reaches the caller.
  - **Spec reads.** The spec-read deny matches explicit `path` arguments and Glob patterns. `.ignore` only hides `.claude/specs/` from path-less Grep, so it's a convenience, not a guard.
  - **Builder write scope by node kind (F0a t11):** the F5–F7 `write` tasks stay with the builder. G1 widens its allowlist by node kind: `evidence` (F5) adds `infrastructure/**`; `docs` (F6, F7) adds `README.md`, `docs/readme-checklist.md` and `docs/pitch-deck/**` (generator and `pitch-deck.html`). The hook reads the node kind from the branch → plan DAG. Reviewer and security still gate them.
  - **Rulings in t13 (David, 2026-10-17):** G7 is marker-gated like G8, and `Bash(git push * main)` is dropped from the settings deny. The tester stub grammar is testing C7's. The agents-skills spec-read deny lives in `guard-agent.mjs`, whose matcher adds `Read|Grep|Glob`; for those tools it denies only `.claude/specs/` paths.
  - **Ask-rule receipts are denials.** An approved prompt can't be told apart from no prompt, and the transcript doesn't record permission decisions. Every ask-rule probe (G4, G6) is passed by David **denying** the prompt, with the tool result showing the rejection.

## Contract

These rules bind every hook. Each hook reads its stdin JSON, **catches every error and emits deny/block (fails closed)**, and finishes in under 2 s. Paths are resolved **relative to their worktree root**: strip `…/.claude/worktrees/<plan>-<node>/` or use `git rev-parse --show-toplevel`, then match (R5).

**Identity** (F0a spike rulings):
- A call with `agent_id` is a subagent, and its role is `agent_type`. No `agent_id` means the main session.
- A subagent PreToolUse whose `agent_type` is empty or not in the role table is denied.
- A SubagentStart or SubagentStop with an empty `agent_type` passes and is logged.

**Harness in force:** the main checkout's settings, hook scripts, agents and `CLAUDE.md`. Settings and agents reload live from the main checkout's working tree; `CLAUDE.md` is read at session start. Worktree copies are inert.

**Stop gates:** a SubagentStop block reason always asks for the complete final message to be restated.

### Guard manifest

This table *is* the manifest (D8). The wiring self-test (G13) checks it against `settings.json` and the hook directory.

| ID | Law | Mechanism | File | Test (fixture in `guards.test.mjs`) | F0a probe |
|---|---|---|---|---|---|
| G1 | Each agent writes only its own paths | PreToolUse `Edit\|Write\|NotebookEdit\|Bash`, keyed on `agent_type`, with an **allowlist** per role: builder `src/**` plus the root build files `*.slnx`, `global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `.gitattributes`, widened by node kind: `evidence` (F5) adds `infrastructure/**`; `docs` (F6, F7) adds `README.md`, `docs/readme-checklist.md` and `docs/pitch-deck/**` (generator and `pitch-deck.html`). The hook reads the node kind from the branch → plan DAG; tester `tests/**`, which includes creating the `tests/*/*.csproj` files (+ G2); reviewer, security, investigator, git and any unknown type get **none**. The Bash leg parses write targets (redirects, `cp/mv/tee/sed -i`) and is a floor (R4). The main session is out of jurisdiction. All agents are denied `docs/trace/**` (traceability) | `guard-agent.mjs`, `lib/roles.mjs`, `lib/paths.mjs` | builder → `tests/X.cs` deny, `src/X.cs` allow, `global.json` allow; tester → `tests/T/T.csproj` allow, `Directory.Packages.props` deny; same via `…/.claude/worktrees/build-F1/tests/X.cs` → deny; reviewer Write → deny; unknown type → deny; `agent_id` with an empty `agent_type` → deny; no `agent_id` (main session) → allow | Log one Edit from a project subagent **and** one from a workflow `agent({agentType})`. `agent_type` must arrive (OQ1) |
| G2 | Tester's `src/` writes are throwing stubs only | Tester may touch `src/**` only if every **added** line matches the stub grammar and **no line is removed**. The grammar is testing C7's, verbatim (blank, braces, `using`, `namespace`, type headers, member signatures, auto-properties, enum members, and `throw new NotImplementedException();`). Attributes and `///` docs are **not** allowed (David, F0a t13). Edit: the diff of `old_string` → `new_string` is checked. Write: only allowed for a new file. Second leg: G3 re-checks the whole `git diff -U0 -- src/` at SubagentStop | `guard-agent.mjs`, `lib/stub-rule.mjs`, `gate-subagent.mjs` | signature + throw → allow; `return 0;` → deny; `if (` → deny; deleting a line → deny; Write over an existing `src/` file → deny | Tester adds `Policy.Cancel(...)` as a throwing stub. The red commit compiles, and the test fails with `NotImplementedException` |
| G3 | What git says changed is in scope | SubagentStart records `HEAD` per `agent_id`, plus the content hash of every file already dirty (so pre-existing changes aren't blamed on the agent; F0a t13). SubagentStop compares `git diff --name-only <start>` + untracked, minus `docs/trace/**` (hook-written) and minus dirty files whose hash is unchanged, against the role's allowlist and the harness lock (G4), and reruns the stub rule for the tester. Out of scope → `decision: block` with the file list. `stop_hook_active` caps it at 3 blocks, then it stops with a loud reason so the orchestrator escalates | `gate-subagent.mjs` | builder writes `tests/A.cs` via `node -e` → block at stop; clean diff → allow; cap test | Probe that SubagentStart/Stop fire with `agent_id` + `agent_type` inside a worktree |
| G4 | Harness lock: agents can't disarm the guards | Any subagent is denied writes to `**/.claude/{agents,skills,hooks,specs,workflows}/**`, `**/.claude/settings*.json`, `**/.githooks/**`, `.git/**` and `CLAUDE.md`, **including the copies inside worktrees**. The main session gets an `ask` rule for the same paths. Everything else under `.claude/worktrees/**` is exempt, and `docs/` is unaffected. Subagent Bash `git config *` is also denied (it protects `core.hooksPath`). **Live from the end of F0a** | `guard-agent.mjs`, `guard-bash.mjs`, settings `ask` | subagent Edit `.claude/worktrees/x/.claude/hooks/a.mjs` → deny; Edit `.claude/worktrees/x/src/A.cs` → **allow** (regression test for the enforce-boundaries lesson); main session Edit `.claude/hooks/a.mjs` → prompts | Main-session edit of `CLAUDE.md` prompts in bypass: David **denies**, and the tool result shows the rejection |
| G5 | Repo admin, merge and force-push are nobody's | settings `deny` (holds in bypass, R3): `gh repo edit\|delete\|create *`, raw `gh api *`, `gh auth *`, `gh pr merge *` (David always merges in the GitHub UI), `git push --force*`. `guard-bash.mjs` catches wrapped forms (`sh -c`, `-f`, `--force-with-lease`). No PAT (D5) | settings.json, `guard-bash.mjs` | each pattern, bare and wrapped → deny (incl. `gh pr merge 3 --merge`) | `gh repo edit --visibility public` in bypass → denied |
| G6 | Every push, PR and PR comment is David's call | settings `ask`: `git push *`, `gh pr create *`, `gh pr comment *`. These prompt even in bypass, and the prompt names the subagent | settings.json | self-test asserts the three rules are present | git agent pushes a feature branch in bypass + auto → David is prompted and **denies**. The tool result shows the rejection, and the prompt names `git` (spike t5 passed with `general-purpose`) |
| G7 | Nothing is pushed to `main` | **From the `main-locked` marker, like G8** (David, F0a t13): F0a still pushes its own commits to `main` under D11. `guard-bash.mjs` catches `main`, `HEAD:main`, `:refs/heads/main`, a bare `git push` on `main`, and wrapped forms, once **the remote's `main`** (`origin/main`) carries `.githooks/main-locked`. **pre-push** rejects any ref update to `refs/heads/main` once the pushed-to ref's current SHA or `origin/main` carries it. The marker commit itself is therefore pushed once, and nothing reaches `main` after it (F0a t20; a local-only check would have blocked the marker's own push) No settings deny: a settings rule can't be marker-gated | settings.json, `guard-bash.mjs`, `.githooks/pre-push` | `git push origin HEAD:main` → deny | In a scratch clone, a hand-run `git push origin HEAD:main` → pre-push rejects |
| G8 | From F0b, no commits on `main` | If the current branch is `main` and `.githooks/main-locked` exists in `HEAD` (it's added by F0a's last commit), then `guard-bash.mjs` denies `git commit` and **pre-commit** exits 1. Before that marker, D11 allows commits on `main` | `guard-bash.mjs`, `.githooks/pre-commit`, `.githooks/main-locked` | commit on `main` with marker → deny; without marker → allow; on a `feature/*` branch → allow | After the marker commit, a test commit on `main` is refused |
| G9 | Commits carry their trailers (shared with traceability) | `guard-bash.mjs` on `git commit` by a subagent: the message must carry `Agent:` = its `agent_type`, plus `Plan:` and `Node:`; a tester or builder commit also carries `Rule:` (an id, or `none`) and `TDD:` (`scaffold|red|stub|green|refactor`), except the QA script (`docs(`), which has no `TDD`. The **commit-msg** hook requires `Agent/Plan/Node` on `feature/*` and `bug/*` branches. `git add -A` / `git add .` are denied for agents (traceability) | `guard-bash.mjs`, `.githooks/commit-msg` | builder commit with `Agent: tester` → deny; missing `Node:` → deny | One loop commit is accepted, and `git log` shows the trailers |
| G10 | Nothing private reaches the public repo (shared with context) | **pre-commit** greps the staged diff against **two files** (format and scan owned by context C6): the committed `.claude/hooks/privacy-denylist.txt`, which holds **generic patterns only** (local absolute paths such as `[A-Za-z]:/Users/` and `/c/Users/`, emails other than the public noreply, key shapes such as `-----BEGIN .*PRIVATE KEY`, `ghp_`, `github_pat_`, `AccountKey=`, `Password=`, `ANTHROPIC_`), and the gitignored `.githooks/privacy-denylist.local.txt`, which holds private terms and is **optional** (absent → committed file only). `/save-context` reads both before writing a snapshot. A hit → commit rejected, with the line shown | `.githooks/pre-commit`, `.claude/hooks/privacy-denylist.txt`, `.githooks/privacy-denylist.local.txt` (local, gitignored) | staged absolute user path (built at test time) → rejected; staged fake `ghp_` → rejected; a term only in the local file → rejected; local file absent → scan still runs | Stage a fake key, then confirm rejection |
| G11 | Git hooks gate commits and pushes | **pre-commit:** G8, G10, then `dotnet build -warnaserror`. **pre-push:** G7, `dotnet build`, `dotnet test` (all test projects), `dotnet ef migrations script --idempotent` (must succeed), and the self-test, isolated from the hook's `GIT_*` environment by `clean-git-env.mjs` (`node --test ".claude/hooks/__tests__/*.test.mjs"`; Node 22 doesn't search a directory argument, F0a t12). `guard-bash.mjs` denies `--no-verify`, `commit -n`, `git -c core.hooksPath` and any `git config` that writes `core.hooksPath` (hook-path overrides; reads with `--get`, `get` or the bare key are allowed, since `/orient` checks the setting) for everyone, the main session included (the git hooks' own leg; the one-time F0a install runs before the guard is live). Postman is manual only (D12) | `.githooks/pre-commit`, `.githooks/pre-push`, `guard-bash.mjs` | compile error → commit rejected; red test → push rejected; broken migration → push rejected | Each of the three, once, in a scratch branch |
| G12 | Reviewer and security are read-only | Frontmatter `tools: Read, Grep, Glob` (D3). G1 denies their writes as a belt | `.claude/agents/{reviewer,security}.md` | self-test parses the frontmatter: no Edit, Write or Bash | — |
| G13 | Declared = wired | `guards.test.mjs`. (i) Every `.claude/hooks/*.mjs` (not `lib/`) is referenced in `settings.json`, and every command there exists. (ii) Every manifest ID has at least one fixture, and every fixture passes. (iii) `core.hooksPath` = `.githooks`, and every `.githooks/*` is executable and LF. (iv) No `enabledPlugins`, `extraKnownMarketplaces` or `mcpServers` (supply chain). (v) settings `deny`/`ask` match this Contract **exactly**. It runs in pre-push and in `/orient` | `.claude/hooks/__tests__/guards.test.mjs` | unwire one hook → the test fails | Run once with a hook unwired, then once rewired |
| G14 | No PR without PASS for HEAD | `guard-bash.mjs` on `gh pr create` (any agent or the main session): reads the node's verdict files (traceability layout; branch → `<plan>/<node>`), takes the **latest** reviewer verdict and the **latest** security verdict by `ts`, and denies unless both are `PASS` **and** both still cover HEAD. A verdict covers HEAD when its `head_sha` is HEAD; when it is an ancestor of HEAD and every commit since touches only the bookkeeping paths (`docs/trace/`, `docs/plans/`, `docs/qa/`, `docs/context/`: the QA script, plan status, trail and snapshots written after review); or, after a rebase, when its `diff_fingerprint` (the code diff's patch-id, bookkeeping paths left out) equals the current code diff's against `merge-base origin/main HEAD`. Any later code commit makes it stale (amended 2026-10-18, David: the loop's own post-review commits made every PR stale). **Harness route:** on a `harness/<slug>` branch, the main session (never an agent) may open a PR when `git diff --name-only origin/main...HEAD` touches only `.claude/`, `.githooks/` and `CLAUDE.md`. A harness change has no verdicts; David reviews it in the PR. A missing or unparseable verdict → deny (fails closed). It runs before G6's `ask` prompt | `guard-bash.mjs` | both PASS at HEAD → allow; reviewer PASS + security FAIL → deny; both PASS but `head_sha` ≠ HEAD (a later commit) → deny; no verdicts → deny; an older FAIL superseded by a later PASS → allow | In a scratch branch, `gh pr create` with a stale verdict → denied |

Hooks owned elsewhere but wired in the same file, so G13 covers them: `session-start.mjs` (context) and the traceability activity / render hooks.

### settings.json permissions block (draft)

```jsonc
{
  "permissions": {
    "deny": [
      "Bash(gh repo edit *)", "Bash(gh repo delete *)", "Bash(gh repo create *)",
      "Bash(gh api *)", "Bash(gh auth *)", "Bash(gh pr merge *)",
      "Bash(git push --force*)"
    ],
    "ask": [
      "Bash(git push *)", "Bash(gh pr create *)", "Bash(gh pr comment *)",
      "Edit(//**/.claude/agents/**)", "Edit(//**/.claude/skills/**)", "Edit(//**/.claude/hooks/**)",
      "Edit(//**/.claude/specs/**)", "Edit(//**/.claude/workflows/**)", "Edit(//**/.claude/settings*.json)",
      "Edit(//**/.githooks/**)", "Edit(//**/.git/**)", "Edit(//**/CLAUDE.md)"
    ]
  },
  "hooks": {
    "SessionStart":  [{ "matcher": "startup|resume|clear|compact", "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/session-start.mjs\"" }] }],
    "PreToolUse":    [{ "matcher": "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob", "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/guard-agent.mjs\"", "timeout": 10 }] },
                      { "matcher": "Bash", "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/guard-bash.mjs\"", "timeout": 10 }] }],
    "SubagentStart": [{ "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/gate-subagent.mjs\" start" }] }],
    "SubagentStop":  [{ "hooks": [{ "type": "command", "command": "node \"$CLAUDE_PROJECT_DIR/.claude/hooks/gate-subagent.mjs\" stop", "timeout": 30 }] }]
  }
}
```

Notes on the block:
- `Edit(...)` rules cover every built-in file-editing tool (Write, NotebookEdit).
- The `ask` path rules also match subagents, but G4's hook denies those first.
- Traceability adds its own hook entries to this file.
- No `allow` rules carry security weight, because they do nothing in bypass (R3).

### Files

**Claude hooks** (Node `.mjs`, no npm deps, `.claude/hooks/`):
- `guard-agent.mjs`: G1, G2, G4
- `guard-bash.mjs`: G4 (Bash), G5, G7, G8, G9, G11 (no-verify, hook-path overrides), G14 (PR gate)
- `gate-subagent.mjs`: G2 (second leg), G3
- `lib/roles.mjs`, `lib/paths.mjs`, `lib/stub-rule.mjs`
- `__tests__/guards.test.mjs` (`node:test`): G13 plus every fixture

**Git hooks** (POSIX `sh`, LF via `.gitattributes` `.githooks/* text eol=lf`):
- `.githooks/pre-commit`
- `.githooks/commit-msg`
- `.githooks/pre-push`
- data files: `.githooks/main-locked`; `.githooks/privacy-denylist.local.txt` (optional, never committed). The committed denylist is `.claude/hooks/privacy-denylist.txt` (context C6)
- `.gitignore` gains `.githooks/privacy-denylist.local.txt`

**Install:** `git config core.hooksPath .githooks`, once per clone, as an F0a setup step. Worktrees share the repo config. G13(iii) and `/orient` report the setting if it's missing, because a git hook that isn't installed is inert.

### Wiring self-test
`node --test ".claude/hooks/__tests__/*.test.mjs"` is G13. It runs in pre-push, in `/orient`, and by hand after any harness edit. F0a is done when every F0a probe above has been run once and its result is noted in F0a's README, with "hooks not yet live" stated for the commits made before the lock.

### OWASP for the harness
The defence is **blast radius, not detection**. Every guard holds even if an agent's instructions are hijacked, because deny rules, ask rules, hooks and git hooks don't consult the model.

| OWASP item (LLM Top 10 2026 · Agentic Top 10 for 2026) | Risk in this harness | Our guard / stance |
|---|---|---|
| LLM01:2026 Prompt Injection · ASI01:2026 Agent Goal Hijack | **PR comments:** anyone can comment on a public PR. **Repo text** (README, requirements, test data, code comments) and **tool output** (build logs, `gh` output) is read as instructions | PR comments are fetched with `gh pr view --json comments` and filtered on `author.login == "dpmorgandev"` before any text reaches an agent. Other authors are never acted on (orchestration). Repo text and tool output are treated as data. Residual risk is **accepted**, and it's bounded by G1–G9 and G14 |
| LLM02:2026 Sensitive Information Disclosure | Local paths, private notes or keys committed to a public repo; snapshots in `docs/context/` | G10 (pre-commit and `/save-context` read the same two denylists). No secrets exist (no Key Vault; managed identity). Logs carry no PII (architecture) |
| LLM03:2026 Excessive Agency · ASI02:2026 Tool Misuse and Exploitation · ASI03:2026 Identity and Privilege Abuse | `gh` is authenticated as David with repo admin. Agents could push, comment, merge, publish or edit the harness | G1/G2/G12 least privilege per role. No agent spawns agents. G5 deny (incl. `gh pr merge`), G6 ask, G7/G8 `main` protection, G14 PR gate, G4 harness lock. There's no server-side branch protection while the repo is private (R7) and no PAT (D5): **accepted**, with a talking point |
| LLM04:2026 Supply Chain · ASI04:2026 Agentic Supply Chain Vulnerabilities | Third-party skills, plugins, MCP servers or hook packages run with our permissions | All skills, agents and hooks are ours. No plugins and no MCP in project settings (G13 iv). Hooks have no npm deps. NuGet packages are named in the architecture spec and pinned in `Directory.Packages.props`; vulnerability scanning is **out of scope** |
| LLM05:2026 Data and Model Poisoning | No training or fine-tuning | **Out of scope.** Context poisoning → ASI06 |
| LLM06:2026 Unbounded Consumption · ASI08:2026 Cascading Failures | Retry loops, stop-hook loops, token spend | Retry ladder (attempt 3 escalates, attempt 5 stops, a repeated fingerprint escalates early), the G3 block cap, hook timeouts. No generic loop breaker (D9). Spend is **accepted** (Max plan) |
| LLM07:2026 Misinformation · ASI09:2026 Human-Agent Trust Exploitation | An agent claims "green" or "PASS" without it being true; David approves a smooth summary | Receipts over claims: the orchestrator runs builds and tests itself, plus G11 git hooks, hook-written verdicts, G14 and trailers (G9). David's pauses review real artefacts (tests, Postman, `walkthrough.md`). Ask prompts name the asking agent |
| LLM08:2026 Hidden Context Exposure | Agent and skill prompts are in the repo | **Accepted by design:** they're public deliverables and hold no secrets |
| LLM09:2026 Vector and Embedding Weaknesses | No RAG or embeddings | **Out of scope** |
| LLM10:2026 Improper Output Handling · ASI05:2026 Unexpected Code Execution (RCE) | Agent output becomes shell commands, code and commits | `guard-bash.mjs` checks what runs, G3 checks what changed, and verdicts are parsed as JSON rather than trusted prose. Code passes tests, reviewer and security. There's no OS sandbox on Windows native: **accepted risk** |
| ASI06:2026 Memory & Context Poisoning | Snapshots, `CLAUDE.md` and skills steer future sessions | Only the main session writes snapshots, and the G10 check runs before they're written. `/orient` waits for David's confirmation. G4 locks `CLAUDE.md`, skills and agents. Agents never read `*.spec.md` |
| ASI07:2026 Insecure Inter-Agent Communication | Hand-offs between agents | No agent-to-agent channel: the orchestrator relays everything. Verdicts are written by SubagentStop as files, not taken as relayed text |
| ASI10:2026 Rogue Agents | An agent acts outside its role | G3 diff-scope, G9 trailer check against `agent_type`, activity receipts cross-checked against git at render time (traceability) |

**Out of contract (deliberately):** a generic detect-loop (D9), `disableBypassPermissionsMode` (D10), a PAT (D5), and sandboxing (unavailable on Windows native).

## Open questions

Resolved:
- **Hook `ask` in bypass:** not relied on. Every prompt is an `ask` *rule* (G6, G4). It stays an F0a probe only.
- **"Every push is David's" vs "the git agent raises the PR":** resolved by D4. The agent proposes and David approves via the `ask` rule. requirements.md §3 should read: "Every push, PR and PR comment needs David's approval; merging and visibility changes are David's alone."
- **requirements.md §3 should state:** "Guards hold in bypass and auto mode (deny rules, ask rules, hooks, git hooks); allow rules and protected-path prompts don't. No branch protection while private (GitHub Free, verified 403)."
- **Where the main thread writes `docs/trace`/`docs/context`:** in the worktree. Trails stay uncommitted until `chore(trace)` (traceability). Snapshots go to the current branch (context D8).
- **Stop-hook build gate:** dropped. The orchestrator runs build and test gates via Bash, and G11 git hooks are the mechanical leg, so there's no per-turn `dotnet build` cost.

Still open:
1. **(open)** Does `agent_type` arrive for workflow `agent({agentType})` as well as for project subagents (G1 F0a probe)? If it doesn't, G1/G2's first leg falls back to frontmatter `hooks:` (O1-B). G3 is unaffected.
2. **(open)** Which `settings.json` governs after `EnterWorktree`: the main checkout's copy or the worktree's? Probe in F0a. G4 locks the worktree copies either way.
3. Resolved (David, 2026-10-17): the builder may create `global.json`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props`, `BannedSymbols.txt`, `.gitattributes` and the `.slnx`; the tester creates the `tests/*.csproj` files under `tests/**` (G1).
4. Resolved (David, 2026-10-17): `Bash(gh pr merge *)` is added to the deny list for everyone, in both the settings draft and G5. David always merges in the GitHub UI.
5. Resolved (David, 2026-10-17): yes. `guard-bash.mjs` denies `gh pr create` unless the latest reviewer and security verdicts for the current node are PASS and their `head_sha` equals HEAD (G14, with fixtures).
6. Resolved (David, 2026-10-17): two files. The committed `.claude/hooks/privacy-denylist.txt` holds generic patterns only; a gitignored, optional `.githooks/privacy-denylist.local.txt` holds private terms; the `/save-context` check and the pre-commit hook read both (G10, context C6).
7. Resolved (David, 2026-10-17): re-keyed to the OWASP LLM Top 10 2026 plus the OWASP Top 10 for Agentic Applications for 2026, both verified from OWASP's own PDFs (R-AI). The 2025 list stays in Research as history.
