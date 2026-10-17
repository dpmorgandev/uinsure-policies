# build/F0a: guard hooks

## t13: guard-agent, guard-bash, gate-subagent — 2026-10-17

**Built** (`.claude/hooks/`, Node built-ins only, fail closed):
- `guard-agent.mjs` covers G1, G2 and G4, plus the spec-read deny. Its matcher is
  `Edit|Write|NotebookEdit|Bash|Read|Grep|Glob`. For Read, Grep and Glob it denies only `.claude/specs/`,
  so read-only agents keep working.
- `guard-bash.mjs` covers G4 (the Bash leg), G5, G7, G8, G9, G11 and G14. It scans the whole command text,
  so wrapped forms and full paths to `git` or `gh` are caught.
- `gate-subagent.mjs start|stop` covers G3 and G2's second leg, the verdict schema v1 and its semantic
  checks, and the investigator findings check. It files verdicts and findings through `lib/trace.mjs`.
- `lib/io.mjs`, `lib/paths.mjs`, `lib/roles.mjs`, `lib/stub-rule.mjs` and `lib/schema.mjs`. The verdict
  schema is read from the verdict-format skill at runtime, so the hook and the skill can't drift apart.
- All are wired in the main checkout's `settings.json`. `core.hooksPath=.githooks` was installed first
  (pulled forward from t15), because G11 denies changing it once `guard-bash.mjs` is live.

**Fixtures:** `__tests__/guards.test.mjs` has 24 tests. With the 12 trace fixtures, `node --test
".claude/hooks/__tests__/*.test.mjs"` gives 36/36 passing.

**Live receipts** (real session, after wiring):

| Probe | Result |
|---|---|
| Main session `gh api rate_limit` | Denied: `G5: raw gh api is denied for everyone`. The command never ran |
| `tester` agent: Write `src/Probe.cs` with a method body | Denied: `G2: a new src/ file from the tester must be stub lines only` |
| `tester` agent: Edit `CLAUDE.md` | Denied: `G4: harness lock: CLAUDE.md can't be written by an agent` |
| `tester` agent: Read `.claude/specs/context.spec.md` | Denied: `agents never read .claude/specs/` |
| `tester` agent: Bash `echo x > .githooks/probe.txt` | Denied: `G4: harness lock: .githooks/probe.txt … (via Bash)` |
| `tester` stop, with uncommitted harness files already in the tree | Passed G3 (`ok: true` in the gate log). The start snapshot excluded the files that were already dirty |
| Claude Code's internal helper (empty `agent_type`) stop | Logged only, not blocked |
| Disk afterwards | Neither `src/Probe.cs` nor `.githooks/probe.txt` exists |

**Unplanned findings**
1. **G3 blamed pre-existing changes on the agent.** `git diff --name-only <start>` compares the working
   tree, so files already uncommitted when an agent started were attributed to it. That meant any dispatch
   during harness work would be blocked as a G4 breach. Fixed: SubagentStart records each dirty file's
   hash, and stop ignores a path whose content hasn't changed (a fixture covers it).
2. **`CLAUDE.md` is read at session start, corroborated.** The tester quoted `CLAUDE.md`'s first line as
   `# PROBE-MARKER-MAIN`: the placeholder from session start, not the file on disk (`# Uinsure Policies
   API`, written in t8). It couldn't have read the file, so that came from its context.
3. **`gh` installs under a path with spaces** (`C:\Program Files\GitHub CLI\gh.exe`). The first
   `normalise` missed a quoted path with spaces. Fixed, and a fixture covers it.
4. **`guard-bash.mjs` reads heredoc commit messages as command text.** A message that mentions a denied
   flag is refused (a deliberate over-match). The main session commits with `git commit -F <file>`.
5. **The over-match bit the main session live.** A `node -e` spec-editing script whose text contained a
   force-push rule string was denied with `G5: force-push is denied for everyone`. That's the intended
   floor. Scripts that carry such strings go in a file (`node <file>`).

## t14: settings deny and ask rules, .ignore — 2026-10-17

`.claude/settings.json` now carries the Contract's permissions block. guardrails' settings draft is
followed exactly, except for the push-to-`main` deny, which David dropped in t13 (G7 is marker-gated):
- **deny** (holds in bypass): repo edit, delete and create, raw `api`, `auth`, and `pr merge` for `gh`;
  force-push for `git`.
- **ask**: `git push *`, `gh pr create *`, `gh pr comment *` (G6), and `Edit(//**/…)` for the harness
  paths (`.claude/{agents,skills,hooks,specs,workflows}/**`, `.claude/settings*.json`, `.githooks/**`,
  `.git/**`, `CLAUDE.md`), which gives G4's main-session prompt.
- `.ignore` at the repo root holds `.claude/specs/`. It's a convenience only; `guard-agent.mjs` is the guard.

**Live receipt (G4, main session):** an Edit to `CLAUDE.md` prompted in bypass mode.
- First attempt: approved by mistake. The one-character change was reverted at once, and `git diff`
  was clean.
- Second attempt: **David denied it**, and the tool result read "The user doesn't want to proceed with
  this tool use". This is the receipt; the first attempt only showed that the prompt appears.

G6's ask rules were proven in t5. The deny rules sit behind the guard hooks, which fire first and deny
the same commands (t13: raw `gh api` was denied by `guard-bash.mjs`). Their presence is asserted by the
G13 self-test (t16).

**Finding:** a heredoc that merely *names* a denied command (this section's first draft) was itself
denied by `guard-bash.mjs`. Trail text with such strings is written with the Edit tool, not through Bash.

## t15: git hooks — 2026-10-17

`.githooks/` holds POSIX `sh` scripts, executable in the index (`100755`), with LF in both the index and
the working tree via `.gitattributes` `.githooks/* text eol=lf`. `core.hooksPath=.githooks` was installed
in t13. `.gitignore` already held `.githooks/privacy-denylist.local.txt`.
- `pre-commit`:
  - G8: no commits on `main` once the marker is in HEAD.
  - G10: the **staged** version of each added or modified file is scanned with `privacy-scan.sh`
    (both denylists).
  - Then `dotnet build Uinsure.Policies.slnx -warnaserror`.
- `commit-msg`: G9. On `feature/*` and `bug/*` branches, `Agent` must be one of
  tester/builder/git/main, and `Plan`/`Node` must match the branch.
- `pre-push`:
  - G7: no ref update to `refs/heads/main` once the marker is on `main` (remote, local or origin), or
    in the pushed commit.
  - Then a Release build, the full test suite, `dotnet ef migrations script --idempotent`, and the
    harness self-test.
- **Before F0b** there's no `Uinsure.Policies.slnx`, so the .NET steps are skipped with a printed notice.
  The privacy scan, G7/G8/G9 and the self-test always run. This is a bootstrap reading of testing C8;
  F0b's first commit adds the solution.

**Receipts:** a scratch clone with a bare remote passed 11/11 scenarios:
- `main` pushed before the lock (self-test ran)
- a staged local absolute path rejected (G10), and a clean file committed
- commit-msg on a node branch: no trailers rejected, wrong `Node` rejected, matching trailers accepted
- a feature branch pushed
- the marker commit itself allowed
- a commit on locked `main` rejected (G8)
- a push to locked `main` rejected (G7), and `HEAD:refs/heads/main` from a branch **without** the marker
  rejected (G7)

The F0a probe for G11's build legs (compile error, red test, broken migration) needs the solution, so it
moves to F0b.

**Findings (all fixed before commit)**
1. **The scanner ran outside the repo.** The first pre-commit ran `privacy-scan.sh` from inside the
   staged-file temp directory, which isn't a git repo, so every scan errored out. Even the G10 "reject"
   failed for the wrong reason.
2. **G7 only looked at the pushed commit.** A branch without the marker could overwrite `main` after the
   lock. The lock is now read from `main` itself. `guard-bash.mjs`'s `mainLocked` had the same flaw (it
   read HEAD only) and now also checks `main` and `origin/main`.
3. **Git for Windows' shell rewrites `refs/…:path` arguments.** `git cat-file -e
   refs/heads/main:.githooks/main-locked` reached git as `refs\heads\main;.githooks\main-locked` and
   silently failed. Hook scripts resolve refs to SHAs before using `rev:path`. Node hooks call `git.exe`
   directly and aren't affected.
4. `xargs` turns a child's exit 1 into 123, which would have misreported a privacy hit as a scan failure.
   pre-commit now decides on whether there's output.

## t20: harness lock and main-locked — 2026-10-17

**Fixes before the lock** (`01677e3`):
- G7 now reads the **remote's** `main`: the pushed-to ref's SHA, or `origin/main`. The t15 version also
  checked the pushed commit and local `main`, which would have rejected the marker commit's own push, so
  the lock could never have reached `origin/main` or F0b's worktree.
- `render-trace.mjs render` writes nothing for an `owner: david` node, so F0a's hand-written README can't
  be overwritten.
- The scratch clone was re-run at 12/12, with each rejection's reason checked:
  - the marker commit's first push to `main` is allowed
  - a branch without the marker pushed to `main` is rejected (G7)
  - a true fast-forward onto locked `main` is rejected (G7)

  An earlier version of that last scenario was rejected only as a non-fast-forward. Git drops such refs
  before pre-push runs, so it wasn't testing G7 at all. It was rewritten.

**Harness-lock provocation (F0a acceptance):** the real `builder` agent was asked to write
`.claude/agents/probe.md`, `.claude/worktrees/build-F0b/.claude/agents/probe.md`, and (via Bash)
`.claude/agents/probe-bash.md`. All three were denied: `G4: harness lock: … can't be written by an agent`.
The worktree path was resolved relative to its worktree root first (R5). Nothing was written, and the
builder's stop passed G3 (`ok: true` in the gate log).

**The marker:** `.githooks/main-locked` is committed in F0a's last commit, with t20 and the F0a node marked
`done`. From that commit on:
- G8: `guard-bash.mjs` and pre-commit refuse commits on `main`.
- G7: once it's pushed, pre-push and `guard-bash.mjs` refuse pushes to `main`.
- Everything goes through a branch and a PR.

The live G8 check runs right after this commit, and its result is recorded in the next context snapshot.

## t16: wiring self-test (G13) — 2026-10-17

`.claude/hooks/__tests__/guards.test.mjs` gains the G13 checks, which read the real repo. The manifest IDs
and the permissions draft are parsed from guardrails itself, so the Contract is the single source:
- **(i)** every `.claude/hooks/*.mjs` is wired in `settings.json`, every wired command's script exists,
  and the manifest's files are present.
- **(ii)** every manifest ID G1–G14 has at least one fixture. G6, G10, G12 and G13 were added here.
- **(iii)** `core.hooksPath` is `.githooks`, and each git hook is `100755` and LF in both the index and
  the working tree.
- **(iv)** no `enabledPlugins`, `extraKnownMarketplaces`, `mcpServers` or `enableAllProjectMcpServers` in
  `settings.json`, `settings.local.json` or `.mcp.json`.
- **(v) + G6:** `permissions.deny` and `permissions.ask` equal the Contract's draft exactly.
- **G12:** reviewer, security and investigator have exactly `Read, Grep, Glob`.
- **G10:** the privacy scan flags a local absolute path and passes clean text.
- **Line caps:** agent bodies ≤120, preloaded skills ≤200 (architecture ≤150), main-session skills ≤250,
  descriptions ≤3 lines, `CLAUDE.md` ≤80.
- The verdict-format schema block is byte-identical to orchestration's.

**Result:** `node --test ".claude/hooks/__tests__/*.test.mjs"` gives **45/45**.

**F0a probe (G13):** with an unwired `.claude/hooks/probe-unwired.mjs` present, the run failed with
`G13 (i) … probe-unwired.mjs exists but isn't wired in settings.json` (44/45). After it was removed, the
run was 45/45 again.

`/orient` gains a harness step: it reports `core.hooksPath` and the self-test counts. pre-push already
runs the self-test (t15). `.claude/settings.local.json` holds only two `allow` rules, for `git add` and
`git commit`. They're inert in bypass mode, and G13(iv) finds no plugins or MCP in it.

**Settled by David (t13):** G7 is gated on the `main-locked` marker like G8 (no settings deny for pushes
to `main`), and the stub grammar is testing C7's.
