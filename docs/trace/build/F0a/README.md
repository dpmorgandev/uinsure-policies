# build/F0a: Harness · owner: David · built in-session, no loop

Written by the main session at the end of F0a (t19). Unlike other node READMEs it isn't generated:
F0a has no loop events or verdicts to render.

## Completeness

`node .claude/scripts/render-trace.mjs check build F0a` → `OK (git-only, F0a)`.

**Hooks not yet live: witnessed by git and trailers only.** F0a built the hooks it is now guarded by.
Every F0a commit is on `main`, and the activity hook records only on `feature/*` and `bug/*` branches, so
F0a has no activity, loop or verdict receipts. Its evidence is the git history, the `plan(build/F0a)`
commit trail, and the receipts below. The guards went live part-way through: `guard-*` and
`gate-subagent` in t13, the settings rules in t14, the git hooks in t15. The harness lock and the
`main-locked` marker come in t20. F0b is the first fully witnessed node.

Re-verify: `git log --oneline --grep='(build/F0a)'`

## Receipts

| File | What it holds |
|---|---|
| [spikes.md](spikes.md) | t3–t5 spikes and their design consequences, plus [t3](t3-hook-log.jsonl), [t4](t4-hook-log.jsonl) and [t4b](t4b-hook-log.jsonl) hook logs |
| [tools.md](tools.md) | t6: Bicep CLI 0.48.1, Postman CLI 1.75.0; a local collection runs without login |
| [guards.md](guards.md) | t13–t16: guard hooks, settings rules, git hooks, the G13 self-test, with live receipts |
| [context.md](context.md) | t18: context smoke tests S1–S9 |

## F0a probes (guardrails manifest), each run once

| ID | Probe | Result | Where |
|---|---|---|---|
| G1 | An Edit from a project subagent is logged with its `agent_type`, plus one from a workflow `agent()` | **Project agent: pass.** `agent_type` = the agent's name, inside a worktree too, and the tester's writes were denied by role in t13. **Workflow `agent()`: not run.** Workflows aren't used | spikes t4; guards t13 |
| G2 | The tester adds a throwing stub; the red commit compiles and fails with `NotImplementedException` | **Partly.** A non-stub `src/` body from the tester was denied live, and the grammar has fixtures. The compile-and-fail half needs the solution, so it **moves to F0b** | guards t13 |
| G3 | SubagentStart/Stop fire with `agent_id` and `agent_type` inside a worktree | **Pass.** Both fired with both fields inside `spike-t3`. Live: a tester stop passed G3, with pre-existing dirty files excluded | t4b log; guards t13 |
| G4 | A main-session edit of `CLAUDE.md` prompts in bypass, and David denies it | **Pass.** The first prompt was approved by mistake and reverted; the second was **denied**. Subagent harness writes (`CLAUDE.md`, `.githooks/`) were denied live | guards t14, t13 |
| G5 | `gh repo edit` in bypass is denied | **Pass.** `gh repo edit --help` was denied by `guard-bash.mjs` (t19), as was raw `gh api` (t13). The settings deny sits behind the hook | this README; guards t13 |
| G6 | The git agent pushes a feature branch; David is prompted and denies, and the prompt names the agent | **Pass, with a stand-in agent.** Push, PR create and PR comment each prompted in bypass and were denied. A subagent's push prompt read "from the general-purpose agent". The `git` agent itself is first exercised in F0b. **Open:** whether a push inside a compound command prompts | spikes t5 |
| G7 | In a scratch clone, a hand-run push to `main` is rejected by pre-push | **Pass.** Marker-gated, as David ruled in t13. A push to locked `main`, and `HEAD:refs/heads/main` from a branch without the marker, were both rejected | guards t15 |
| G8 | After the marker commit, a test commit on `main` is refused | **Pass in a scratch clone** (t15, re-run in t20). The live check runs right after this README's commit, the marker commit, so its result is in the next context snapshot, carried to F0b's branch | guards t15, t20 |
| G9 | A loop commit is accepted, and `git log` shows its trailers | **Partly.** commit-msg accepted matching trailers and rejected missing or wrong ones in a scratch clone. The first real loop commit **moves to F0b** | guards t15 |
| G10 | The privacy scan blocks private content | **Pass, twice live.** It blocked this repo's own commit of a test fixture that held a literal user path (t16), and it refused a `/save-context` draft with a planted path (S6) | guards t16; context S6 |
| G11 | A compile error, a red test and a broken migration are each rejected at commit or push | **Moves to F0b.** It needs the solution. Hook bypass (`--no-verify`, `-n`, hooks-path overrides) was denied live | guards t13, t15 |
| G12 | — | Fixture: reviewer, security and investigator have exactly `Read, Grep, Glob` | guards t16 |
| G13 | The self-test fails with a hook unwired, and passes once it's rewired | **Pass.** 44/45 with an unwired file, 45/45 after removing it | guards t16 |
| G14 | `gh pr create` with a stale verdict is denied | **Partly live.** `gh pr create --help` on `main` was denied (no node branch, no verdicts; t19). The stale-verdict case passes its fixture, and its first live run is F0b | this README; guards t13 |

## F0a acceptance

| Acceptance item | Status |
|---|---|
| CLAUDE.md, six agents, our skills incl. architecture and /feature, Node hooks with a manifest and a passing self-test, sh git hooks | Done (t8–t16) |
| Spike: subagents inherit the worktree | Pass (t3) |
| Spike: `agent_type` in PreToolUse and SubagentStop for each agent | Pass for project agents (t4). Each of the six is first seen live in F0b |
| Spike: which settings.json governs inside a worktree | The main checkout's (t3), corrected to "reloads live" (t4–t9) |
| Spike: ask rules prompt in bypass | Pass (t5) |
| Spike: a subagent has CLAUDE.md loaded | Pass, read at session start (t4, corroborated in t13) |
| A failed spike is fixed or recorded as a design change | Every finding was ruled on by David (spike rulings, t13 rulings) |
| Harness lock live: a provoked subagent write to `.claude/agents/**` is denied, incl. inside a worktree | **Pass (t20).** The `builder` agent's Write to `.claude/agents/probe.md`, its Write to `.claude/worktrees/build-F0b/.claude/agents/probe.md`, and a Bash redirect into `.claude/agents/` were all denied under G4. Nothing was written, and its stop passed G3 |
| Postman CLI and Bicep CLI installed, versions recorded, local collection without login | Done (t6) |
| requirements.md updated | Done (t7) |
| docs/readme-checklist.md | Done (t17) |
| Context smoke tests S1–S9 | Done (t18). The S7 zero-snapshot case is a step-0 claim only |
| /plan-dag run, plan approved | Done (t2) |

**Moved to F0b** (needs the solution or a real loop): the G2 compile-and-fail half, G9's first loop
commit, the G11 build legs, G14's stale-verdict case, and the six agents' `agent_type` seen live.
