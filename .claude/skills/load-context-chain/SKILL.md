---
name: load-context-chain
description: Read the docs/context/ snapshot chain on the current branch and synthesise where the work stands, then stop and ask David to confirm before resuming. Read-only. Use at session start (via /orient) and after a compact reload.
argument-hint: "[--latest-only] [--max-depth N]"
---

# /load-context-chain

Contract: context.spec C4. This skill is read-only: it writes nothing, dispatches no agents and makes no commits.

## 1. List
`ls docs/context/*.md 2>/dev/null | sort`. If there are none, say "no prior context on this branch" and go to step 6.

## 2. Walk
Start at the newest file. Read its frontmatter and follow `previous-context` back. Stop at the first of:
- a file with `checkpoint: true` (inclusive)
- depth N (default 5, or `--max-depth N`)
- `previous-context: null`
- a broken link (the named file doesn't exist). Report it by name, for example "chain forks: <file> not on this branch". It is not fatal.

`--latest-only` reads one file.

## 3. Staleness
If the newest `timestamp` is more than 24h before `date -u`, flag it as `STALE (<n>h)`. If the session input supplied `seconds_since_last_response`, show it as idle time. That's information only.

## 4. Drift
Compare the newest snapshot's `branch` and `head` with `git rev-parse --abbrev-ref HEAD` and `git rev-parse --short HEAD`. Flag any mismatch, for example "head moved <old> → <new>: N commits since the snapshot" (use `git log --oneline <old>..HEAD | wc -l`).

## 5. Synthesise
Write ≤ 50 lines, oldest → newest:
- where the work is (plan, node, phase, step, attempt, branch)
- what's been done (with SHAs)
- decisions made (as links, never restated)
- open decisions
- flags from steps 2–4
- the next action

Never paste snapshot text: summarise it in your own words.

## 6. Confirm gate
End with exactly:
`Resume with: <next-action>?`
(or `No prior context — what should we start with?` when there are no snapshots)

Then STOP. No writes and no dispatches until David confirms or redirects.
