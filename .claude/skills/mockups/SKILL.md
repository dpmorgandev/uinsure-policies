---
name: mockups
description: >-
  Design stage G2 for the ui plan: dispatch ui-designer once per direction × screen with a script-rendered
  prompt, lint each artifact, render the screenshot review, and stop for David to pick a direction.
argument-hint: "[direction ...]"
ported-from: agentflow_phase3 .claude/skills/mockups/SKILL.md (2026-10-19); the procedure, without the factory machinery
---
# /mockups: design stage G2

## Reads / Writes

- **Reads:** `docs/ui/styles.md`, `docs/ui/screens.json`, `docs/ui/components.md`, and the trail files
  listed in `.claude/skills/mockups/inputs.json`, all through `.claude/scripts/design/render-prompt.mjs`.
- **Writes:** `docs/ui/mockups/**` (the artifacts, by ui-designer; `_shots/` and `review.html`, by the
  screenshot script); `docs/trace/ui/DM/**` (events by `render-trace.mjs`, feedback files by you);
  `.claude/scripts/design/.out/**` (rendered prompts, gitignored).

You are the main session. You dispatch, run every check yourself, and stop for David. Rules in force:
`efficiency.spec.md` (EFF-1 to EFF-13) and guardrails G15 and G16. The two that shape every step:

- **You never write or edit a ui-designer prompt.** You render it with the script and pass the file's
  content unchanged. The dispatch guard (G15) re-renders it and denies any difference, a missing header,
  or a summary. A denied dispatch is a defect in your step, not something to work around.
- **No agent's report about pixels counts.** David judges the screenshots; nobody else does.

## 0. Setup (once)

1. From the `ui` worktree: `git fetch origin`, then
   `git worktree add .claude/worktrees/ui-DM -b feature/ui/DM origin/ui`, then `EnterWorktree` into it.
2. `npm --prefix .claude/scripts/design ci` (Playwright, pinned; Chromium from the local cache).
3. Check the inputs render: for every direction and screen in `inputs.json`,
   `node .claude/scripts/design/render-prompt.mjs mockups <direction> <screen> > /dev/null`. Any error
   stops the stage: fix the input (a missing heading, screen or trail file), not the template.
4. Event: `node .claude/scripts/render-trace.mjs event '{"event":"setup","detail":"feature/ui/DM from origin/ui <sha>"}'`.

The directions are the keys of `inputs.json` `directions`, or the ones David named as arguments. The
screens are the keys of `screens` (overview, rule): 2–3 directions × 2 screens, the dispatch budget in
`efficiency.spec.md`.

## 1. Dispatch (per direction × screen, up to 3 at once)

For each pair, attempt `n` (starting at 1):

1. Render: `node .claude/scripts/design/render-prompt.mjs mockups <direction> <screen> --attempt <n> --out .claude/scripts/design/.out/<direction>-<screen>-<n>.md`
2. Read that file and dispatch `ui-designer` with its **entire content as the prompt, unchanged**.
3. The stop hooks check the agent: G3 (it wrote only under `docs/ui/mockups/**`) and G16 (its message is
   the receipt and each listed file is clean). A blocked stop makes the agent restate; you do nothing.
4. Read the receipt. `status: blocked` → record it, don't retry; tell David at the stop (step 4).

Independent pairs run in parallel (one message, several dispatches), at most 3 at a time.

## 2. Check each artifact (in the parent, never in the agent)

For each artifact the receipt lists:

1. Lint: `node .claude/scripts/design/lint.mjs docs/ui/mockups/<direction>/<screen>.html <screen>`
2. Record it as a gate event with the real exit code:
   `node .claude/scripts/render-trace.mjs event '{"event":"gate.design-lint","cmd":"lint.mjs <direction>/<screen>","exit":<code>,"expect":"0","ok":<true|false>,"head":"<HEAD sha>","detail":"attempt <n>: <problems, or clean>"}'`
3. **Lint fails, attempt < 3:** write the lint output, verbatim, to
   `docs/trace/ui/DM/feedback/<direction>-<screen>-<n+1>.txt`, then go back to step 1 with attempt `n+1`
   (the renderer copies that file into the next prompt). At most 2 regenerations per pair (EFF-4).
4. **Lint fails at attempt 3:** stop retrying that pair; it goes to David at step 4 with its lint output.

Fix nothing yourself. You don't edit an artifact, a prompt or a lint rule to make a check pass. If a
check is wrong, that's a harness bug: stop and tell David.

## 3. Screenshot review

1. `node .claude/scripts/design/screenshots.mjs docs/ui/mockups`: every artifact at 1440 and 390 wide,
   light and dark, into `docs/ui/mockups/_shots/` and `docs/ui/mockups/review.html`. A page error, a failed
   request or zero artifacts fails the script.
2. Event `gate.screenshots` with the real exit code, `expect: "0"`.
3. Commit the artifacts, shots, review page, feedback files and trail:
   `git add docs/ui/mockups docs/trace/ui/DM`, with trailers `Agent: main`, `Plan: ui`, `Node: DM`.

## 4. Stop for David: gate G2

Tell David, in this order, and then stop:

1. The path to `docs/ui/mockups/review.html` to open in a browser.
2. Per pair: attempts used, lint result, `blocked` receipts and their notes, any pair that hit the cap.
3. Nothing about how the mockups look. You haven't judged them and won't.

David picks a direction (or asks for changes: a steer, recorded as `steer(D<n>,pause2)` with his words in
`detail`, which goes back through steps 1–3 with the steer written to the feedback file). When he picks:

1. He writes the direction's concept paragraph himself (EFF-9): the governing idea, what it excludes, the
   category default it rejects. Record it in `docs/ui/styles.md` under the chosen style, as he wrote it.
2. Event `signoff(<HEAD sha>)` with `detail` naming the chosen direction.
3. Push `feature/ui/DM` and open a PR into `ui` (each needs David's approval). David merges in the GitHub
   UI; his merge is G2 passed.

## Never

- Write, edit, shorten or "clean up" a ui-designer prompt, or dispatch ui-designer without one.
- Describe or score a mockup's appearance, or report one as good.
- Edit an artifact the agent wrote, or a lint rule, to get a pass.
- Exceed 3 attempts per pair or 3 dispatches at once.
- Dispatch any other agent from this skill.
