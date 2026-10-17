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

## t4 (part 1): stop re-prompt, Grep .ignore, CLAUDE.md, agent registration — 2026-10-17

**Setup**
- `.claude/hooks/probe-stop.mjs`, registered on SubagentStop in main's settings. It exits 2 once, with an instruction on stderr.
- Two temporary marker files: `CLAUDE.md` = `PROBE-MARKER-MAIN` in the main checkout, and `PROBE-MARKER-WT` in the worktree.
- `.ignore` = `.claude/specs/` in both.
- Three probe agents: `probe` and `probe-full` (no `tools` line) in main's `.claude/agents/`, and `probe-wt` in the worktree's only.
- All dispatched from inside the worktree. Receipt: [t4-hook-log.jsonl](t4-hook-log.jsonl).

| Question | Result | Evidence |
|---|---|---|
| Does SubagentStop exit 2 re-prompt the agent with stderr? | **Yes.** The first stop (20:51:26) was blocked. The agent continued with the stderr text as its instruction and appended `REPROMPTED`, and the second stop (20:51:29, `stop_hook_active`) passed. **But only the agent's final message reaches the caller.** The report it gave before the block was lost, so a SubagentStop gate must ask for the *whole* output to be restated, not just a fix. | log; the agent's two final messages |
| Does the Grep tool honour `.ignore`? | **Yes, for searches without a path.** With `.ignore` present, a no-path Grep for `contract final` found nothing; with it removed, the same Grep found all 11 specs. **An explicit `path: .claude/specs` bypasses it**, and so does Glob `.claude/specs/*.md`. `.ignore` hides the specs from casual search only; the PreToolUse deny is the real guard. | the probe's report; my controls at 20:51:52–55 |
| What is `agent_type` for a built-in agent? | `general-purpose`, the agent's name exactly. | SubagentStart, PreToolUse, SubagentStop |
| Are project agents added mid-session usable? | **No.** `probe`, `probe-full` and `probe-wt` all failed with "Agent type not found"; the agent list is read at session start. | dispatch errors at 20:50:49–52 |
| Does a subagent see the project `CLAUDE.md`? | **Not when it was created mid-session.** The subagent saw neither marker, but did see the user-level `~/.claude/CLAUDE.md`. Unresolved until a fresh session: are project CLAUDE.md files read only at startup, or never given to subagents? | the probe's report |
| Can a worktree session write outside its worktree? | **No.** Edit of `<root>/.claude/hooks/probe-stop.mjs` from inside the worktree was refused ("Edit the worktree copy"). Since main's settings and hook scripts are the ones in force (t3), agents working in a worktree can't reach the live harness by file tools. | tool error |
| Is `agent_type` on every subagent event? | **No (again).** There was a SubagentStop with an empty `agent_type` mid-run, during each dispatch (20:51:16, 20:51:39), plus t3's `ScheduleWakeup`. It looks like an internal helper agent. `agent_id` was present on all of them. | log |

**Not run:** workflow `agent()`, because running a workflow needs David's explicit opt-in and the specs say it's moot unless workflows are used.

**t4 part 2 (needs a fresh session started in the main checkout):**
- dispatch `probe` and `probe-full` for the project-agent `agent_type` value, and `probe-wt` to see whether worktree-only agents load
- check which `PROBE-MARKER` a subagent sees, from the main checkout and after `EnterWorktree`

**Design consequences (for David)**
- Orchestration's "the harness in force is the branch's copy" (R1 note) is **wrong** for settings: main's copy governs, and the worktree copies are inert. The harness lock's protection of worktree copies is therefore belt-and-braces, not the main defence. A hook change made on a feature branch has no effect until it is merged and the main checkout updated.
- The SubagentStop verdict gate (guardrails) works as designed: exit 2 re-prompts. Its stderr has to say "restate your complete verdict JSON", because only the final message survives.
- `.ignore` is a convenience, not a guard. The PreToolUse deny on `.claude/specs/` paths carries the "agents never read specs" law, and it has to match explicit `path` arguments and Glob patterns.
- Guards must detect a subagent by `agent_id`, not by `agent_type`. They should look up the role from `agent_type` when it is present, and fail closed (deny) when a subagent call has no role.
