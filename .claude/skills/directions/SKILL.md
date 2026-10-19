---
name: directions
description: >-
  Design stage 2 for the ui plan: dispatch the creative-director once with a script-rendered prompt to
  write three cited, divergent territories to docs/ui/directions.json, validate them in the parent, and
  open the PR into ui. No gate: David judges the territories as mockups at G2.
ported-from: agentflow_phase3 .claude/skills/creative-director/SKILL.md (2026-10-19); the procedure, without the factory machinery
---
# /directions: design stage 2

## Reads / Writes

- **Reads:** `docs/ui-requirements.md`, `docs/ui/research/`, `docs/ui/flows.md`, `docs/ui/screens.json`,
  `docs/ui/components.md`, through `.claude/scripts/design/render-prompt.mjs` and the agent.
- **Writes:** `docs/ui/directions.json` (by creative-director); `docs/trace/ui/DC/**` (events by
  `render-trace.mjs`, feedback files by you); `.claude/scripts/design/.out/**` (rendered prompts,
  gitignored).

You are the main session. You dispatch once, check the result yourself, and open the PR. Rules in force:
`efficiency.spec.md`, guardrails G15 and G16, and `ui-research.spec.md`'s stage 2 contract.

- **You never write or edit the creative-director's prompt.** You render it and pass the file's content
  unchanged; G15 re-renders it and denies any difference.
- **You don't write, edit or rank territories.** The agent writes them; the script checks them; David
  chooses between them as mockups.
- **Don't edit files in this worktree while the agent runs.** The stop check attributes uncommitted
  changes to the agent.

## 0. Setup (once)

1. From the `ui` worktree: `git fetch origin`, then
   `git worktree add ../ui-DC -b feature/ui/DC origin/ui` (a sibling under `.claude/worktrees/`), then
   `EnterWorktree` into it.
2. Check the input renders: `node .claude/scripts/design/render-prompt.mjs directions all all > /dev/null`.
   An error stops the stage: fix the input (a missing file or brief heading), not the template.
3. Check coverage still passes: `node .claude/scripts/design/coverage.mjs`. Directions built on a catalog
   that misses a requirement inherit the gap.
4. Event: `node .claude/scripts/render-trace.mjs event '{"event":"setup","detail":"feature/ui/DC from origin/ui <sha>"}'`.

## 1. Dispatch (attempt `n`, starting at 1)

1. Render: `node .claude/scripts/design/render-prompt.mjs directions all all --attempt <n> --out .claude/scripts/design/.out/directions-<n>.md`
2. Read that file and dispatch `creative-director` with its **entire content as the prompt, unchanged**.
3. The stop hooks check the agent: G3 (it wrote only `docs/ui/directions.json`) and G16 (its message is
   the receipt and the file parses). A blocked stop makes the agent restate; you do nothing.
4. Read the receipt. `status: blocked` → record it, don't retry; tell David.

## 2. Validate (in the parent, never in the agent)

1. `node .claude/scripts/design/validate-directions.mjs`
2. Record a gate event with the real exit code:
   `node .claude/scripts/render-trace.mjs event '{"event":"gate.directions","cmd":"validate-directions.mjs","exit":<code>,"expect":"0","ok":<true|false>,"head":"<HEAD sha>","detail":"attempt <n>: <problems, or clean>"}'`
3. **Fails, attempt < 3:** write the validator's output, verbatim, to
   `docs/trace/ui/DC/feedback/all-all-<n+1>.txt`, then go back to step 1 with attempt `n+1`. At most 2
   regenerations (EFF-4).
4. **Fails at attempt 3:** stop and tell David, with the validator's output.

Fix nothing yourself: not the file, not a citation, not a contrast value. If the validator is wrong,
that's a harness bug: stop and tell David.

## 3. Commit and hand over

1. Commit `docs/ui/directions.json`, the feedback files and the trail:
   `git add docs/ui/directions.json docs/trace/ui/DC`. Trailers `Agent: main`, `Plan: ui`, `Node: DC`, in
   one final paragraph with `Co-Authored-By` (G9 reads only the last paragraph).
2. Tell David: the three territory names and governing ideas, quoted from the file, the attempts used and
   the validator result. Nothing about which is better: you haven't judged them and won't.
3. David reads `docs/ui/directions.json`. If he wants a territory reconsidered, that's a steer:
   `steer(D<n>,pause2)` with his words in `detail`, written to the next feedback file, back through
   steps 1–2. When he's content: event `signoff(<HEAD sha>)`, commit the trail, then push
   `feature/ui/DC` and open a PR into `ui` (each needs his approval; G14's design route checks the
   sign-off). He merges in the GitHub UI.

The mockups stage then renders each territory straight from `docs/ui/directions.json`.

## Never

- Write, edit, shorten or "clean up" the creative-director's prompt, or dispatch it without one.
- Write, edit or rank a territory.
- Exceed 3 attempts.
- Dispatch any other agent from this skill.
