---
name: stylesheet
description: >-
  Design stage G3 for the ui plan: scaffold the web project, derive the tokens from the chosen territory,
  dispatch kit-builder batch by batch in dependency waves with script-rendered prompts, check every wave
  in the parent, audit the kitchen sink against the catalog, and stop for David's sign-off.
argument-hint: "[territory]"
---
# /stylesheet: design stage G3, the React kit

## Reads / Writes

- **Reads:** `docs/ui/concept.md` (the chosen territory), `docs/ui/directions.json`,
  `docs/ui/components.md`, `docs/ui/screens.json`, `docs/ui/research/mood-type-icons.md`, the mockups
  under `docs/ui/mockups/r2/`, and `.claude/skills/stylesheet/inputs.json`, through the scripts in
  `.claude/scripts/design/`.
- **Writes:** `src/web/` root config and `src/web/kit/tokens/` and `src/web/kit/index.ts` (you, by script);
  `src/web/kit/**` and `tests/web/kit/**` (kit-builder); `docs/ui/kit/` (screenshots and review page, by
  script); `docs/trace/ui/DK/**` (events, feedback).

You are the main session. You scaffold, dispatch, run every check, and stop for David. Rules in force:
`efficiency.spec.md` (the kit's dispatch budget: at most 9 `kit-builder` dispatches, up to 3 in parallel,
at most 2 regenerations each), guardrails G15 and G16, `web-architecture.spec.md` and
`design-system.spec.md`.

- **You never write or edit a kit-builder prompt,** and never write or edit a component, an example or
  a test. You render prompts with the script and pass them unchanged (G15). A failing check goes back to
  the agent as feedback.
- **No edits in this worktree while an agent runs.** Commit between waves.
- **No agent's report about pixels counts.** David judges the sink's screenshots.

## 0. Setup (once)

1. From the `ui` worktree: `git fetch origin`, then
   `git worktree add ../ui-DK -b feature/ui/DK origin/ui`, then `EnterWorktree` into it.
2. `npm --prefix .claude/scripts/design ci` (Playwright, pinned).
3. The territory is the one `docs/ui/concept.md` names (or David's argument).
   - `node .claude/scripts/design/scaffold-web.mjs`: writes the web project's root config. It never
     overwrites a file that exists.
   - `node .claude/scripts/design/derive-tokens.mjs <territory>`: writes `src/web/kit/tokens/tokens.json`
     and `tokens.css` from the territory, with the decided overrides in `inputs.json`.
   - `npm --prefix src/web install`: creates `package-lock.json`.
4. Check every batch renders:
   `node .claude/scripts/design/render-prompt.mjs stylesheet <territory> <batch> > /dev/null` for each
   batch in `inputs.json`. An error stops the stage: fix the input, not the template.
5. `node .claude/scripts/design/coverage.mjs` passes.
6. Commit the scaffold, lockfile and tokens:
   `git add src/web docs/trace/ui/DK`, with trailers `Agent: main`, `Plan: ui`, `Node: DK` in one final
   paragraph with `Co-Authored-By`. Then event
   `node .claude/scripts/render-trace.mjs event '{"event":"setup","detail":"feature/ui/DK from origin/ui <sha>; tokens from <territory>"}'`.

## 1. Build, wave by wave

`inputs.json` `waves` gives the order. A wave's batches run in parallel, at most 3 at once. A wave starts
only when the one before it passes its checks. For each batch in the wave, attempt `n` (starting at 1):

1. Render: `node .claude/scripts/design/render-prompt.mjs stylesheet <territory> <batch> --attempt <n> --out .claude/scripts/design/.out/kit-<batch>-<n>.md`
2. Read that file and dispatch `kit-builder` with its **entire content as the prompt, unchanged**.
3. The stop hooks check each agent: G3 (only `src/web/kit/**` and `tests/web/kit/**`) and G16 (the
   receipt; no fence or prose in code). A blocked stop makes the agent restate; you do nothing.
4. Read each receipt. `status: blocked` → record it, don't retry; tell David at the stop.

## 2. Check the wave (in the parent)

When every agent in the wave has stopped:

1. Run each check and record it as a gate event with the real exit code:
   - `npm --prefix src/web run typecheck` → `gate.kit-typecheck`
   - `npm --prefix src/web run test:kit` → `gate.kit-tests`
   - `npm --prefix src/web run lint:css` → `gate.kit-css`
   
   Use this form:
   `node .claude/scripts/render-trace.mjs event '{"event":"gate.kit-tests","cmd":"npm run test:kit","exit":<code>,"expect":"0","ok":<true|false>,"head":"<HEAD sha>","detail":"wave <w>: <summary>"}'`
2. **A check fails, attempt < 3:** for each batch whose files appear in the failure output, write that
   output, verbatim, to `docs/trace/ui/DK/feedback/<territory>-<batch>-<n+1>.txt`, and run that batch
   again from step 1 with attempt `n+1`. At most 2 regenerations per batch.
3. **Attempt 3 still fails:** stop the stage and tell David, with the output.
4. **The wave passes:** commit it (`git add src/web/kit tests/web/kit docs/trace/ui/DK`), then start the
   next wave.

Fix nothing yourself: not a component, a test, a token or a check. If a check is wrong, that's a harness
bug: stop and tell David.

## 3. Audit the sink (after the `sink` wave)

1. `node .claude/scripts/design/scaffold-web.mjs --barrel` (the kit's `index.ts`, generated), then
   `npm --prefix src/web run typecheck`.
2. `npm --prefix src/web run build`, then `node .claude/scripts/design/audit-kit.mjs --shots`.
   - The audit checks every catalogued component in every listed state, every icon, and the type
     specimen.
   - It also writes `docs/ui/kit/_shots/` and `docs/ui/kit/review.html`.
   - Record it as `gate.kit-audit` with the real exit code.
3. **Audit problems, attempts left:** each problem names a component. Write the audit output, verbatim, to
   the feedback file of the batch that owns that component, or to the `sink` batch when the component's
   examples exist but the sink doesn't render them. Re-run those batches (step 1), their wave's checks
   (step 2), then this step.
4. Commit: `git add src/web docs/ui/kit docs/trace/ui/DK`.

## 4. Stop for David: gate G3

Tell David, in this order, and then stop:

1. The path to `docs/ui/kit/review.html`.
2. Per batch: attempts used, the check results, and any `blocked` receipt with its notes.
3. The audit result.
4. What only his eye can settle:
   - **the type specimen:** do the T5 faces set tabular figures?
   - **any icon the audit listed as missing from `lucide-react`.**
5. Nothing about how the kit looks.

David signs off or steers. A steer is recorded as `steer(D<n>,pause2)` with his words in `detail`. His
words go to the feedback file of the batches he names, and those batches go back through steps 1–3.

When he signs off:
1. Record event `signoff(<HEAD sha>)` and commit the trail.
2. Push `feature/ui/DK` and open a PR into `ui`. Each needs his approval, and G14's design route checks
   the sign-off.
3. His merge is G3 passed.

## Never

- Write, edit or "clean up" a kit-builder prompt, component, example or test.
- Edit `tokens.json` or `tokens.css` by hand; re-run `derive-tokens.mjs` from the territory.
- Exceed 9 dispatches, 3 at once, or 3 attempts per batch.
- Describe or score the kit's appearance.
- Dispatch any other agent from this skill.
