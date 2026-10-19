# The AI build harness

This repo was built by AI agents in [Claude Code](https://claude.com/claude-code), inside a harness that David
Morgan designed for it. The harness sets the order of work, checks every step it can mechanically, and keeps an
audit trail of who did what and why. David decided the design, steered each feature at two pauses, signed off
every QA script and merged every PR himself, in the GitHub UI.

Nothing in `src/` or `tests/` depends on the harness. You don't need it to build, run or test the API.

**Why build a harness at all?** An agent left to itself will write code and tests together, mark its own work as
passing, and drift from the brief. The harness is there to prevent each of those, mechanically where it can:

- the tests come first, and a gate proves they fail before any code exists;
- a separate agent judges the work, and never the one that wrote it;
- every result is a recorded command and exit code, never an agent's claim;
- every rule in the brief traces to its tests, its commits and a human sign-off.

## What was built, at a glance

| Piece | Where | What it does |
|---|---|---|
| Design decisions | `.claude/specs/*.spec.md` | 12 specs, each with research, options, David's decision and a binding contract |
| Requirements | [requirements.md](requirements.md) | every rule from the brief with an ID and the interpretation taken |
| Plans | [plans/active/](plans/active/) | the build DAG, its reasoning, and the investigations |
| Slash commands | `.claude/skills/` | the session ritual, planning and the feature loop |
| Agents | `.claude/agents/` | six roles with fixed tools, models and write scopes |
| Guardrails | `.claude/hooks/`, `.githooks/` | hooks that block what the rules forbid |
| Context chain | [context/](context/) | session snapshots, so work resumes across sessions and days |
| Audit trail | [trace/](trace/), [qa/](qa/) | per-feature logs, verdicts, walkthroughs and signed QA scripts |

The rules every agent works under are in [CLAUDE.md](../CLAUDE.md).

## The specs: deciding before building

Before any code, each part of the system got a spec in `.claude/specs/`, covering requirements, API contract,
architecture, testing, orchestration, agents and skills, planning, guardrails, traceability, context, deployment
and infrastructure. Each spec researched the options, recommended one, and recorded David's decision (all dated
2026-10-17) and a binding **Contract** section. Eleven are decided. `efficiency.spec.md`, on token and time cost,
is still a draft.

**Why:** the decisions are made once, by David, with the reasoning written down. Agents never read the specs
(a hook enforces it). They act on the contracts distilled into their skills and on `docs/requirements.md`, so
they never see the rejected options and can't re-argue them.

## Plans: a DAG with explicit tasks

The build plan is two files:

- [build.dag.yaml](plans/active/build.dag.yaml) holds the only state: nodes (features), their rules,
  dependencies, status and attempt count, and an explicit task list per node.
- [build.plan.md](plans/active/build.plan.md) holds the reasoning: why this order, the rejected alternatives,
  the prior work consulted.

A validator checks the DAG on every run. Every rule must be in requirements.md and in exactly one node, and every
node's tasks must follow its sequence in order. Node and task status change only through commits on the node's
branch (`plan(build/F3): t5 pending → done`), so `git log` is the plan's history.

| Plan kind | Made by | For |
|---|---|---|
| `build` | `/plan-dag`, once | the whole build: F0a harness, F0b walking skeleton, F1–F4 features, F5 infrastructure, F6 README, F7 pitch deck |
| `inv-NNN` | `/plan-investigation` | a node that failed three times: one falsifiable question, a hypothesis, evidence and recommendations David accepts or rejects |
| `bug-NNN` | `/plan-bug` | a defect found after merge: N1's first task is the failing reproduction test |
| `feat-NNN` | `/plan-feature` | a change request (specified, not built yet) |

**Why a DAG with explicit tasks:** the orchestrator never improvises the next step. It reads the next task that
isn't done, and the plan file shows exactly what is left.

## Slash commands

| Command | What it does | Why |
|---|---|---|
| `/orient` | Reads the repo (requirements, spec status, plans, git, PRs, the trail in flight) and runs the harness self-test, then `/load-context-chain` | every session starts from the repo's real state, not from memory |
| `/load-context-chain` | Walks the snapshot chain back to the last checkpoint, flags staleness and drift, then stops for David to confirm | resuming is a decision David makes |
| `/save-context` | Writes a snapshot of where the work stands to `docs/context/` | the next session, or the next day, picks up exactly here |
| `/plan-dag` | Drafts the build plan after asking David once | one considered plan instead of a running to-do list |
| `/feature <plan> <node>` | Runs one node through the loop below | the whole method in one place, run the same way every time |
| `/plan-investigation` | Opens an investigation when a node keeps failing | the third failure stops for a diagnosis, not a fourth guess |
| `/plan-bug` | Drafts a bug plan for a defect on `main` | post-merge defects go through the same test-first loop |

The agents also preload skills that aren't commands: `tdd-outside-in`, `architecture`, `code-quality`,
`owasp-api-security`, `requirements-trace`, `root-cause`, `verdict-format`, `git-workflow` and `dotnet10-webapi`.
Each is a role's working method, distilled from the specs.

## How a feature is built (`/feature`)

1. **Set-up.** A branch `feature/build/<node>` in its own git worktree, so `main` is never touched.
2. **Red.** The `tester` writes the acceptance tests for the node's rules first (outside-in), with throwing stubs
   so they compile, and the Postman requests. A gate proves the build passes and every rule's tests fail.
3. **Pause 1.** David runs the stubbed API and reads the red suite, then replies `go` or asks for a change.
4. **Green, per layer.** The `tester` writes unit tests and the `builder` makes them pass: domain, then services,
   then persistence, then the API, then a refactor step. Each step has a gate: the build, the rule's tests and
   the diff staying inside the allowed paths.
5. **Review.** The `reviewer` (rules, TDD order, test strength, code quality) and `security` (OWASP API Top 10
   2023 plus .NET checks) judge the diff in parallel, from a fixed review packet, without seeing each other's
   verdict. Both must PASS on the current commit.
6. **QA script.** The `tester` writes a step-by-step manual script, `docs/qa/build-<node>.md`, one row per rule.
7. **Integrate and trail.** The `git` agent rebases onto `main`, the full suite re-runs on the result, and the
   trail is rendered and checked for completeness.
8. **PR and Pause 2.** A draft PR opens with David's own paragraph, and the verdicts and the rebase result are
   posted as comments. The node's Postman folder runs as a gate. David works through the QA script, signs it
   off, and merges.

**When a review or gate fails:** the `investigator` diagnoses the root cause, the `tester` writes a failing test
for each finding, the `builder` fixes it, and the gates and review run again. On the third failure the node stops
for `/plan-investigation`. On the fifth it stops for David. David's own change requests take the same route but
don't count as failures.

**Why outside-in with two pauses:** the acceptance tests pin the API's behaviour before any design exists, and
David sees the shape of each feature (Pause 1) before the work behind it, and the finished result (Pause 2)
before it reaches `main`.

## The agents

| Agent | Model | Can write | Job |
|---|---|---|---|
| `tester` | Opus | tests, throwing stubs, Postman, QA scripts | the red step, a red test per finding, the QA script |
| `builder` | Sonnet | `src/`, the build files, docs for docs nodes | stubs, green per layer, refactor |
| `reviewer` | Opus | nothing (read-only) | verdict on rules, TDD order, test strength, quality |
| `security` | Sonnet | nothing (read-only) | OWASP API Security Top 10 2023 and .NET checks |
| `investigator` | Opus | nothing (read-only) | root cause for each failure or change request |
| `git` | Haiku | git only | branch, rebase, push, PR comments, clean-up |

The orchestrator is the main Claude Code session. It dispatches one agent at a time (reviewer and security are the
one parallel pair), runs every gate itself and never takes an agent's word for a result. A gate without a recorded
receipt counts as blocked. Three more agents (`ui-designer`, `creative-director`, `kit-builder`) belong to a design
stage for a future web front end, which this API didn't use.

**The model split:** Opus writes the tests, reviews and diagnoses. Sonnet writes code against tests that already
exist and runs the security checklist. Haiku runs git commands. A test that can fail and a review that can say no
are where the loop's quality is decided.

## Guardrails

Hooks enforce the rules, rather than trusting agents to follow them:

- **`guard-agent`**: each agent writes only its own paths, no agent writes the harness itself, and no agent reads
  the specs.
- **`guard-bash`**: blocks force-pushes, `--no-verify`, merging by CLI, changing the hooks path (and, for an
  agent, any git config), and opening a PR from a branch that has no review verdicts.
- **`gate-subagent`**: when an agent stops, checks what git says it changed against its role, and validates its
  verdict or findings JSON.
- **`trace-activity`**: logs every tool call to the node's trail.
- **`session-start`**: reports where the work stands and tells the session to orient first.
- **Permissions:** every push, PR and PR comment prompts David. Pushes to `main` and repo-admin commands are
  denied outright.
- **Git hooks**, active once `git config core.hooksPath .githooks` is set:
  - `pre-commit`: no commits on `main`, a privacy scan of staged content, a warnings-as-errors build;
  - `commit-msg`: trailers naming the agent, plan, node, rule and TDD step;
  - `pre-push`: no pushes to `main`, then a Release build, the full suite, the idempotent migration script and
    the harness self-test (117 tests).
- **Prompt injection:** once the repo is public, only PR comments by the owner's account are acted on.

**Why hooks:** a rule an agent is asked to follow can be forgotten. A hook that denies the call can't be.

## The context chain

Each session writes snapshots to [docs/context/](context/) with `/save-context`. A snapshot has fixed frontmatter
(plan, node, step, attempt, branch, head, tests, next action, open decisions) and fixed sections (completed, current
state, carry-overs, open decisions, pointers). It is capped at 200 lines and privacy-scanned before it is written.
Each snapshot links to the one before it (`previous-context`). A snapshot is a checkpoint, where the walk back
stops, when a node finishes or is signed off, and otherwise every fifth snapshot.

`/load-context-chain` reads that chain, flags a stale snapshot or a branch that moved, and asks David to confirm
before any work resumes. The `## Carry-overs` section is binding: at the next node's set-up, each item becomes a
task or David cuts it, so a deferred finding can't be forgotten. There are 18 snapshots, 10 of them checkpoints.

**Why:** sessions end, and context windows compact. The chain is how the next session knows exactly where the work
stands without trusting a summary from memory.

## The audit trail

Everything a node did is in `docs/trace/build/<node>/`:

- `walkthrough.md`: the readable story of the node, rendered from the files below.
- `loop.jsonl`: every state change, gate result (command, exit code, commit), verdict, steer, PR and sign-off.
- `verdicts/` and `findings/`: each review round's verdicts and each failure's diagnosis.
- `packet/`: exactly what the reviewers saw (diff, commit trailers, gate results).
- `activity.jsonl`: every tool call by every agent.

[trace/requirements-matrix.md](trace/requirements-matrix.md) maps each rule to its tests, its red, green and
refactor commits, its QA step, its sign-off and its PR. The QA scripts with David's sign-offs are in [qa/](qa/).
Each commit's trailers (`git log --format='%h %s%n%(trailers)'`) show which agent made it and at which TDD step.
[trace/build/F0a/](trace/build/F0a/) records the spikes that tested the harness's assumptions about Claude Code
(worktrees, hooks, settings, permissions) before anything relied on them.

**Why:** "the tests pass" is a claim. A trail of commands, exit codes and commits is evidence anyone can check.

## How it went

| Node | Feature | Attempts | PR |
|---|---|---|---|
| F0a | Harness | — | (built on `main`, before the lock) |
| F0b | Walking skeleton | 2 | #6 |
| F1 | Sell (+ bare GET) | 2 | #10 |
| F1b | Layered models and interfaces | 2 | #15 |
| F2 | Retrieve | 3 | #17 |
| F3 | Cancel (+ quote, + claims) | 2 | #20 |
| F4 | Renew | 1 | #28 |
| F5 | Infrastructure and pipeline (never deployed) | 3 | #34 |
| F6 | README | 4 | #38 |
| F7 | Pitch deck | not started | — |

"Attempts" counts review or gate failures plus one, so 1 means it passed first time. F2, F5 and F6 each reached the
investigation stop: [inv-001](plans/active/inv-001.plan.md), [inv-002](plans/active/inv-002.plan.md) and
[inv-003](plans/active/inv-003.plan.md) say why, and what changed. In all, 39 PRs were merged: 25 of them changed
the harness itself, mostly fixes found while running it.

**What the harness did not catch,** found later:

- The activity hook logged an agent's search pattern verbatim, and that pattern contained a private term. The
  privacy scan missed it at the time. The current files are redacted, and the gap is a known harness fix.
- An API process started by one step was left running, and it locked the build for later steps.
- `"policyholders":[null]` on a sale returns a `500`, not a `400`. No test sent a null element, and no review caught
  it. It was found in a security review before publishing, and is the first `/plan-bug`.
- The read-only guard also blocks read-only shell commands for general-purpose agents, so that review had to run
  its git-history scan from the main session.

## Running it yourself

You need Claude Code, and this repo with `git config core.hooksPath .githooks` set. Start a session in the repo
root and run `/orient`, then `/feature build <node>` for the next pending node.
