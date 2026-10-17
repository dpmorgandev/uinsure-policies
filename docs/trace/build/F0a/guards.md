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

**Settled by David (t13):** G7 is gated on the `main-locked` marker like G8 (no settings deny for pushes
to `main`), and the stub grammar is testing C7's.
