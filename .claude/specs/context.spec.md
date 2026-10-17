# context.spec.md
Status: DECIDED — contract final (2026-10-17)
Researched 2026-10-17 · source of truth: `docs/requirements.md` §4 row 0

## Dependencies

**Upstream (relied on, not designed here)**
- `docs/requirements.md` — read by `/orient`; rule IDs referenced in snapshots.
- Repo layout §3: snapshots in `docs/context/` (project records, committed), skills in `.claude/skills/`, hooks in `.claude/hooks/`, registration in `.claude/settings.json`.
- Claude Code 2.1.302 (installed). Git Bash is present, so it is the default hook shell (see Research R1).
- Tools found on PATH: `git`, `gh`, `node`, `dotnet`.

**Consumed paths (owned by other specs; /orient reads them and never defines their shape)**
- `docs/plans/active/*.dag.yaml` (the DAG: plan, node and task status) and `*.plan.md` (prose). Owner: `planning.spec`.
- `docs/trace/<plan-id>/<node-id>/`, the trail for the node in flight. Owner: `traceability.spec`.
- `git worktree list`, the branch convention `feature/<plan>/<node>` (or `bug/<plan>/<node>`) and the worktree path `.claude/worktrees/<plan>-<node>`. Owner: `orchestration.spec`.
- `gh pr list`. The remote may not exist yet (requirements §6 item 2), so a missing remote is reported, not treated as an error.
- `docs/lessons.md`, `docs/qa/`. Owners: planning and testing.

**Downstream consumers**
- The orchestrator calls `/save-context` at node boundaries (orchestration.spec).
- `guardrails.spec` must cover the privacy guard on `docs/context/` (see D6).
- `traceability.spec`: snapshots are *not* a trail. They point at trails and never duplicate them.

## Research

**R1. SessionStart hook** ([hooks docs](https://code.claude.com/docs/en/hooks), fetched 2026-10-17)
- Matchers (equal to the input's `source`): `startup`, `resume`, `clear`, `compact`, `fork`.
- Input: common fields (`session_id`, `transcript_path`, `cwd`, `hook_event_name`) plus `source`, and optionally `model`, `agent_type` and `session_title`. On `resume`/`fork` it also gets `seconds_since_last_response` (v2.1.251+, so available here), which is a free staleness signal.
- Injection: plain stdout on exit 0 is added to Claude's context. The structured form is `{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"…"}}`.
- **Cap: 10,000 characters per `additionalContext` string**, with no setting to raise it. Over the cap, the output goes to a file and only a 2,000-char preview is injected, and Claude is not told to read the file.
- It **cannot block**. Exit 2 only shows stderr to the user. Only `command` and `mcp_tool` handler types are allowed (no `prompt`/`agent`).
- Claude's first response waits for SessionStart hooks to finish, so the hook must be fast. That counts against a `dotnet run` file-based script (cold build) and in favour of bash or node.
- **No documented way for a hook to invoke a skill.** `initialUserMessage` applies only to `-p` (headless), which is ToS-barred here (requirements §5). Uncertain: whether a slash command inside it would expand. Irrelevant either way, since we can't use `-p`. **So the hook can only *prompt* `/orient`. It cannot run it.** That settles the requirements' "triggers or prompts" wording: prompts.
- `CLAUDE_ENV_FILE` is available to SessionStart. Not needed here.
- Windows: shell-form hooks run in Git Bash when it is installed, otherwise PowerShell. `"shell"` can force either.

**R2. PreCompact / PostCompact / SessionEnd** (same source)
- PreCompact: matchers `manual|auto`, input `trigger` and `custom_instructions`. It **can block** (exit 2 or `"decision":"block"`). It **cannot inject context or run a skill**. Blocking auto-compaction at the context limit makes the request fail.
- PostCompact: input `trigger` and `compact_summary`. No decision control. Side effects only.
- SessionEnd: input `reason` (`clear|resume|logout|prompt_input_exit|other`). Default 1.5 s budget, no decision control.
- **Consequence:** no hook can make Claude write a *reasoned* snapshot before compaction. A hook can only (a) write a *mechanical* snapshot from git and plan facts, or (b) block a manual `/compact` with a "run /save-context first" message. After compaction, `SessionStart` with `source=compact` *can* inject "reload context", and that is the useful leg.
- `compact_summary` is model-written session prose. Writing it into a committed file would risk leaking private content into a public repo. Don't.

**R3. Prior art: agentkinOS**
- `save-context` (156 lines): real-clock step, tail = sorted-last of `contexts/*.md`, name `{YYYYMMDD-HHMMSS}-{agent}-{slug}.md`, 7 frontmatter keys and 9 sections (`(none)`, never omitted), 500-line cap (warn and force a checkpoint at 500, refuse above), checkpoint at 5 non-checkpoints / `--status final|blocked` / "a thread settled", and an append to `checkpoints.md`.
- **Machinery not to port:** step 8 native receipt (`scripts/context-receipt.ts`, `@substrate/orientation`), step 9 retrospective/sleep cycle (`AGENTKINOS_SLEEP_CMD`), and the `Promote?` section (brain promotion path).
- `load-context-chain` (74 lines): read-only, walks back to a checkpoint / `--max-depth` (default 5) / null / broken link, synthesises oldest to newest in about 50 lines (never pastes), flags snapshots older than 24h, and **confirms with the human before resuming**. The snapshot wins over `checkpoints.md`.
- `orient` (99 lines): steps 1–4 read a private notes workspace plus a receipt, and are **not portable** (requirements: project-only). Step 5 is portable: *"run the REAL /load-context-chain VERBATIM … HOLD for the operator's confirmation"*. The doc's "hook-vs-skill split" says *"The SessionStart hook runs the MECHANICAL receipt"*. **But `agentkinOS/.claude/settings.json` registers no SessionStart hook** (only PreToolUse). That confirms requirements §4: the documented mechanical leg was never wired.

**R4. agentkinOS `contexts/`: evidence from 31 real snapshots** (measured 2026-10-17)
- **Checkpoint inflation: 28 of 31 snapshots have `checkpoint: true`.** The "every 5" cadence almost never fired, because "thread settled" plus `--status` made nearly every save a checkpoint. In practice the chain walk was depth 1, and the chain added little over "read the newest file".
- **`checkpoints.md` drifted:** 2 of its 28 entries carry a `contexts/` prefix and the rest are bare filenames. Hand-maintained manifests drift.
- **The 500-line cap was never approached:** the largest snapshot is 193 lines (`20260724-034213-…`). Typical size is 100–190 lines.
- **Unsnapshotted session:** the 2026-10-14 snapshot records that the 2026-09-08 session wrote no snapshot and had to be "reconstructed from file mtimes". That is the failure mode a SessionStart/compact reminder exists to catch.
- Snapshots are rich in private and relational content (the operator's retreat, health, relationship framing). **That shape must not be copied into a public repo.**

**R5. Prior art: agentflow_phase3** (`.claude/skills/save-context` 170 lines, `load-context-chain` 143 lines, no `orient`)
- It adds what agentkinOS lacks: a `task-id` frontmatter key, and Current state = branch+sha / tests X/Y / uncommitted count / blockers. That is closer to what this project needs.
- **It registered a SessionStart hook** (`settings.json:70`): `echo '{"additionalContext": "If resuming work, run /load-context-chain …"}'`. Uncertain: that JSON puts `additionalContext` at the top level, not under `hookSpecificOutput`. Per R1, valid JSON stdout is parsed as hook output, so the hint was probably dropped rather than injected (not tested). **So requirements §4's "no SessionStart hook was registered" is true of agentkinOS, but agentflow did try one, and likely got the format wrong.** Lesson: test the hook (D4 contract includes a smoke check).
- agentflow's `commit-on-stop.mjs` is the pattern requirements §5 says to avoid. Snapshots must not be auto-committed by a hook either.

**R6. This repo today:** one commit (`48fdd0d docs: initial requirements`), on `main`, one worktree. `.claude/` doesn't exist yet. `git worktree list` prints **absolute paths containing the Windows username** (`C:/Users/<user>/...`). Snapshots and hook output must use repo-relative paths. Timestamps: the UK is on BST until 2026-10-25, so use UTC in names (`date -u`) to avoid an ambiguous offset.

## Options

**D1. Snapshot content and frontmatter**
- A. Port agentkinOS's 7 keys and 9 sections as they are. Familiar, but it has no plan/node/attempt fields, and `Promote?` belongs to private-workspace machinery.
- B. agentflow's shape plus project keys: `plan-id`, `node-id`, `phase` (analysis|plan|loop|hitl|merge), `step` (red|green|review|security|integration|qa), `attempt` (retry ladder 1–5), `branch`, `worktree` (relative), `head` (short sha), `tests` (X/Y or `not-run`). Machine-checkable, and the orchestrator can fill it in.
- C. Minimal: frontmatter links only, with state derived from plan and trace at load time. Least duplication, but analysis sessions (before any plan exists) have nothing to derive from.

**D2. Size cap and checkpoint cadence**
- A. Port 500 lines / every 5th. Proven harmless, but 500 is about 2.5× the observed maximum and checkpoints inflated.
- B. **150 lines** soft cap (forced checkpoint) and 200 hard (refuse). Checkpoint triggers are *mechanical only*: `--checkpoint`, every 5th, a feature node reaching DONE/HITL sign-off, and `status: final`. Drop the subjective "thread settled" trigger, which caused the 28/31 inflation.
- C. No chain. Each snapshot is self-contained, and load reads only the newest. Simplest, but it loses "decisions made earlier this feature".

**D3. checkpoints.md manifest**
- A. Keep it (port). A lookup aid, but it drifted (R4).
- B. Drop it. `grep -l '^checkpoint: true' docs/context/*.md` is the manifest. One less file to keep honest.

**D4. SessionStart hook (the mechanical leg)**
- A. A static reminder: plain-text stdout "run /orient". Trivially safe, but it carries no facts.
- B. **A script (bash, LF)** that branches on `source` and prints at most 10k characters: the newest snapshot name and its age, its `next` line, `git status -s` count, branch, relative worktree list, the active plan file names, then the instruction. `startup|resume|clear` → "run /orient before any work". `compact` → "run /load-context-chain, then confirm". It reads only repo files and writes nothing.
- C. B written in node `.mjs` (agentkinOS style). Cross-platform, but it adds a JS file to a C# repo the interviewers will read.

**D5. Triggers for /save-context**
- A. Manual only.
- B. Manual, plus the orchestrator at each node boundary (step change, verdict, attempt increment, HITL hand-off), plus the reminder at SessionStart(`compact`) to save after reloading. No PreCompact hook.
- C. B plus a PreCompact hook on `manual` that blocks `/compact` until a snapshot newer than N minutes exists. It catches the forgotten save, but it can't help with `auto`, and blocking auto-compaction fails the request (R2).
- D. A PreCompact/SessionEnd script writing a mechanical-only snapshot. Rejected as an option to recommend: it can't capture decisions or open questions, and it risks a hook writing (and someone committing) unreviewed content.

**D6. Privacy guard for committed snapshots** (the repo goes public)
- A. A rule in the skill only ("no private content"). Prose, so not a receipt.
- B. The skill rule plus a deterministic check in `/save-context` before writing, and again in the pre-commit hook (guardrails.spec): reject `docs/context/*.md` matching a denylist (private-workspace terms, `C:/Users/`, `/c/Users/`, email regex, the `ANTHROPIC_`/key patterns). Also an allowlist rule: content must be about this repo's work only.
- C. Don't commit snapshots (gitignore them). Safest, but it contradicts requirements §4 ("committed, as project records") and loses visible-process evidence.

**D7. /orient composition**
- A. `/orient` calls the real `/load-context-chain` and then holds for confirmation (agentkinOS step 5). One entry point, one confirm gate.
- B. Separate skills. `/orient` gathers project state, and the user runs `/load-context-chain` separately. That makes two gates and an easy-to-skip second step.

**D8. Who commits snapshots**
- A. The snapshot rides the next normal commit on the branch it was written in.
- B. Its own commit, `docs(context): <slug>`, made by David or the git agent on `main`'s checkout only. Snapshots written inside a feature worktree go to that worktree's `docs/context/`, then merge with the PR.
- Note: with worktrees, *where* the chain lives matters. The chain on `main` and the chain on a feature branch can fork.

## Recommendation

- **D1 → B.** Plan/node/attempt/worktree are the exact things lost across sessions in this project. `plan-id: null` covers analysis sessions.
- **D2 → B.** Observed snapshots peak at 193 lines, so a tight cap keeps the load brief small. Mechanical-only checkpoint triggers fix the measured 28/31 inflation.
- **D3 → B.** Frontmatter is already authoritative in both prior arts. Drop the manifest that drifted.
- **D4 → B (bash).** Git Bash is the default hook shell, and requirements §5 already mandates LF for `*.sh`. Facts plus a prompt beats a bare prompt. Add a one-line smoke test (`echo '{"source":"startup"}' | node .claude/hooks/session-start.mjs`; the Decision moved the hook to Node) to avoid agentflow's silent format bug.
- **D5 → B.** PreCompact can't capture reasoning, and blocking it is brittle. Node boundaries are where state changes, and the orchestrator owns them. C is a cheap add-on if David wants a belt as well.
- **D6 → B.** "A self-reported trail isn't a receipt" (requirements §4 6b) applies equally to privacy. The denylist must be deterministic.
- **D7 → A.** A single confirm gate, ported from the only part of agentkinOS orient that is portable.
- **D8 → B, with the rule "the chain lives on the branch where work happens; on merge, main inherits it".** It keeps snapshots out of TDD commits (visible red/green history).

## Decision — DECIDED 2026-10-17 (David)
- [x] **D1–D7:** accepted as recommended.
  - Snapshot schema B, with project keys: plan, node, branch, attempt, next action and open decisions. Cap 200 lines.
  - Drop `checkpoints.md`.
  - The SessionStart hook is in **Node** (`startup|resume|clear|compact`).
  - No PreCompact block.
  - The privacy denylist is shared with guardrails' pre-commit check.
  - `/orient` runs `/load-context-chain` verbatim and then waits for David's confirmation.
- [x] **D8: snapshots go to the *current branch's* `docs/context/`.** Inside a feature worktree, that means they land on `main` with that feature's PR. A snapshot taken on `main` between features is committed with the next branch, so there's one chain, visible on GitHub. The privacy check refuses anything private before writing.
- [x] **Open questions 1–3:**
  - `/orient` reads whichever checkout the session is in, and it is refused outside the repo root.
  - Only the main session saves snapshots.
  - Decisions live in the specs, so **no `docs/decisions.md`**. Update architecture's "record in `docs/decisions.md`" note.
- [x] **Final rulings (David, 2026-10-17):**
  - The privacy denylist is two files: the committed `.claude/hooks/privacy-denylist.txt` holds generic patterns only, and a gitignored, optional `.githooks/privacy-denylist.local.txt` holds private terms. Both consumers read both (C6).

## Contract

Built first, as F0a step 0. Everything below is binding. Paths are repo-relative. Times are UTC.

**C1. Snapshot file**
- Path: `docs/context/<YYYYMMDD-HHMMSS>-<slug>.md`. Time comes from `date -u +%Y%m%d-%H%M%S`, never estimated. Slug: kebab-case, ≤ 40 chars, `[a-z0-9-]`.
- Only the main session writes snapshots, so the filename carries no agent.
- Frontmatter (every key present, `null` where it doesn't apply):

| Key | Value |
|---|---|
| `timestamp` | ISO 8601 UTC, from `date -u +%Y-%m-%dT%H:%M:%SZ` |
| `previous-context` | basename of the prior snapshot on this branch, or `null` |
| `checkpoint` | `true` / `false` (C3) |
| `status` | `in-progress` / `blocked` / `final` |
| `plan` | `build` / `feat-NNN` / `bug-NNN` / `inv-NNN` / `null` (analysis) |
| `node` | `F0a`..`F7` / `null` |
| `phase` | `analysis` / `plan` / `loop` / `hitl` / `merge` |
| `step` | `red` / `stub` / `green` / `refactor` / `review` / `security` / `investigate` / `rebase` / `qa` / `null` |
| `attempt` | `1`–`5` / `null` |
| `branch` | current branch name |
| `worktree` | repo-relative path (`.claude/worktrees/<plan>-<node>`) / `null` |
| `head` | short SHA |
| `tests` | `X/Y` passing, or `not-run` |
| `next-action` | one line, ≤ 120 chars; the hook echoes it |
| `open-decisions` | integer count of items in the body section |

**C2. Snapshot body.** Sections in this order, all present, `(none)` when empty, never omitted. **Hard cap 200 lines** for the whole file, frontmatter included.
1. `## Summary`: ≤ 5 lines.
2. `## Completed since last snapshot`: bullets, with commit SHAs where they exist.
3. `## Current state`: branch + head, tests X/Y, uncommitted count, blockers.
4. `## Next action`: expands the `next-action` key.
5. `## Open decisions`: one bullet each, naming the spec or question it belongs to.
6. `## Decisions made`: links to the spec's `## Decision` section (e.g. `.claude/specs/domain.spec.md#decision`). Never restate them.
7. `## Pointers`: plan file, `docs/trace/<plan>/<node>/`, PR number. Pointers only; a snapshot never copies a trail.

Content is about this repo's work only: no personal, relational or private-workspace material.

**C3. `/save-context [--checkpoint] [--status in-progress|blocked|final] [--slug <slug>]`** (main session only)
1. Read the clock: `date -u`.
2. Tail = sorted-last `docs/context/*.md` on the current checkout, or `null`.
3. Gather facts: `git rev-parse --abbrev-ref HEAD`, `git rev-parse --short HEAD`, `git status --porcelain | wc -l`, the active plan YAML (`plan`, `node`, `attempt`, node status), the latest test result if one ran this session (else `not-run`). Worktree is relativised against `git rev-parse --show-toplevel`.
4. Checkpoint = true if any of these hold: `--checkpoint`; 4 non-checkpoints since the last checkpoint (so this is the 5th); the node reached DONE or HITL sign-off since the tail; `status: final`; the draft is over 150 lines. Nothing subjective.
5. Compose C1 + C2.
6. Cap: over 200 lines → refuse, report the count, and ask for a tighter draft.
7. Privacy scan the draft (C6). Any hit → refuse, print each hit as `line: category`, write nothing.
8. Write the file. Never commit, never stage. Report: path, lines/200, checkpoint, `privacy: clean`.

Callers: David by hand, and the orchestrator at each node boundary (step change, verdict, attempt increment, HITL hand-off) and after a `compact` reload is confirmed. Subagents never call it.

**C4. `/load-context-chain [--latest-only] [--max-depth N]`** (N defaults to 5; read-only, writes nothing)
1. List `docs/context/*.md` sorted. None → say "no prior context on this branch" and go to step 6.
2. Start at the newest. Follow `previous-context` until a `checkpoint: true` file (inclusive), depth N, `null`, or a broken link. `--latest-only` stops at one. A broken link is reported by name (for example a chain that forked across branches), not fatal.
3. Staleness: newest `timestamp` older than 24h → flag it. If the session input supplied `seconds_since_last_response`, show it as idle time; it is informational only.
4. Drift check: frontmatter `branch`/`head` against the live git values; any mismatch is flagged.
5. Synthesise oldest → newest in ≤ 50 lines: where the work is, decisions made (as links), open decisions, the next action. Never paste snapshot text.
6. **Confirm gate:** ask David "Resume with: `<next-action>`?" and stop. No writes or dispatches until he confirms or redirects.

**C5. `/orient`** (project-only; read-only)
0. Root guard: `git rev-parse --show-toplevel` must succeed and the cwd must sit inside it. Any path argument resolving outside the root is refused. It reads whichever checkout the session is in (main or a feature worktree).
1. `CLAUDE.md`.
2. `docs/requirements.md`: headings plus the §6 open items.
3. The `Status:` line of each `.claude/specs/*.spec.md` (status line only).
4. `docs/plans/active/*.dag.yaml`: plan status, node statuses and each in-flight node's next task.
5. `git status -s`, branch, `git log --oneline -5`, `git worktree list` with every path relativised.
6. `gh pr list`. No remote or a `gh` failure → print "no remote", not an error.
7. `docs/trace/<plan>/<node>/` for the node in flight, if any.
8. Print an orientation summary of ≤ 60 lines.
9. Run the real `/load-context-chain` **verbatim** (no shadow, no summary of it). Its confirm gate is the final step: hold for David.

**C6. Privacy denylist**
- Two files, one pattern per line, same format:
  - `.claude/hooks/privacy-denylist.txt`: committed, **generic patterns only** (it is published with the repo).
  - `.githooks/privacy-denylist.local.txt`: **gitignored and optional**, holding private terms. Absent → only the committed file is used; never an error.
- Format: `#` comment, blank ignored, a line starting `!` is an allow pattern, every other line is a deny pattern. Patterns are POSIX ERE, matched case-insensitive, so `grep -E` and JS `RegExp` agree (no `\d`, no lookaround).
- Categories (the file holds the patterns; this spec never does, so the spec cannot trip its own scan):
  - Absolute user paths: Windows drive paths under a users or development root, Git Bash `/c/...` and WSL `/mnt/c/...` forms, and `/home/<name>` / `/Users/<name>`. Each requires a real path segment after the root, so placeholders such as `<user>` don't match.
  - Email addresses: a generic address regex. Allows: `noreply@anthropic.com`, `example.com`/`.org`, `git@github.com`.
  - Keys and tokens: Anthropic key prefix and `ANTHROPIC_*KEY`, GitHub token prefixes (`ghp_` family and fine-grained PATs), AWS access-key IDs, PEM private-key headers, storage account-key connection strings and SAS signatures, and `key|secret|token|password` assignments to a 16+ char value.
  - Private-workspace terms: names of private notes or private locations and their path fragments. These live **only in the local file**, never in the committed one. The prior project names agentkinOS and agentflow are allowed and are not listed.
- Scan: `F` is the concatenation of the committed file and, if present, the local file. A hit is a line matching any deny pattern and no allow pattern:
  `grep -nHiE -f <(grep -vE '^(#|!|$)' "$F") -- <files> | grep -viE -f <(sed -n 's/^!//p' "$F")`. Non-empty output = refuse.
- Consumers: `/save-context` step 7 (the draft) and the guardrails pre-commit hook (staged files; scope owned by guardrails.spec). Both read both files. **`.claude/hooks/privacy-denylist.txt` is excluded from the scan**, since it matches itself; the local file is never staged.
- `.gitignore` gains `.githooks/privacy-denylist.local.txt`.

**C7. SessionStart hook: `.claude/hooks/session-start.mjs`**
- Registration in `.claude/settings.json`: event `SessionStart`, matcher `startup|resume|clear|compact` (not `fork`), `{"type":"command","command":"node \"$CLAUDE_PROJECT_DIR/.claude/hooks/session-start.mjs\"","timeout":10}`.
- Node built-ins only (`node:fs`, `node:path`, `node:child_process` running `git` via `execFileSync`). No network, no writes, no `gh`.
- Reads stdin JSON (`source`, `cwd`, optional `seconds_since_last_response`). Unparseable input → `source=unknown`.
- Stdout is exactly one JSON object: `{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"<text>"}}`. `<text>` is truncated to 9,000 chars, keeping the last line:
  ```text
  [context] source=<source> branch=<branch> dirty=<n>[ idle=<h>h]
  [context] latest snapshot: docs/context/<file> (<age>h old) | none
  [context] next-action: <next-action key> | none
  [context] worktrees: <relative paths, comma-separated> | none
  [context] active plans: <plan file names> | none
  <instruction>
  ```
  `<instruction>` is `Run /orient before any work.` for startup/resume/clear/unknown, and `Run /load-context-chain and confirm with David before resuming; then /save-context.` for compact.
- Any failure (no git, no `docs/context/`, bad frontmatter) drops that line's value to `unknown`; the instruction line is always printed. **Always exits 0**, including on internal exceptions (top-level `try/catch`, `process.exitCode = 0`). It cannot block and never tries to run a skill.

**C8. Commit convention**
- Snapshots are written to the **current branch's** `docs/context/`. No hook or skill commits them.
- Each is committed in its own commit, `docs(context): <slug>`, with the trailers traceability.spec requires. Never inside a red/green/refactor commit.
- In a feature worktree, it lands on `main` with that feature's PR. Before F0b, commits on `main` are allowed. From F0b, a snapshot taken on `main` stays uncommitted and goes in the next feature branch's first `docs(context)` commit, so there's one chain on GitHub.
- No `checkpoints.md`: `grep -l '^checkpoint: true' docs/context/*.md` is the manifest.

**C9. F0a smoke tests** (all must pass before F0a closes)
- S1. `echo '{"source":"startup"}' | node .claude/hooks/session-start.mjs` → exit 0; stdout parses as JSON; `hookEventName` = `SessionStart`; text ends `Run /orient before any work.`; ≤ 10,000 chars.
- S2. Same with `"source":"compact"` → instruction names `/load-context-chain`.
- S3. `echo 'not json' | node …` and a run with cwd outside any git repo → both exit 0 and print the instruction line.
- S4. The S1 output piped through the C6 scan → no hits (worktree paths are relative).
- S5. Denylist: one sample per category, built at runtime by string concatenation into a temp file (never committed) → each is a hit; the allowed addresses → no hit; staging `.claude/hooks/privacy-denylist.txt` doesn't trip pre-commit.
- S6. `/save-context` with a planted absolute path → refused, nothing written. Clean run → file written, all 15 keys present, all 7 sections present, ≤ 200 lines, nothing staged.
- S7. `/load-context-chain` with 0 snapshots → "no prior context" plus the confirm gate. With 2 chained snapshots → oldest → newest synthesis, stopping at the checkpoint.
- S8. `/orient` given a path outside the repo root → refused.
- S9. Live: David opens a new session in the repo → the first reply reflects the `[context]` lines and asks to run `/orient` (manual check, recorded in the F0a trail).

## Open questions

1. Resolved (Decision): `/orient` reads whichever checkout the session is in, refused outside the repo root (C5 step 0).
2. Resolved (Decision): only the main session saves snapshots (C3).
3. Resolved (Decision): decisions live in the specs; there is no `docs/decisions.md` (C2 section 6).
4. Resolved by test: the hook is Node and emits `hookSpecificOutput` JSON; injection is verified by S1 and S9.
5. Resolved: `seconds_since_last_response` is shown as `idle=` and in the C4 staleness line, informational only; snapshot age stays the staleness rule.
6. Resolved (David, 2026-10-17): two files. The committed `.claude/hooks/privacy-denylist.txt` holds generic patterns only; a gitignored, optional `.githooks/privacy-denylist.local.txt` holds private terms; `/save-context` and the pre-commit hook read both (C6).
