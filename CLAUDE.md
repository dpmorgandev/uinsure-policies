# Uinsure Policies API

A C# REST API for home insurance policies (sell, retrieve, cancel, renew, claims), built outside-in with TDD
as a take-home for Uinsure. Every rule, its ID and its agreed interpretation are in `docs/requirements.md`.
Read rule rows there by ID. Every line must be something David can explain and defend.

## Session ritual

- `/orient` before any work. It ends with `/load-context-chain`, which waits for David's confirmation.
- `/save-context` at node boundaries, at a hand-off to David, and when David asks.
- `/feature` runs from a session launched in the main checkout. The harness in force (settings, hooks,
  agents, this file) is the main checkout's. Settings and agents reload live from its working tree;
  this file is read at session start.

## Plan and loop skills

- `/plan-dag`: the build plan (`docs/plans/active/build.plan.md` + `build.dag.yaml`), run once.
- `/feature <plan-id> <node-id>`: the loop for one node. Main session only, dispatching the six agents
  (tester, builder, reviewer, security, investigator, git), with David's two pauses.
- `/plan-feature`, `/plan-bug`, `/plan-investigation`: built on first need.
- The `.dag.yaml` is the only plan state. Node and task status change only through commits.

## Hard rules

- **Commits:** use the configured git identity; never change `user.*` or `core.hooksPath`. Stage with
  explicit `git add <paths>`, never `-A` or `.`. Loop commits carry the trailers `Agent`, `Plan`, `Node`,
  `Rule` and `TDD: scaffold|red|stub|green|refactor`, plus `Fixes-Finding` when fixing one.
- **No push to `main`.** Every push, PR and PR comment is approved by David. **David merges**, in the
  GitHub UI. Nobody merges by CLI, force-pushes, uses `--no-verify` or changes repo settings.
- **`main`:** direct commits are allowed only until F0b starts. After that, every change goes through a
  branch and a PR.
- **Nothing private in this repo.** It goes public: no local absolute paths, personal emails, keys or
  private names in any file, snapshot or commit. The pre-commit privacy scan enforces this.
- **Subagents never read `.claude/specs/`.** They act on their preloaded skills and on `docs/requirements.md`.
- **Harness lock:** no subagent writes `.claude/{agents,skills,hooks,specs,workflows}/`,
  `.claude/settings*.json`, `.githooks/`, `.git/` or this file, including the copies in worktrees.
- **Time and money:** never use `DateTime.Now`, `UtcNow` or `Today`. Use `TimeProvider`, and pass
  `DateOnly today` into the domain. Round money only through `Money.Round`.
- Agents' final message is one JSON object. Gates are run by the orchestrator, never taken from an agent.

## Repository layout

```text
CLAUDE.md        this file
.claude/         harness: agents/, skills/, hooks/, specs/, settings.json; worktrees/<plan>-<node>/
.githooks/       pre-commit, commit-msg, pre-push
docs/            requirements.md, lessons.md, plans/{active,archive}/, context/, trace/<plan>/<node>/, qa/
src/             Uinsure.Policies.Api, .Services, .Persistence, .Domain   (src/web/ reserved, not built)
tests/           Uinsure.Policies.Domain.Tests, .Services.Tests, .Api.Tests, .Architecture.Tests, postman/
infrastructure/  Bicep + Azure DevOps pipeline (evidence only, never deployed; `az` is never run)
```

Nothing in `src/` or `tests/` references `.claude/`.

## Build and test

```sh
dotnet build Uinsure.Policies.slnx -warnaserror                       # pre-commit runs this
dotnet test --solution Uinsure.Policies.slnx                          # unit, integration, architecture
dotnet run --project src/Uinsure.Policies.Api --urls http://localhost:5080    # HITL, from the worktree
postman collection run tests/postman/Uinsure.Policies.postman_collection.json \
  -e tests/postman/local.postman_environment.json                     # Postman CLI, no login needed
node --test ".claude/hooks/__tests__/*.test.mjs"                    # harness self-test
```

pre-push runs a Release build, the full test suite, `dotnet ef migrations script --idempotent` and the
self-test. All must pass.
