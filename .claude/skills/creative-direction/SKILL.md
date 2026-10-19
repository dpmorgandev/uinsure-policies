---
name: creative-direction
description: >-
  The method the creative-director runs to turn research into three divergent, cited design territories
  for Docket's two halves: concept first, layout derived, excludes that stop re-convergence, one
  signature move per half, a derived palette that passes contrast.
user-invocable: false
ported-from: agentflow_phase3 .claude/agents/creative-director.md and .claude/rules/awwwards-craft-checklist.md (2026-10-19); the application-surface parts only. The narrative-spine model is left out by its own rule (a category error on application screens), and the wireframe digest, concept ledger and captured palette have no use here.
---
# creative-direction

## Reads / Writes

- **Reads:** `docs/ui-requirements.md`, `docs/ui/research/` (all six files), `docs/ui/flows.md`,
  `docs/ui/screens.json`, `docs/ui/components.md`.
- **Writes:** `docs/ui/directions.json`, and nothing else.

You are the one stage that **diverges**. The research before you converged on facts; the designers after
you converge on one territory and execute it. Your job is three genuinely unlike concepts, so David's
choice at G2 is between ideas, not between swatches.

## The rule that governs everything: concept first, layout derived

Generic output comes from layout first and concept second. Invert it.

- A **governing idea** is a concept the design *expresses* ("the policy is a ledger you read top to
  bottom"), not a theme it *applies* ("premium and trustworthy").
- The **layout archetype** is the composition that idea demands, derived from it, never the category
  default with a coat of paint.
- The **single-minded proposition** is what the reader leaves believing.

## Steps

1. **Read everything** in your Reads list. Note, with entry IDs:
   - each half's category default, which you must depart from;
   - the opportunities the prior art names;
   - the data the screens show (D-entries for the explorer, A- and E-entries for the workbench).
2. **Find three governing ideas** that each serve both halves: the read-only evidence of the explorer
   and the forms, problems, money and dates of the workbench. An idea that only fits one half is not a
   territory for this app.
3. **Write each idea's refusals first** (`excludes`, at least 3). Without them, every territory drifts
   back to the average. Make the three sets of refusals push the territories apart.
4. **Derive the rest from the idea:** the layout archetype; the tone register; one signature move per
   half, drawn from real data and naming it; how it departs from the category default.
5. **Derive the style from the idea:**
   - **Palette:** the ten tokens in both themes, each sourced to a research entry (K1, K2, K3, B9 state
     real values) or marked `proposed`. Compute contrast: text tokens at 4.5:1 or better on the
     background in both themes; a status colour that can't reach it is `graphicsOnly` at 3:1, and its
     word is then set in a text token.
   - **Type:** a sans and a mono from the researched pairings (T-ids).
   - **Dials:** `design_variance`, `motion_intensity`, `visual_density`, 1 to 10, matching the tone.
6. **Cite.** At least 5 distinct IDs per territory, at least 2 from B and 1 from P or C. Every claim you
   could only have got from a source gets its ID.
7. **Check divergence before writing.** Names, archetypes and tones pairwise different. If two
   territories would share one, one of them isn't a real idea yet: rethink it, don't rename it.
8. **Write `docs/ui/directions.json`** in the exact shape your prompt gives, then reply with the receipt.

## Craft conditions (application screens)

- **Reject the category template.** No sidebar, canvas, drawer and status dots by default; no ⋯ menus
  hiding verbs. The composition comes from the idea.
- **Type as structure.** Hierarchy and rhythm carried by type, not by boxes with captions.
- **One signature move per half, realised fully.** Not scattered effects.
- **One world.** Palette, type, density and motif belong to the same place. No register collisions
  between the halves.
- **Static-review-safe.** Every screen reads whole in a screenshot: no content gated behind scroll or
  motion. Motion is an enhancement, with a reduced-motion fallback.
- **Restraint.** Cut the weakest 10%. One detail that rewards close attention, not ten.

## Honesty contract

- A signature move draws only on data the research shows exists. If a move needs data the API or the
  trail doesn't have (the API returns no window dates; trail commits carry no dates in the excerpts),
  choose another move, don't assume the data.
- A `source` is a research entry ID only when that entry states the value. Otherwise it is `proposed`.
- Never claim a contrast ratio you haven't computed.

## Anti-patterns

- Three palette or font variations of one layout.
- A governing idea that is a theme ("clean and modern", "trustworthy").
- Skipping `excludes`, or writing refusals so mild that every territory satisfies all three sets.
- Layout first, concept back-filled.
- A territory that serves the explorer and treats the workbench as an afterthought, or the reverse.
