# The AI build harness

This repo was built by AI agents in [Claude Code](https://claude.com/claude-code), inside a harness that sets
the order of work, checks every step it can mechanically, and keeps an audit trail. David Morgan planned it,
steered it at two pauses per feature, signed off each QA script and merged every PR himself. Nothing in `src/`
or `tests/` depends on the harness: you don't need it to build, run or test the API.

The harness lives in `.claude/` (agents, skills, hooks, specs) and `.githooks/`. Its rules for agents are in
[CLAUDE.md](../CLAUDE.md).

## How a feature moves through it

The build plan is a DAG, [docs/plans/active/build.dag.yaml](plans/active/build.dag.yaml), with the reasoning in
[build.plan.md](plans/active/build.plan.md). Each node is one feature with an explicit task list, and node and
task status change only through commits. `/feature build <node>` runs one node:

1. **Set-up.** A branch `feature/build/<node>` in its own git worktree, so `main` is never touched.
2. **Red.** The `tester` agent writes the acceptance tests for the node's rules first (outside-in), plus throwing
   stubs, then the Postman requests. A gate checks that the build passes and every rule's tests fail.
3. **Pause 1.** David runs the stubbed API and the red suite, then replies `go` or asks for a change.
4. **Green, per layer.** The `tester` writes unit tests and the `builder` makes them pass: domain, then
   services, then persistence, then the API. Then a refactor step. Every step has a gate (build, test filters per
   rule, the diff stays inside the allowed paths).
5. **Review.** The `reviewer` (rules, TDD order, test strength, code quality) and `security` (OWASP API Top 10
   2023 plus .NET checks) agents judge the diff in parallel, from a fixed review packet, without seeing each
   other's verdict. Both must PASS on the current commit.
6. **QA script.** The `tester` writes a step-by-step manual script, `docs/qa/build-<node>.md`, with one row per
   rule.
7. **Integrate and trail.** The `git` agent rebases onto `main`; the full suite is re-run on the result. The
   trail is rendered and checked for completeness.
8. **PR and Pause 2.** A draft PR opens with David's own paragraph, and the verdicts and the rebase result are
   posted as comments. The node's Postman folder runs as a gate. David works through the QA script, signs it off,
   and merges in the GitHub UI.

**When a review or gate fails,** the `investigator` agent diagnoses the root cause, the `tester` writes a failing
test for each finding, and the `builder` fixes it. Then the gates and the review run again. On the third failure
the node stops for a written investigation (`docs/plans/active/inv-NNN.plan.md`), whose recommendations David
accepts or rejects before the node continues. On the fifth it stops for David to decide. David's own change
requests take the same route but don't count as failures.

## The agents

| Agent | Model | Can write | Job |
|---|---|---|---|
| `tester` | Opus | tests, throwing stubs, Postman, QA scripts | the red step, a red test per finding, the QA script |
| `builder` | Sonnet | `src/`, the build files, docs for docs nodes | stubs, green per layer, refactor |
| `reviewer` | Opus | nothing (read-only) | verdict on rules, TDD order, test strength, quality |
| `security` | Sonnet | nothing (read-only) | OWASP API Security Top 10 2023 and .NET checks |
| `investigator` | Opus | nothing (read-only) | root cause for each failure or change request |
| `git` | Haiku | git only | branch, rebase, push, PR, comments, clean-up |

The orchestrator is the main Claude Code session. It dispatches one agent at a time (reviewer and security are
the one parallel pair), runs every gate itself and never takes an agent's word for a result. A gate without a
recorded receipt counts as blocked. Three more agents (`ui-designer`, `creative-director`, `kit-builder`) belong
to a design stage that this API didn't use.

## Guardrails

Hooks enforce the rules mechanically, rather than trusting the agents to follow them:

- **`guard-agent`**: each agent writes only its own paths, and no agent reads `.claude/specs/` (agents act on
  their skills and on `docs/requirements.md`, not on the harness's design notes).
- **`guard-bash`**: blocks force-pushes, `--no-verify`, merging by CLI, changing the hooks path (and, for an
  agent, any git config), and opening a PR from a branch that has no review verdicts.
- **`gate-subagent`**: when an agent stops, checks what git says it changed against its role, and validates its
  verdict or findings JSON.
- **`trace-activity`**: logs every tool call to the node's trail.
- **Git hooks**, active once `git config core.hooksPath .githooks` is set: `pre-commit` (no commits on `main`,
  a privacy scan of staged content, a warnings-as-errors build), `commit-msg` (trailers naming the agent, plan,
  node, rule and TDD step), `pre-push` (no pushes to `main`, then a Release build, the full suite, the idempotent
  migration script and the harness self-test, 117 tests).

## The audit trail

Everything a node did is in `docs/trace/build/<node>/`:

- `walkthrough.md`: the readable story of the node, rendered from the files below.
- `loop.jsonl`: every state change, gate result (command, exit code, commit), verdict, steer, PR and sign-off.
- `verdicts/` and `findings/`: each review round's verdicts and each failure's diagnosis.
- `packet/`: exactly what the reviewers saw (diff, commit trailers, gate results).
- `activity.jsonl`: every tool call by every agent.

[docs/trace/requirements-matrix.md](trace/requirements-matrix.md) maps each rule to its tests, its red, green
and refactor commits, its QA step, its sign-off and its PR. The QA scripts with David's sign-offs are in
[docs/qa/](qa/). Each commit's trailers (`git log --format='%h %s%n%(trailers)'`) show which agent made it and at
which TDD step.

## How it went

| Node | Feature | Attempts | PR |
|---|---|---|---|
| F0a | Harness | — | (on `main`, before the lock) |
| F0b | Walking skeleton | 2 | #6 |
| F1 | Sell (+ bare GET) | 2 | #10 |
| F1b | Layered models and interfaces | 2 | #15 |
| F2 | Retrieve | 3 | #17 |
| F3 | Cancel (+ quote, + claims) | 2 | #20 |
| F4 | Renew | 1 | #28 |
| F5 | Infrastructure and pipeline (never deployed) | 3 | #34 |
| F6 | README | 4 | #38 |
| F7 | Pitch deck | not started | — |

"Attempts" counts review or gate failures plus one, so 1 means it passed first time. F2, F5 and F6 each
reached the investigation stop: [inv-001](plans/active/inv-001.plan.md),
[inv-002](plans/active/inv-002.plan.md) and [inv-003](plans/active/inv-003.plan.md) say why and what changed.
In all, 39 PRs were merged: 25 of them changed the harness itself, mostly fixes found while running it.

**What the harness did not catch,** found later:

- The activity hook logged an agent's search pattern verbatim, and that pattern contained a private term. The
  privacy scan missed it at the time. The current files are redacted, and the gap is a known harness fix.
- An API process started by one step was left running, and it locked the build for later steps.
- The read-only guard also blocks read-only shell commands for general-purpose agents, so an ad-hoc security
  audit had to run its git-history scan from the main session.

## Running it yourself

You need Claude Code and this repo with `git config core.hooksPath .githooks` set. Start a session in the repo
root and run `/orient`, then `/feature build <node>` for the next pending node. The design notes behind each rule
of the harness are in `.claude/specs/`. All of them are decided except `efficiency.spec.md`, which is still a
draft.
