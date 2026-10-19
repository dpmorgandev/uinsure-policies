---
name: mockups
description: >-
  Design stage G2 for the ui plan: dispatch ui-designer once per territory × screen with a script-rendered
  prompt, lint each artifact, render the screenshot review, and stop for David to pick a direction.
argument-hint: "[territory ...]"
ported-from: agentflow_phase3 .claude/skills/mockups/SKILL.md (2026-10-19); the procedure, without the factory machinery
---
# /mockups: design stage G2

## Reads / Writes

- **Reads:** `docs/ui/directions.json` (the territories), `docs/ui/styles.md` (its shared constraints
  section only), `docs/ui/screens.json`, `docs/ui/components.md`, and the files listed in
  `.claude/skills/mockups/inputs.json` (trail, QA and research excerpts), all through
  `.claude/scripts/design/render-prompt.mjs`.
- **Writes:** `docs/ui/mockups/r2/**` (the artifacts, by ui-designer; `_shots/` and `review.html`, by the
  screenshot script); `docs/ui/concept.md` (David's concept, in his words); `docs/trace/ui/DM2/**`
  (events by `render-trace.mjs`, feedback files by you); `.claude/scripts/design/.out/**` (rendered
  prompts, gitignored).

You are the main session. You dispatch, run every check yourself, and stop for David. Rules in force:
`efficiency.spec.md` (EFF-1 to EFF-13) and guardrails G15 and G16. The two that shape every step:

- **You never write or edit a ui-designer prompt.** You render it with the script and pass the file's
  content unchanged. The dispatch guard (G15) re-renders it and denies any difference, a missing header,
  or a summary. A denied dispatch is a defect in your step, not something to work around.
- **No agent's report about pixels counts.** David judges the screenshots; nobody else does.
- **Don't edit files in this worktree while an agent runs.** The stop check attributes uncommitted
  changes to the agent.

## 0. Setup (once)

1. From the `ui` worktree: `git fetch origin`, then
   `git worktree add ../ui-DM2 -b feature/ui/DM2 origin/ui` (a sibling under `.claude/worktrees/`; a path
   relative to the worktree root would nest it inside `ui`), then `EnterWorktree` into it.
   `origin/ui` must hold the merged `feature/ui/DC` (the territories).
2. `npm --prefix .claude/scripts/design ci` (Playwright, pinned; Chromium from the local cache).
3. Check the inputs render: for every territory and screen,
   `node .claude/scripts/design/render-prompt.mjs mockups <territory> <screen> > /dev/null`. Any error
   stops the stage: fix the input (a missing territory, screen or excerpt file), not the template.
4. Event: `node .claude/scripts/render-trace.mjs event '{"event":"setup","detail":"feature/ui/DM2 from origin/ui <sha>"}'`.

The territories are the `name`s in `docs/ui/directions.json`, or the ones David named as arguments. The
screens are the keys of `inputs.json` `screens`: one explorer screen and one workbench screen, so each
territory is seen on evidence and on a form. 3 territories × 2 screens is the dispatch budget.

## 1. Dispatch (per territory × screen, up to 3 at once)

For each pair, attempt `n` (starting at 1):

1. Render: `node .claude/scripts/design/render-prompt.mjs mockups <territory> <screen> --attempt <n> --out .claude/scripts/design/.out/<territory>-<screen>-<n>.md`
2. Read that file and dispatch `ui-designer` with its **entire content as the prompt, unchanged**.
3. The stop hooks check the agent: G3 (it wrote only under `docs/ui/mockups/**`) and G16 (its message is
   the receipt and each listed file is clean). A blocked stop makes the agent restate; you do nothing.
4. Read the receipt. `status: blocked` → record it, don't retry; tell David at the stop (step 4).

Independent pairs run in parallel (one message, several dispatches), at most 3 at a time.

## 2. Check each artifact (in the parent, never in the agent)

For each artifact the receipt lists:

1. Lint: `node .claude/scripts/design/lint.mjs docs/ui/mockups/r2/<territory>/<screen>.html <screen>`
2. Record it as a gate event with the real exit code:
   `node .claude/scripts/render-trace.mjs event '{"event":"gate.design-lint","cmd":"lint.mjs <territory>/<screen>","exit":<code>,"expect":"0","ok":<true|false>,"head":"<HEAD sha>","detail":"attempt <n>: <problems, or clean>"}'`
3. **Lint fails, attempt < 3:** write the lint output, verbatim, to
   `docs/trace/ui/DM2/feedback/<territory>-<screen>-<n+1>.txt`, then go back to step 1 with attempt
   `n+1` (the renderer copies that file into the next prompt). At most 2 regenerations per pair (EFF-4).
4. **Lint fails at attempt 3:** stop retrying that pair; it goes to David at step 4 with its lint output.

Fix nothing yourself. You don't edit an artifact, a prompt or a lint rule to make a check pass. If a
check is wrong, that's a harness bug: stop and tell David.

## 3. Screenshot review

1. `node .claude/scripts/design/screenshots.mjs docs/ui/mockups/r2`: every artifact at 1440 and 390
   wide, light and dark, into `docs/ui/mockups/r2/_shots/` and `docs/ui/mockups/r2/review.html`. A page
   error, a failed request or zero artifacts fails the script.
2. Event `gate.screenshots` with the real exit code, `expect: "0"`.
3. Commit the artifacts, shots, review page, feedback files and trail:
   `git add docs/ui/mockups/r2 docs/trace/ui/DM2`, with trailers `Agent: main`, `Plan: ui`, `Node: DM2`.
   The trailers and `Co-Authored-By` form the message's last paragraph, with no blank line between them:
   git reads trailers from the last paragraph only, and G9 rejects a commit whose `Agent` it can't find.

## 4. Stop for David: gate G2

Tell David, in this order, and then stop:

1. The path to `docs/ui/mockups/r2/review.html` to open in a browser.
2. Per pair: attempts used, lint result, `blocked` receipts and their notes, any pair that hit the cap.
3. Nothing about how the mockups look. You haven't judged them and won't.

David picks a territory (or asks for changes: a steer, recorded as `steer(D<n>,pause2)` with his words in
`detail`, which goes back through steps 1–3 with the steer written to the feedback file). When he picks:

1. He writes the concept paragraph himself (EFF-9): the governing idea, what it excludes, the category
   default it rejects. Record it in `docs/ui/concept.md`, as he wrote it, naming the chosen territory.
2. Commit the concept, then event `signoff(<HEAD sha>)` with `detail` naming the chosen territory, and
   commit the trail. G14 reads that sign-off: the PR is allowed only when it's committed and nothing but
   `docs/trace/ui/DM2/` changed after the signed commit.
3. Push `feature/ui/DM2` and open a PR into `ui` (each needs David's approval). David merges in the
   GitHub UI; his merge is G2 passed.

## Never

- Write, edit, shorten or "clean up" a ui-designer prompt, or dispatch ui-designer without one.
- Describe or score a mockup's appearance, or report one as good.
- Edit an artifact the agent wrote, or a lint rule, to get a pass.
- Exceed 3 attempts per pair or 3 dispatches at once.
- Dispatch any other agent from this skill.
