# traceability.spec.md
Status: DECIDED — contract final (2026-10-17)
Researched: 2026-10-17 · Input: `docs/requirements.md` §4 row 6b, §6 item 6 · Environment checked: Claude Code 2.1.302, git 2.49.0

Scope: three trails keyed `docs/trace/<plan-id>/<node-id>/`, which form one chain: rule ID → plan → node → tests + code → verdicts → QA sign-off → PR.
- **Requirement trail:** rule → tests → code → QA step → sign-off
- **Activity trail:** agent → files → loop → commit
- **Verdict trail:** verdicts, bugs, loops

The scope also covers the cross-links (investigation → plans, bug → broken rule + originating plan/node), and the split between what is generated mechanically and what is written by an agent. **A self-reported trail isn't a receipt.**

## Dependencies

| Spec | Assumption (not decided there yet) | If it breaks |
|---|---|---|
| `orchestration` | Skill-driven loop (its A2). The main session runs inside the worktree via `EnterWorktree`. Loop events go to `loop.jsonl`. Branch `feature/<plan>/<node>`. Merge commit, so SHAs survive. Verdict JSON schema v1. The git agent posts verdicts as PR comments | Under squash merge the commit-level trail dies (it lives only in the PR). Under rebase-merge, SHAs in the trail go stale: key on trailers, not SHAs |
| `agents-skills` | Every agent is a named file in `.claude/agents/`, so `agent_type` = `tester`/`builder`/`reviewer`/`security`/`investigator`/`git` (six agents). The orchestrator is the main session (no `agent_id`; F0a spike rulings) | Unnamed agents arrive as `general-purpose`, and identity becomes ambiguous |
| `testing` | xUnit tests carry a rule marker: `[Trait("Rule","SELL-2")]`, or a custom `[Rule("SELL-2")]` trait attribute. QA script steps are tagged with rule ids. Rule filter command exists | The rule → test link falls back to test-name conventions (weaker) |
| `planning` | Plan YAML lists rule ids per node and task. Investigation output lists recommended plans. Bug plans carry `breaks:` and `introduced_by:` | Cross-links lose their anchors |
| `guardrails` | Denies agent Edit/Write to `docs/trace/**` (the investigator stays read-only; a hook files its findings) and `git add -A`. Implements the commit-trailer check | Trail files become editable by producers, so they would be self-report again |

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
- [x] **Final rulings (David, 2026-10-17):**
  - OQ4: `activity.jsonl` also logs `Read`/`Grep` calls, for `reviewer`, `security` and `investigator` only.
  - OQ8: only reviewer, security or gate FAILs count as attempts (`A<n>`). David's steers at Pause 1, Pause 2 and in PR comments don't increment the attempt; each gets the next id `D1`, `D2`… on the node, with `findings/D<n>.md` and `verdicts/D<n>-<agent>.json`, so nothing collides with `A<n>`.
  - Accepted as proposed: the `TDD` trailer value `stub` for the stub-controller commit.

## Contract

### Layout
```text
docs/trace/
  README.md                       written once: how to read the trails, how to re-verify with git, generated vs written
  requirements-matrix.md          generated: rule → tests → commits → QA row, for every rule id in requirements.md
  <plan>/<node>/
    activity.jsonl                generated (PostToolUse hook): one line per Edit/Write/Bash/PowerShell call, any agent; plus Read/Grep for reviewer, security and investigator only
    loop.jsonl                    generated (orchestrator events + gate receipts); current state = last event
    verdicts/<round>-<agent>.json generated (SubagentStop hook): one validated verdict per reviewer/security run; <round> = A<n> or D<n>
    findings/A<n>.md              recorded (SubagentStop hook): the investigator's final message, verbatim, per FAIL (attempt n)
    findings/D<n>.md              recorded (SubagentStop hook): the same, per David steer n (Pause 1, Pause 2 or a PR comment; no attempt++)
    README.md                     generated node summary (the interviewer's entry point; source of the PR body)
    walkthrough.md                generated: every commit in order (pause 2 and the interview)
docs/qa/<plan>-<node>.md          QA script: tester writes it after PASS, steps tagged [RULE-ID]; David signs it
```
- `<plan>`/`<node>` are the stable ids (`build/F1`, `bug-001/N1`) parsed from the branch `feature/<plan>/<node>` or `bug/<plan>/<node>`.
- Hooks write under `git -C <cwd> rev-parse --show-toplevel`, so the trail lands in the worktree and never in the main checkout.
- No agent may Edit/Write `docs/trace/**` (guardrails). The investigator stays read-only: it returns findings as its final message, and the hook files them.
- `bugs/A<n>.md` from the earlier drafts is replaced by `findings/A<n>.md` (or `findings/D<n>.md` for a steer), which the tester and builder receive.
- **Rounds.** `A<n>` is attempt n: only reviewer, security or gate FAILs increment it. `D<n>` numbers David's steers on the node (1, 2… in order, never reset); a steer leaves `attempt` unchanged. Verdicts from the review that follows a steer are `verdicts/D<n>-<agent>.json`; the first review of a node is `A1`.

### Record schemas
Every record has `v` (schema version, `1`), `ts` (ISO-8601 UTC, from the hook/script clock, never from the agent), `plan`, `node`, `attempt`.

**Paths are repo-relative with forward slashes** (`src/Uinsure.Policies.Domain/Policy.cs`). A hook that cannot make a path relative to the repo root writes `"<outside-repo>"`. No absolute path, user name, transcript path or prompt text is ever stored. Agents are identified by `agent` and `agent_id` only.

```jsonc
// activity.jsonl (PostToolUse, matcher "Edit|Write|Bash|PowerShell|Read|Grep"; Read/Grep are recorded only when agent ∈ reviewer|security|investigator, else dropped)
{"v":1,"ts":"2026-10-18T09:12:03Z","plan":"build","node":"F1","attempt":1,
 "agent":"builder","agent_id":"a1b2c3","tool":"Edit","tool_use_id":"toolu_01…",
 "file":"src/Uinsure.Policies.Domain/Policy.cs","command":null}
// agent = hook agent_type when agent_id is present ("unknown" if agent_type is empty), or "main" when agent_id is absent (F0a spike rulings). attempt = the last loop.jsonl event's attempt.
// Bash/PowerShell: "file":null, "command":"<first 200 chars, repo-relative paths>".
// Read: "file" = the path read. Grep: "file" = the search path (or null), "command" = the pattern (first 200 chars).
// A successful `git commit` also records "commit":{"sha":"<40 hex>","subject":"<subject>","tdd":"scaffold|red|stub|green|refactor|null"}.

// loop.jsonl (written by the orchestrator via the render script's `event` subcommand, never by hand)
{"v":1,"ts":"…","plan":"build","node":"F1","attempt":1,"event":"gate.red",
 "cmd":"dotnet test --filter-trait \"Rule=SELL-2\"","exit":1,"expect":"fail","ok":true,"head":"<40 hex>","source":"orchestrator"}
// event ∈ setup · state(<STATE>) · gate.red · pause1 · gate.green · verdict(<agent>,<PASS|FAIL>,<file>) · findings(<file>) ·
//         attempt++ (FAILs only) · steer(D<n>,<pause1|pause2|pr-comment>) · investigation.opened(inv-NNN) · escalated ·
//         gate.integrate · trace.rendered(<ok|incomplete>) · pr(<number>) · pr.comment(<kind>,<comment id>) ·
//         gate.postman(<pass|fail|skipped>) · signoff(<sha>) · merged(<sha>) · archived
// gate events carry cmd/exit/expect/ok/head; others carry only the event fields they name.
// source = "orchestrator" | "reconciled" (backfilled from git on resume; orchestration's resume rule).

// verdicts/<A<n>|D<n>>-<agent>.json (SubagentStop; written only when the verdict passes the schema + semantic checks)
{"v":1,"ts":"…","plan":"build","node":"F1","attempt":1,"agent":"reviewer","agent_id":"…",
 "verdict":{ /* orchestration's verdict JSON schema v1, verbatim; findings[].file repo-relative */ }}

// findings/A<n>.md (FAIL) or findings/D<n>.md (David steer) (SubagentStop for agent_type investigator): frontmatter + the message body verbatim
// ---
// v: 1 · plan · node · attempt · round: A<n>|D<n> · agent: investigator · agent_id · source: reviewer|security|gate|david|pr-comment
// finding_ids: [R1, S2] · fingerprints: the investigator findings' `fingerprint` values, e.g. ["SELL-5|src/…/Policy.cs|<hash>"]
// ---
```

### Commit trailers
| Trailer | Values | Written by | Checked by |
|---|---|---|---|
| `Agent` | `tester`, `builder`, `git`, `main` | committing agent | **PreToolUse hook on `git commit`**: must equal `agent_type` (or `main`) |
| `Plan` | `build`, `feat-NNN`, `bug-NNN` | committing agent | **same hook**: must equal the branch's `<plan>` |
| `Node` | `F0a`…`F7`, `N1`… | committing agent | **same hook**: must equal the branch's `<node>` |
| `Rule` | a rule id; repeatable | committing agent | not by the hook; the TDD gates and the render script cross-check it against the rule markers in the diff |
| `TDD` | `scaffold`, `red`, `stub`, `green`, `refactor` | tester (`red`), builder (rest) | not by the hook; the gates check it (`red`: rule filter fails, diff is `tests/**` plus throwing `src/` signatures only; `green`: rule filter passes, diff is `src/**` only) |
| `Fixes-Finding` | `R1`, `S2`, `D1`…; repeatable | tester/builder | the render script: the id must exist in a verdict or findings file |

`plan(...)` and `chore(trace)` commits carry `Agent/Plan/Node` only. The hook denies a commit with a missing or mismatched `Agent/Plan/Node`, and gives the reason. It doesn't rewrite the command.

### Render script
- **`.claude/scripts/render-trace.mjs`**: Node (ESM, Node built-ins only, no npm dependencies), matching the hooks. Run by the orchestrator via Bash.
- **Subcommands:**
  - `event <json>` appends one validated line to `loop.jsonl`.
  - `render <plan> <node>` writes the node's `README.md` and `walkthrough.md` and regenerates `docs/trace/requirements-matrix.md`.
  - `pr-body <plan> <node>` prints the PR body to stdout.
  - `check <plan> <node>` runs only the completeness check.
- **Inputs:**
  - `activity.jsonl`, `loop.jsonl`, `verdicts/*.json` and `findings/*.md`
  - `git log origin/main..HEAD` with trailers, and `git show --name-only` per commit
  - rule markers in `tests/**` (`[Trait("Rule","ID")]`, grep), and the TRX from the `gate.integrate` full-suite run
  - the plan YAML (node rules and title), the rule ids in `docs/requirements.md`, and the QA script's `[RULE-ID]` tags and sign-off block
- **Outputs:** the generated files above, plus exit 0 = complete or exit 1 = `TRAIL INCOMPLETE`. A failed check renders anyway, with the reasons at the top of the README and the walkthrough.
- **When:**
  - After `gate.integrate`, the orchestrator renders. The git agent commits `docs/trace/<plan>/<node>/**` and `docs/trace/requirements-matrix.md` as `chore(trace): <node> trail`. That's the only commit allowed after the integrate receipt, because its diff is `docs/trace/**` only.
  - A later code commit (a fix from a PR comment, for example) re-runs INTEGRATE, then renders and commits again.
  - At ARCHIVE, a final render adds `signoff` and `merged`, on the git agent's `chore/archive-<id>` branch.
- SHAs are resolved at render time, after the final rebase. Activity records are matched to commits by `(agent, subject)`, not by their pre-rebase SHA.

### Completeness check (fails loudly)
`check` fails, and every rendered file says `TRAIL INCOMPLETE: <reasons>` and never shows a clean summary, when any of these hold:
- a commit on the branch has no activity `commit` record with the same `Agent` and subject
- a commit is missing `Agent/Plan/Node`, or has a `TDD` value outside the set
- `activity.jsonl` or `loop.jsonl` is empty while commits exist
- a REVIEW round has no reviewer + security verdict pair, or a PASS reviewer verdict's `rules_covered` misses one of the node's rules
- a FAIL verdict has no `findings/A<n>.md`, a `steer(D<n>)` event has no `findings/D<n>.md`, or a `Fixes-Finding` id resolves to nothing
- a node rule has no test marker, no `red` commit before its `green` commit, or no QA step
- a gate event is missing, or its `ok` is false
- an activity `file` is absolute (a privacy breach, not just a gap)
- at ARCHIVE only: there is no `signoff` whose sha is the PR head, or no `merged` event

**F0a exception:** F0a builds these hooks in-session, outside the loop, so it has no hook receipts. Its README states "hooks not yet live: witnessed by git and trailers only" and `check` reports `OK (git-only, F0a)`. F0b is the first fully witnessed node.

### Generated vs written
| Generated (hook or script; never a producer agent) | Recorded (hook stores an agent's output verbatim; it doesn't vouch for it) | Written (labelled) |
|---|---|---|
| `activity.jsonl`, `loop.jsonl` | `verdicts/*.json` (reviewer/security judgement) | `docs/trace/README.md` (David + main session, once in F0a) |
| node `README.md`, `walkthrough.md` | `findings/*.md` (investigator reasoning) | commit subjects (the walkthrough's one-liners) |
| `requirements-matrix.md` | | QA script (tester) and its ticks + sign-off (David) |
| PR body (all but one paragraph) | | the PR's "From David" paragraph, and `docs/lessons.md` entries |

Every generated file starts with: `Generated by .claude/scripts/render-trace.mjs from <inputs> at <ts>. Do not edit.`

### Node README template
```text
# <plan>/<node>: <node title> · status: <state> · PR #<n> · merge <sha|pending>
Generated by .claude/scripts/render-trace.mjs from loop/activity JSONL, verdicts, findings, git, test source and TRX at <ts>. Do not edit.
## Completeness       OK | TRAIL INCOMPLETE: <reasons>      ← always first, never omitted
## Rules              | rule | tests (pass/total) | red | green | refactor | QA step |
## Walkthrough        <n> commits → walkthrough.md
## Loop               attempt 1: FAIL (reviewer R1 SELL-5 boundary → findings/A1.md) · attempt 2: PASS/PASS
## Who touched what   | commit | Agent trailer | files | hook-log agent | match ✓/✗ |
## Gates              red ✓ · pause 1 ✓ · green ✓ · integrate ✓ (full suite 142/142 at <sha>) · sign-off David <date> <sha|pending>
## Re-verify          git log --format='%h %(trailers:key=Agent,valueonly,separator=%x2C) %(trailers:key=TDD,valueonly,separator=%x2C) %s' origin/main..<sha>
```

### walkthrough.md template
One row per commit on the branch, oldest first. **TDD** comes from the trailer (`plan`/`trace` for those commits). **Layer** is derived from the paths changed, never declared: `tests`, `domain`, `services`, `persistence` or `api` (by `src/Uinsure.Policies.<Layer>/`), `harness`, `docs`, or `mixed`. The one-liner is the commit subject's text after the `<type>(<rule>): <phase>:` prefix, so it is the agent's words and is labelled that way.
```text
# <plan>/<node> walkthrough: <n> commits
Generated by .claude/scripts/render-trace.mjs from git log at <ts>. Do not edit. The "what it did" column is the commit subject (agent-written).
| # | sha | TDD | layer | rule | agent | what it did |
|---|---|---|---|---|---|---|
| 1 | a1b2c3d | red | tests | SELL-2 | tester | rejects a start date more than 60 days ahead |
| 2 | … | stub | api | SELL-2 | builder | controller returns a hard-coded 201 |
| 3 | … | green | domain | SELL-2 | builder | Policy.Sell enforces the 60-day window |
```

### requirements-matrix.md
One row per rule id in `docs/requirements.md`, regenerated on every render, so it reaches `main` with each feature PR:
```text
| rule | plan/node | tests (pass/total) | red | green | refactor | QA step | sign-off | PR |
| SELL-2 | build/F1 | 6/6 | a1b2c3d | d4e5f6a | 0b1c2d3 | docs/qa/build-F1.md#3 | David 2026-10-18 | #3 |
| CANCEL-5 | build/F4 | not built yet | | | | | | |
```
A rule on a cut node shows `cut (<node>)`. A rule with no node fails the planning check rather than showing an empty row.

### PR body template (`pr-body`; matches orchestration's PR contract)
The title is `<node-id>: <node title>`. The PR is a draft. The body is generated from the node README:
```text
## <plan>/<node>: <node title>
Rules: SELL-1, SELL-2, … · Tests: <unit pass/total> unit, <integration pass/total> integration, <arch pass/total> architecture
Walkthrough: docs/trace/<plan>/<node>/walkthrough.md · Trail: docs/trace/<plan>/<node>/README.md
| rule | tests | red | green | refactor |
Attempts: <n> · <one line per attempt> · Verdicts and the rebase receipt are posted below as PR comments by the git agent.
How to run: dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080 · Postman env baseUrl=http://localhost:5080
QA: docs/qa/<plan>-<node>.md · sign-off: pending
Completeness: OK | TRAIL INCOMPLETE: <reasons>
## From David
<!-- one paragraph, written by David before `gh pr create`; the render leaves this marker and the PR isn't raised while it's empty -->
```
- The reviewer verdict, the security verdict (with OWASP ids) and the rebase-and-test receipt are **comments, not body**. The git agent posts each one and logs a `pr.comment` event. David's comments (only `dpmorgandev`) are steers: each becomes `findings/D<n>.md` with `source: pr-comment` and no attempt increment, and the git agent's reply names the fixing SHA.

## Open questions
1. ~~requirements.md §6.6~~ **Resolved:** hook input identifies the subagent, so the activity trail is mechanical (A3). The requirements update pass rewords §4 row 6b.
2. ~~Spike in F0a~~ **Resolved (F0a t3/t4):** `agent_type` arrives after `EnterWorktree` on Windows and equals the agent's name, and hook `cwd` is the worktree for subagent calls. `agent_type` is sometimes empty on subagent events, so the activity record keys a subagent on `agent_id` (schema comment above).
3. ~~F0's own trail~~ **Resolved:** F0a is git-only and labelled (see the F0a exception). F0b is the first fully witnessed node.
4. Resolved (David, 2026-10-17): yes. `activity.jsonl` logs `Read`/`Grep` for `reviewer`, `security` and `investigator` only (Layout, Record schemas).
5. **(open)** Is the `Rule` trait carried into the TRX under xUnit v3 + MTP (testing)? If not, rule → result joins on test names.
6. ~~Absolute paths~~ **Resolved:** repo-relative paths only, with no prompt text or transcript paths. An absolute path fails the check.
7. Resolved during reconciliation: the QA script path is `docs/qa/<plan-id>-<node-id>.md` in every contract (planning, testing, orchestration).
8. Resolved (David, 2026-10-17): no. Only reviewer, security or gate FAILs count as attempts. David's steers (Pause 1, Pause 2, PR comments) still go through investigate → red → fix, with ids `D1`, `D2`… and files `findings/D<n>.md` / `verdicts/D<n>-<agent>.json` (Layout).
