# traceability.spec.md
Status: RESEARCH COMPLETE — decision pending
Researched: 2026-10-17 · Input: `docs/requirements.md` §4 row 6b, §6 item 6 · Environment checked: Claude Code 2.1.302, git 2.49.0

Scope: three trails keyed `docs/trace/<plan-id>/<node-id>/`, which form one chain: rule ID → plan → node → tests + code → verdicts → QA sign-off → PR.
- **Requirement trail:** rule → tests → code → QA step → sign-off
- **Activity trail:** agent → files → loop → commit
- **Verdict trail:** verdicts, bugs, loops

The scope also covers the cross-links (investigation → plans, bug → broken rule + originating plan/node), and the split between what is generated mechanically and what is written by an agent. **A self-reported trail isn't a receipt.**

## Dependencies

| Spec | Assumption (not decided there yet) | If it breaks |
|---|---|---|
| `orchestration` | Skill-driven loop (its A2). The main session runs inside the worktree via `EnterWorktree`. Loop events go to `loop.jsonl`. Branch `feature/<plan>/<node>`. Merge commit, so SHAs survive. Verdict JSON schema v1 | Under squash merge the commit-level trail dies (it lives only in the PR). Under rebase-merge, SHAs in the trail go stale: key on trailers, not SHAs |
| `agents-skills` | Every agent is a named file in `.claude/agents/`, so `agent_type` = `tester`/`builder`/`reviewer`/`security`/`git`. The orchestrator is the main session (no `agent_type`) | Unnamed agents arrive as `general-purpose`, and identity becomes ambiguous |
| `testing` | xUnit tests carry a rule marker: `[Trait("Rule","SELL-2")]`, or a custom `[Rule("SELL-2")]` trait attribute. QA script steps are tagged with rule ids. Rule filter command exists | The rule → test link falls back to test-name conventions (weaker) |
| `planning` | Plan YAML lists rule ids per node and task. Investigation output lists recommended plans. Bug plans carry `breaks:` and `introduced_by:` | Cross-links lose their anchors |
| `guardrails` | Denies agent Edit/Write to `docs/trace/**` and `git add -A`. Implements the commit-trailer check | Trail files become editable by producers, so they would be self-report again |

## Research

### R1. Does hook input identify the subagent? (open item §6.6) **Yes, documented.**
- From [hooks](https://code.claude.com/docs/en/hooks), common input fields:
  - `agent_id`: "Unique identifier for the subagent or in-process teammate the hook fires in"
  - `agent_type`: "Agent name (for example, `"Explore"` or `"security-reviewer"`). Present when the session uses `--agent` or the hook fires inside a subagent."
  - Verbatim: "When a subagent calls a tool, tool events such as `PreToolUse` and `PostToolUse` fire the same configured hooks as in the main conversation, and the input carries the `agent_id` and `agent_type` common input fields that identify the subagent."
  - **Absent ⇒ the main session** (the orchestrator).
- **PostToolUse input** carries `session_id, transcript_path, cwd, tool_name, tool_input, tool_response, tool_use_id, duration_ms` plus the agent fields.
- **SubagentStart** carries `agent_id, agent_type`. **SubagentStop** adds `agent_transcript_path` (`…/subagents/agent-<id>.jsonl`), `last_assistant_message` and `stop_hook_active`. SubagentStop **can block** (exit 2 / `decision:block`), so it can refuse a malformed verdict.
- **Consequence:** the activity trail can be **mechanical**. A hook, not the agent, writes `{agent_type, tool, file}` for every Edit/Write/Bash. The agent cannot omit or forge its own identity.
- **Limits, stated honestly:**
  - For `Bash`, the hook sees the *command text*, not the files it wrote. File attribution for shell writes comes from git (`git show --name-only`), not from the hook.
  - **Unverified:** the `agent_type` value for *workflow* agents (`agentType:` option, and the default workflow subagent). It is irrelevant if orchestration picks the skill route, and it is a spike item otherwise.
  - `cwd` in hook input is the **worktree** after `EnterWorktree`, while `${CLAUDE_PROJECT_DIR}` stays at the main checkout ([worktrees](https://code.claude.com/docs/en/worktrees)). Hooks must write under `git -C <cwd> rev-parse --show-toplevel`, or the trail lands in David's checkout, which §3 forbids.

### R2. Commit trailers: verified locally (git 2.49.0, scratch repo)
- **Write:** `git commit -m "…" --trailer "Agent: tester" --trailer "Plan: build" --trailer "Node: F1" --trailer "Rule: SELL-2" --trailer "TDD: red"` works (git ≥ 2.32).
- **Query, as a table:** `git log --format='%h %(trailers:key=Agent,valueonly,separator=%x2C) %(trailers:key=TDD,valueonly,separator=%x2C) %(trailers:key=Rule,valueonly,separator=%x2C) %s'`. Gotcha: without `separator=`, `valueonly` appends a newline.
- **Query, by rule:** `git log --grep='^Rule: SELL-2$'` returned exactly the two commits.
- **Parse one:** `git log -1 --format=%B | git interpret-trailers --parse` returns `Key: value` lines.
- **The payoff for bugs:** `git blame -L <line> <file>` → commit → `Plan:`/`Node:` trailers. That is how `/plan-bug` finds the plan and node that introduced the faulty code (§3 Planning model), and it is mechanical.
- **Trailers are self-reported text**, unless a hook checks them. PreToolUse fires on the `Bash` call that runs `git commit`, and it has `agent_type` and `cwd`. It can therefore **deny** a commit whose `Agent:` ≠ `agent_type`, or whose `Plan:`/`Node:` ≠ the branch `feature/<plan>/<node>`. Deny-with-reason is more robust than rewriting the command via `updatedInput` (which can't handle heredoc/`-F` message forms). Result: `Agent/Plan/Node` are **mechanically guaranteed**. `Rule/TDD` are declared by the agent, but **cross-checked** by gates: a `TDD: red` commit must touch only `tests/**` and the rule filter must fail on it; `green` must touch only `src/**` and pass. Also, `Rule:` must match a rule marker added or used in the diff.
- **SHAs:** our own pre-PR rebase onto `origin/main` can change SHAs, and GitHub's rebase-merge always does. So trail files written *during* the loop **reference trailers + tool_use_id, not SHAs**. SHAs are resolved when the summary is generated, after the final rebase. With a merge-commit PR they then stay valid on `main`.

### R3. Rule → test mapping (xUnit)
- **Option 1:** `[Trait("Rule","SELL-2")]`. It needs no code, filters with `dotnet test --filter "Rule=SELL-2"` (VSTest) or `--filter-trait "Rule=SELL-2"` (xUnit v3 native / MTP), and is greppable: `rg -n 'Trait\("Rule", ?"([A-Z]+-\d+[a-z]?)"\)' tests/`.
- **Option 2:** a custom `[Rule("SELL-2")]` attribute. It is less noise and type-checked. In xUnit v3 a trait attribute implements `ITraitAttribute.GetTraits()`, a few lines. **Exact v3 API to be confirmed in the testing spec**: not verified here.
- **Which tests ran and passed:** use a TRX/JUnit report from the full-suite run. Whether the trait is carried into the TRX under MTP is **unverified**. The robust path is a two-step join: rule → test names via grep on source (static), then test name → outcome via the TRX (dynamic).
- **Coverage gap check (mechanical):** every rule id in requirements.md §2 must have ≥ 1 test with its marker, unless the trail says it is out of scope for the node. Listing the rule ids is `rg -o '^\| ([A-Z]+-[0-9]+[a-z]?(/[0-9])?) \|' docs/requirements.md`. Note `RENEW-1/2` is a compound id, and testing must pick `RENEW-1` + `RENEW-2` or keep it compound.

### R4. Prior art
- **agentkinOS `trace-agent-activity.mjs`:** a hash-chained, append-only, **fail-closed** PreToolUse witness of `Agent` dispatches (`prompt_sha256`, `seq`, `prev`).
  - **Its own header records the failure that matters most:** after the `Task`→`Agent` rename, the guard still matched only `Task`. So "from the rename until 2026-09-03 the hook fired on every dispatch and exited on line one: `agent-activity.jsonl` did not exist anywhere in the repo despite thousands of dispatches."
  - **Lesson:** an empty trail must be *detectable as wrong*. A completeness check has to compare the trail against an independent count, here the git commits and loop events.
- **agentflow `track-read.mjs` + `verify-gate.mjs`:** a PostToolUse logger plus a PreToolUse deny ("can't flip `passes:true` without reading evidence this session"). It ports directly as "can't open a PR without receipts for HEAD".
- **agentflow `evidence/phase0-step-NNN-result.txt`:** prose evidence files ("Verification: … Verdict: GO") were *written by the agent that did the work*. That is readable, but it is self-report. Keep the readability and drop the authorship.
- **agentflow planning completion records:** built from `git log main..branch` / `git diff --name-status`. Git facts, not claims. Same approach here.

### R5. Interviewer readability
An interviewer will open the repo on GitHub, not run scripts. So every trail needs a **rendered markdown** face. JSONL is the machine receipt, and the markdown is generated from it. The rendered file must say *how it was generated*, so a reader can tell receipt from narrative. One `docs/trace/README.md` (≈30 lines) explains how to read the trails and how to re-verify them with the `git log` commands above.

## Options

**A. Activity trail mechanism**
- A1 PostToolUse hook only: JSONL of `{agent_type, tool, file|command}`
- A2 Commit trailers only: self-reported unless checked
- A3 **Both**, with trailers checked by a PreToolUse hook against `agent_type`, and the hook log cross-checked against `git show --name-only` per commit at render time

**B. Where in-loop trail files live**
- B1 Uncommitted in the worktree's `docs/trace/<plan>/<node>/`, committed once by the git agent as `chore(trace)` at INTEGRATE. TDD commits stay clean, because the guard forbids `git add -A` and staging `docs/trace/**` in TDD commits.
- B2 The per-worktree git dir (`$(git rev-parse --git-dir)/trace/`, outside the working tree), copied into `docs/trace/` at INTEGRATE.
- B3 Committed alongside each TDD commit (rejected: pollutes the red/green/refactor diffs the interviewer will read).

**C. File format per node**
- C1 JSONL receipts plus a generated `README.md` summary
- C2 Markdown only, agent-written (rejected: self-report)
- C3 A single repo-level DB/JSON (rejected: unreadable on GitHub, merge conflicts)

**D. Tamper evidence for JSONL during the loop**
- D1 A guardrail deny on Edit/Write to `docs/trace/**`, plus git once committed
- D2 D1 plus agentkinOS's hash chain (`id = sha256(prev + canonical(record))`, ~25 lines to port)

**E. Rule marker:** E1 `[Trait("Rule",…)]` · E2 custom `[Rule(…)]` (testing spec decides; traceability needs only "greppable + filterable")

## Recommendation

- **A3.** The hook gives identity the agent can't forge. Trailers make it queryable with plain git forever, including by an interviewer with no harness. The cross-check turns two self-describing sources into one receipt.
- **B1.** The files are visible in the worktree for HITL. One `chore(trace)` commit shows up in the PR as a separate, skippable diff. B2 hides them during the loop for no real gain.
- **C1.** Split as follows:

| Generated (by hook or script, never by a producer agent) | Written (by an agent or David, labelled as such) |
|---|---|
| `activity.jsonl` (PostToolUse hook) | investigation findings (`inv-NNN.plan.md`, agent-written reasoning) |
| `verdicts.jsonl` (SubagentStop hook stores the validated verdict JSON verbatim + `agent_transcript_path`) | the verdict *content* (the reviewer's judgement: the hook records it, it doesn't vouch for it) |
| `loop.jsonl` (orchestrator events + gate results; gate results come from commands the orchestrator ran) | QA script ticks + sign-off (David) |
| `bugs/A<n>.md` (rendered from the verdict JSON) | PR "what to look at" paragraph (orchestrator, labelled) |
| `README.md` per node, `docs/trace/requirements-matrix.md` (render script over JSONL + git + test source + TRX) | `docs/lessons.md` entries |

- **D1** for F0/F1. D2 only if a producer is ever caught editing the trail. Guard against over-building: the hash chain isn't used by F0/F1 unless it earns its place.
- **A completeness check is non-negotiable** (the agentkinOS lesson). The render script **fails** (rendering `TRAIL INCOMPLETE`, never a clean summary) when any of these hold:
  - a commit on the branch has no matching activity record by its `Agent`
  - a REVIEW pass has no verdict pair
  - `activity.jsonl` is empty while commits exist
  - `checked` misses one of the node's rules

## Decision — DECIDED 2026-10-17 (David)
- [x] All eight items accepted as recommended:
  - A3: an activity hook, trailers, and a PreToolUse trailer check.
  - B1: one `chore(trace)` commit before the PR.
  - C1: JSONL receipts plus generated markdown.
  - The hash chain is deferred.
  - Trailers are `Agent, Plan, Node, Rule, TDD` (+ `Fixes-Finding`), and the hook checks `Agent/Plan/Node`.
  - The rule marker is `[Trait("Rule","ID")]` (testing).
  - The completeness check fails loudly.
  - The PR body is generated from the trail.
- [x] **Added (planning):** each node also renders `walkthrough.md`. It lists every commit in order (red, stub, domain, services, persistence, refactor), each with one line on what it did. David uses it at pause 2.

## Contract — PENDING (DRAFT outline)

### Layout
```text
docs/trace/
  README.md                         how to read + re-verify (git commands), what is generated vs written
  requirements-matrix.md            generated: every rule id → tests → commits → QA step → sign-off → PR
  <plan-id>/<node-id>/
    README.md                       generated node summary (the interviewer's entry point)
    loop.jsonl                      orchestrator events + gate receipts (state is derived from the last event)
    activity.jsonl                  PostToolUse hook: one line per Edit/Write/Bash by any agent
    verdicts.jsonl                  SubagentStop hook: validated verdict JSON per reviewer/security run
    bugs/A<n>.md                    rendered bug report per FAIL attempt
docs/qa/<plan-id>-<node-id>.md      QA script, steps tagged [SELL-2], sign-off block (David)
```

### Record shapes (DRAFT; every line has `v`, `ts` (real clock, ISO-8601 UTC, written by the hook/script), `plan`, `node`)
```jsonc
// activity.jsonl (PostToolUse, matcher "Edit|Write|Bash|PowerShell")
{"v":1,"ts":"…","plan":"build","node":"F1","agent":"builder","agent_id":"a1b2…","tool":"Edit",
 "file":"src/Policies.Domain/Sale.cs","command":null,"tool_use_id":"toolu_…","attempt":1}
// agent = agent_type, or "main" when absent; plan/node parsed from branch; attempt read from the last loop.jsonl event
// Bash: "file":null, "command":"<first 200 chars>"; a `git commit` line also records "head_after":"<sha>"

// loop.jsonl (orchestrator events; gate lines carry the command it ran and its exit code)
{"v":1,"ts":"…","plan":"build","node":"F1","event":"gate.red","attempt":1,
 "cmd":"dotnet test … --filter Rule=SELL-2","exit":1,"expect":"fail","ok":true,"head":"<sha>"}
// events: setup · state(<STATE>) · gate.red · gate.green · verdict.ref · bug.raised · attempt++ ·
//         investigation.opened(inv-NNN) · escalated · gate.integrate · pr(url) · signoff(sha) · merged(sha) · archived

// verdicts.jsonl (SubagentStop): {"v":1,"ts":"…","agent":"reviewer","agent_id":"…",
//   "transcript":"…/subagents/agent-<id>.jsonl","valid":true,"verdict":{…schema v1 from orchestration…}}
```

### Requirement trail: how each link is established
| Link | Source | Mechanical? |
|---|---|---|
| rule → plan/node | plan YAML `rules:` per node/task | written (plan, approved by David) |
| rule → tests | rule marker in test source (grep) | **yes** |
| tests → result | TRX from `gate.integrate` full-suite run | **yes** |
| rule → code | commits with `Rule:` trailer → `git show --name-only` (src only) | trailer declared, cross-checked by the TDD gates |
| rule → QA step | `[RULE-ID]` tag on QA script steps | written, checked: every node rule has ≥ 1 QA step |
| QA → sign-off | QA sign-off block: David, date, `signed_sha` | David (human receipt, by design) |
| node → PR → merge | `loop.jsonl` `pr`/`merged` + `gh pr view --json mergeCommit` | **yes** |

### Cross-links
- **Investigation → plans:** `inv-NNN.plan.md` frontmatter `recommended: [{kind: feature|bug|amend-node, id: feat-002|null, reason: "…"}]`. When David opens one, the new plan's frontmatter gets `recommended_by: inv-NNN`, and the `id` is filled in on the investigation. The render script checks the link both ways.
- **Bug → rule + origin:** `bug-NNN.plan.md` frontmatter `breaks: SELL-2`, `introduced_by: {plan, node, commit}`. `introduced_by` is **derived** with `git blame` → commit → `Plan:`/`Node:` trailers, and the command is recorded in the plan so it can be re-run.
- **Verdict → bug report → red test:** finding `R1` → `bugs/A1.md#R1` → a commit with `Fixes-Finding: R1` and `TDD: red`.

### Per-node README.md (generated; this is what the interviewer reads)
```text
# build/F1 (Sell) · status: merged · PR #3 · merge <sha>
Generated by .claude/hooks/render-trace.mjs from loop/activity/verdicts JSONL + git; do not edit.
## Rules → tests → commits      (table: rule | tests (pass/fail) | red sha | green sha | refactor sha | QA step)
## Loop                          attempt 1: FAIL (reviewer R1 SELL-5 boundary) → A1.md · attempt 2: PASS/PASS
## Who touched what              (table: commit | Agent trailer | files | hook-log agent | match ✓/✗)
## Gates                         red ✓ · green ✓ · integrate ✓ (full suite 142/142, <sha>) · sign-off David <date> <sha>
## Completeness                  OK  |  TRAIL INCOMPLETE: <reasons>   ← never omitted
```

### PR description (generated from the node README, plus one labelled human paragraph)
Summary (node title + rules) · Rules → tests → commits table · Attempts and verdicts (with links to `bugs/A<n>.md`) · How to run (`dotnet run … --urls http://localhost:5080`, Postman env) · QA script link and sign-off status · Completeness line · "Reviewer notes (orchestrator-written)" paragraph.

## Open questions
1. **requirements.md §6.6 can be closed:** hook input does identify the subagent (`agent_type`/`agent_id`), so the activity trail is mechanical. Recommend rewording §4 row 6b's "candidate mechanisms" to the decided A3.
2. Spike: confirm `agent_type` for agents in this exact setup, i.e. after `EnterWorktree` and on Windows, with `PowerShell` and `Bash` both present. Confirm too that hook `cwd` is the worktree for subagent calls. It takes 10 minutes and is the first F0 task, alongside orchestration's R1 spike.
3. F0 is the node that builds these hooks, so its own trail can only be git + trailers (unchecked), plus a hand-noted "hooks not yet live". Accept that and label it in F0's README rather than backfilling? Recommendation: accept and label.
4. Should `activity.jsonl` log `Read`/`Grep`? That is useful evidence that a reviewer actually read the code (agentflow `track-read`), but it is noisy. Suggested: log reads only for `reviewer`/`security`, which supports "the loop ran" (`checked` vs files actually read).
5. Is the TRX trait carried under xUnit v3 + MTP? (testing spec). If not, the rule → result join goes via test names.
6. Public repo: activity records hold file paths and command prefixes only, never prompt text (agentkinOS hashed prompts for the same reason). Confirm there are no absolute user paths (`C:/Users/<user>/…`): store paths relative to the repo root.
