# context.spec.md
Status: RESEARCH COMPLETE — decision pending
Researched 2026-10-17 · source of truth: `docs/requirements.md` §4 row 0

## Dependencies

**Upstream (relied on, not designed here)**
- `docs/requirements.md` — read by `/orient`; rule IDs referenced in snapshots.
- Repo layout §3: snapshots in `docs/context/` (project records, committed), skills in `.claude/skills/`, hooks in `.claude/hooks/`, registration in `.claude/settings.json`.
- Claude Code 2.1.302 (installed). Git Bash is present, so it is the default hook shell (see Research R1).
- Tools found on PATH: `git`, `gh`, `node`, `dotnet`.

**Consumed paths (owned by other specs; /orient reads them and never defines their shape)**
- `docs/plans/active/*.plan.md` (YAML DAG: node status). Owner: `planning.spec`.
- `docs/trace/<plan-id>/<node-id>/`, the trail for the node in flight. Owner: `traceability.spec`.
- `git worktree list`, the branch convention `feature/F<n>-<slug>` and the worktree path `../wt/F<n>-<slug>`. Owner: `orchestration.spec`.
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
- **D4 → B (bash).** Git Bash is the default hook shell, and requirements §5 already mandates LF for `*.sh`. Facts plus a prompt beats a bare prompt. Add a one-line smoke test (`echo '{"source":"startup"}' | .claude/hooks/session-start.sh`) to avoid agentflow's silent format bug.
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

## Contract — PENDING (DRAFT outline only, not yet binding)

```text
DRAFT · docs/context/<YYYYMMDD-HHMMSS>-<agent>-<slug>.md   (UTC, slug ≤40 kebab)
---
session-id, timestamp (from `date -u`, never estimated), agent,
previous-context (basename|null), checkpoint, status (in-progress|blocked|final),
plan-id (build|feat-NNN|bug-NNN|inv-NNN|null), node-id|null,
phase, step|null, attempt (1–5)|null, branch, worktree (relative)|null,
head (short sha), tests (X/Y|not-run)
---
## Summary · ## Completed since last snapshot · ## Current state (blockers)
## Next action (the first line is what the hook echoes) · ## Open questions
## Decisions made (link docs/decisions.md entries, do not restate them)
## Pointers (plan file, trace dir, PR #) · all present, (none) if empty

DRAFT · /save-context [--checkpoint] [--status ...] [--brief ...]
 1 real clock  2 tail = sorted-last docs/context/*.md  3 gather git + plan facts
 4 checkpoint rule (D2)  5 compose  6 cap check  7 privacy check (D6) → refuse on hit
 8 write; never commit; report path, lines/cap, checkpoint, privacy: clean

DRAFT · /load-context-chain [--latest-only] [--max-depth N=5]
 read-only · walk to checkpoint/depth/null/broken · synthesise ≤50 lines
 oldest→newest · staleness >24h (or seconds_since_last_response) · CONFIRM gate

DRAFT · /orient  (project-only; any path outside the repo root is refused)
 1 CLAUDE.md  2 docs/requirements.md (headings + §6 open items)  3 .claude/specs/ status lines
 4 docs/plans/active/* node statuses  5 git status/branch/log -5, worktree list (relativised)
 6 gh pr list (or "no remote")  7 docs/trace/<plan>/<node>/ for the node in flight
 8 run /load-context-chain VERBATIM → hold for David's confirmation  9 synthesis ≤60 lines

DRAFT · .claude/hooks/session-start.sh  (LF; registered SessionStart, matcher startup|resume|clear|compact)
 stdin JSON → source · prints ≤10k-char plain text: newest snapshot + age + Next action,
 branch, dirty count, worktrees (relative), active plans, then
 "Run /orient" (or "Run /load-context-chain" when source=compact) · writes nothing · exit 0 always
```

## Open questions

1. Does `/orient` run inside a feature worktree session, or only from David's main checkout? That decides which `docs/context/` it reads (D8). It depends on orchestration.spec's worktree research (requirements §6 item 5).
2. Agent snapshots: should subagents (tester/builder) ever call `/save-context`, or only the orchestrator? This spec assumes only the orchestrator (`agent: orchestrator|claude|david`).
3. `docs/decisions.md` (used by the existing `/decide` skill name) is not in the §3 layout. Should it be added, or should decisions live inside specs only (§4: "decisions are recorded inside the spec")?
4. Untested: does the SessionStart plain-stdout injection work identically under Git Bash on this machine? Verify with the smoke test in F0.
5. Is the `seconds_since_last_response` staleness signal worth using in the hook, or is snapshot age enough?
