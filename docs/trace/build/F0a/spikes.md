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

Part 2 (below) ran in a fresh session started in the main checkout.

**Design consequences (for David)**
- Orchestration's "the harness in force is the branch's copy" (R1 note) is **wrong** for settings: main's copy governs, and the worktree copies are inert. The harness lock's protection of worktree copies is therefore belt-and-braces, not the main defence. A hook change made on a feature branch has no effect until it is merged and the main checkout updated.
- The SubagentStop verdict gate (guardrails) works as designed: exit 2 re-prompts. Its stderr has to say "restate your complete verdict JSON", because only the final message survives.
- `.ignore` is a convenience, not a guard. The PreToolUse deny on `.claude/specs/` paths carries the "agents never read specs" law, and it has to match explicit `path` arguments and Glob patterns.
- Guards must detect a subagent by `agent_id`, not by `agent_type`. They should look up the role from `agent_type` when it is present, and fail closed (deny) when a subagent call has no role.

## t4 (part 2): project agents and project CLAUDE.md, fresh session — 2026-10-17

**Setup**
- A fresh Claude Code session started in the main checkout, with the part 1 probe files still on disk (uncommitted) so they were loaded at startup.
- The probe-stop "once" marker was absent at start, so the stop probe was armed.
- Every probe got the same prompt: quote any `PROBE-MARKER` line from its context without reading a file, say whether the user-level instructions are present, then run `pwd`, `git rev-parse --show-toplevel` and `git rev-parse --abbrev-ref HEAD`.
- Round 1: `probe` and `probe-full` dispatched from the main checkout. Round 2: `EnterWorktree({path: .claude/worktrees/spike-t3})`, then `probe` and `probe-wt`.

Receipt: [t4b-hook-log.jsonl](t4b-hook-log.jsonl) (paths redacted to `<root>`).

| Question | Result | Evidence |
|---|---|---|
| What is `agent_type` for a project agent? | **The agent's `name:` exactly**: `probe` and `probe-full`, on SubagentStart, PreToolUse and SubagentStop. It's the same in the main checkout and inside a worktree. | log 20:56:13–22, 20:56:35–42 |
| Are project agents loaded at session start usable? | **Yes.** Both appeared in the agent list and dispatched. A missing `tools:` line (`probe-full`) makes no difference to registration. | dispatch results |
| Do agents defined only in a worktree's `.claude/agents/` load after `EnterWorktree`? | **No.** `probe-wt` failed with "Agent type not found", and the list it offered was the main checkout's roster. The roster comes from the launch directory at session start. | dispatch error 20:56:37 (PreToolUse, no SubagentStart) |
| Does a subagent have the project `CLAUDE.md` loaded? | **Yes, if it existed at session start.** Both main-checkout probes quoted `# PROBE-MARKER-MAIN` from their context without reading the file. | the probes' reports |
| After `EnterWorktree`, which `CLAUDE.md` does a subagent see? | **The main checkout's, not the worktree's.** The worktree `probe` ran in the worktree (`pwd` and toplevel were spike-t3, branch `spike/t3`) but quoted `PROBE-MARKER-MAIN`. The worktree's `CLAUDE.md` holds `# PROBE-MARKER-WT`, and no probe saw it. | the probe's report; `head -1 CLAUDE.md` in the worktree |
| Do subagents see the user-level `~/.claude/CLAUDE.md`? | **Yes**, all three, alongside the project file. | the probes' reports |
| SubagentStop exit 2 again | It re-prompted once more (`probe`, 20:56:20 blocked, 20:56:22 passed). This time the agent restated its whole report before `REPROMPTED`, but part 1 shows it can't be relied on to. | log; the report |
| Is `agent_type` on every subagent event? | **No (third time).** An empty-`agent_type` SubagentStop came with every dispatch round (20:56:18, 20:56:22, 20:56:24, 20:56:40). Each had an `agent_id` that matches no dispatched probe. | log |

**Unplanned finding**
- Inside the worktree, the isolation check refused a Bash pipeline into `node -e` again, as in t3. Plain separate commands (and the Read tool) worked. Hook scripts and agents that pipe into `node` inside a worktree need literal script paths.

**Not run:** workflow `agent()`, as in part 1.

**Design consequences (for David), part 2**
- `agent_type` = the agent file's `name:`, so the guard's role table keys on the six roster names. Combined with part 1, a subagent is detected by `agent_id`, the role comes from `agent_type`, and an empty role fails closed.
- The harness (agents, `CLAUDE.md`, settings and hook scripts) is fixed at session start from the **main checkout**. Worktree copies of all four are inert. A change to any harness file only takes effect after it is merged to main **and a new session starts**. This extends the part 1 R1 correction from settings to agents and `CLAUDE.md`.
- The `/feature` orchestrator must run from a session launched in the main checkout. Launching a session directly inside a worktree is a different case and wasn't tested.
- Subagents get the project `CLAUDE.md` and the user-level one. The project `CLAUDE.md` is therefore a channel to every agent and must stay contract-only (t8). The user-level file leaking into subagents is outside the repo's control and gets recorded in the README.

## t5: ask rules in bypass mode — 2026-10-17

**Setup**
- The session ran in `bypassPermissions` (as recorded in the transcript).
- The G6 rules were added temporarily to the main checkout's `.claude/settings.json`: `"ask": ["Bash(git push *)", "Bash(gh pr create *)", "Bash(gh pr comment *)"]`, with no restart.
- Every probe command was harmless even if run: `git push --dry-run origin HEAD:refs/heads/t5-probe`, `gh pr create --dry-run --head main --base main …`, and `gh pr comment 999999 …`.
- **An approval is indistinguishable from no prompt** (the command just runs), so the receipts are denials: David chose No, and the tool result came back "The user doesn't want to proceed with this tool use".

| Question | Result | Evidence |
|---|---|---|
| Does `ask` on `gh pr comment *` prompt in bypass? | **Yes.** Prompted; David denied; the command didn't run. | tool rejection |
| Does `ask` on `git push *` prompt in bypass? | **Yes.** Prompted; denied. | tool rejection |
| Does `ask` on `gh pr create *` prompt in bypass? | **Yes.** Prompted; denied. | tool rejection |
| Does it prompt for a subagent's call, and name the agent? | **Yes.** A `general-purpose` subagent's `git push --dry-run` prompted as "Bash command · from the general-purpose agent"; denied. | David's report of the prompt; the subagent's result |
| Do `permissions` edits apply without a restart? | **Yes**, as with hooks in t3. The rules were live within the same session. | the prompts |

**Unplanned finding**
- A first, informal pass (David approving prompts) left no usable receipt: two dry-run pushes and the gh calls all ran, and it wasn't possible afterwards to tell which had prompted. The transcript doesn't record permission decisions. **The receipt for an ask rule has to be a denial.** The F0a smoke tests and the G6 live check should be specified as "David denies; the tool result shows the rejection".

**Design consequences (for David)**
- G6 holds as designed: `ask` rules prompt in bypass, for the main session and for subagents, and the prompt names the subagent, so David can see which agent wants to push.
- The temporary rules were removed. t14 adds the full G5–G7 deny and ask set.
