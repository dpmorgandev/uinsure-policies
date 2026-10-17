---
name: orient
description: Gather as much project context as possible before work begins, covering requirements, spec status, plans, git, PRs and the trail in flight, then run /load-context-chain and wait for David. Project-only and read-only. Use at the start of every session.
argument-hint: ""
---

# /orient

Contract: context.spec C5. This skill is read-only. It reads **only inside this repository**.

## 0. Root guard
`git rev-parse --show-toplevel` must succeed, and the cwd must sit inside that root. Otherwise stop with "orient: not inside the repository".
Refuse any path argument, or any path you were about to read, that resolves outside the root. Never read user-level, home-directory or other-workspace files. This skill reads whichever checkout the session is in (main or a feature worktree).

## 1. Entry point
Read `CLAUDE.md` at the root. If it's missing, note "CLAUDE.md: missing" and carry on.

## 2. Requirements
`docs/requirements.md`: its headings (`grep -n '^#' docs/requirements.md`) and the §6 open items, in full.

## 3. Spec status
The `Status:` line of each `.claude/specs/*.spec.md`, and only that line:
`grep -m1 -H '^Status:' .claude/specs/*.spec.md`

## 4. Plans
`docs/plans/active/*.plan.md`: for each node, its id, title, status and attempt (from the `yaml dag` block). If there are none, say "no active plan".

## 5. Git
- `git status -s`
- `git rev-parse --abbrev-ref HEAD`
- `git log --oneline -5`
- `git worktree list`. Print every path relative to the root; never print an absolute path.

## 6. Pull requests
`gh pr list`. If there's no remote or `gh` fails, print "no remote" rather than an error.

## 7. Trail in flight
If a plan node is in flight, list `docs/trace/<plan>/<node>/` and read its `README.md` if present.

## 8. Summary
Print an orientation summary of ≤ 60 lines:
- where the project is (sequence step, plan, node)
- spec status
- git and PR state
- open items
- anything inconsistent, such as a dirty tree, a stale worktree, or a branch that doesn't match the plan

## 9. Context chain
Run the real `/load-context-chain` skill **verbatim**: invoke it, don't summarise or imitate it. Its confirm gate is the last thing that happens. Hold for David.
