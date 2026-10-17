# build/F0a: spike results

Hooks were not yet live for these runs; the evidence is the probe log and git (F0a exception).
The probe is `.claude/hooks/probe-log.mjs` (67a1fad), a logging-only hook registered for PreToolUse, SubagentStart and SubagentStop. Each registration is labelled with the settings file that holds it.

## t3: worktree inheritance, hook cwd, which settings.json governs — 2026-10-17

**Setup**
- `git worktree add .claude/worktrees/spike-t3 -b spike/t3 HEAD`.
- In the worktree's copy of `.claude/settings.json`, the probe label was changed from `main` to `wt`.
- The session then ran `EnterWorktree({path})`, the same route orchestration step 1 uses.
- A `general-purpose` subagent was dispatched with only bare instructions.

Receipt: [t3-hook-log.jsonl](t3-hook-log.jsonl) (paths redacted to `<root>`).

| Question | Result | Evidence |
|---|---|---|
| Does the main session move into the worktree? | **Yes.** Bash `pwd` and `git rev-parse --show-toplevel` both give `<root>/.claude/worktrees/spike-t3`, on branch `spike/t3`. | log lines at 20:46:33 |
| Does a subagent dispatched after `EnterWorktree` inherit the worktree? | **Yes.** The subagent's Bash cwd and toplevel are the worktree, and its relative `Write spike-t3-subagent.txt` landed in the worktree, not in `<root>`. `<root>/spike-t3-subagent.txt` does not exist. | subagent report; log 20:46:40–42; `git status` in the worktree shows `?? spike-t3-subagent.txt` |
| What is hook `cwd` on Windows, for subagent calls? | **The worktree**, as a Windows path (`C:\…\.claude\worktrees\spike-t3`). The hook process cwd is the worktree too. | every subagent line |
| Which `settings.json` governs inside a worktree? | **The main checkout's.** Every event, in both the main session and the subagent, carried label `main`, never `wt`. That still held after the worktree copy was edited again from inside the worktree (label `wt2`, then a tool call). `CLAUDE_PROJECT_DIR` stays `<root>`, so hook scripts also resolve to main's copies. | all lines; 20:47:15–16 |
| Do settings edits apply without a restart? | **Yes**, for the main checkout's file. The probe fired on the first tool call after the edit. | log line at 20:46:23 |

**Unplanned findings**
1. **`agent_type` is not on every subagent call.** One PreToolUse (`ScheduleWakeup`) and one SubagentStop carried an `agent_id` but no `agent_type`, or an empty one. A guard keyed on "is `agent_type` set" would treat those calls as the main session. Distinguishing a subagent from the main session needs `agent_id` (present on every subagent event, absent from every main-session event). This feeds t4 and guardrails G1/G4.
2. **The subagent's context included the user-level `~/.claude/CLAUDE.md`** (the operator's personal context), because there is no project `CLAUDE.md` yet. So whether a *project* `CLAUDE.md` loads is still open for t4.
3. **Session isolation.** Inside a worktree, Claude Code refused a Bash command whose program it couldn't statically resolve (`node` with a runtime-computed argument), because it couldn't prove the command didn't target git outside the worktree. Literal paths ran fine.
4. **Subagent hook input** includes `agent_transcript_path` and `last_assistant_message` on SubagentStop. That's useful for G3, and for verdict validation without re-reading the transcript.

**Design consequences (for David)**
- Orchestration's "the harness in force is the branch's copy" (R1 note) is **wrong** for settings: main's copy governs, and the worktree copies are inert. The harness lock's protection of worktree copies is therefore belt-and-braces, not the main defence. A hook change made on a feature branch has no effect until it is merged and the main checkout updated.
- Guards must detect a subagent by `agent_id`, not by `agent_type`. They should look up the role from `agent_type` when it is present, and fail closed (deny) when a subagent call has no role.
