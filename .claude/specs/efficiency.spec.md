# efficiency.spec.md

Status: DRAFT: Research and Recommendation written; Decision pending David
Researched: 2026-10-19 · Settles: `docs/ui-requirements.md` §5 (agentflow's costly lessons, carried as rules), for the `ui` plan

Principle: **agentflow's cost was mostly rework, not generation.** The one measured run (finance-track-01)
spent $45.95 on 54 bugs found late, about 89% of them cascades of 5–10 root causes. The design stages
reran because gates passed work they never measured, prompts lost the skill's mandates, and screens
drifted from a kit described in prose. So this spec is about **the failures that caused rework** as much
as about tokens. Each lesson becomes a rule with a mechanical leg and an owner, or it is cut. A lesson
with no enforcement is a reading list.

Scope: one static React app (about 12 data-dense application screens, no marketing surface, light and
dark, desktop and phone) built feature by feature through this repo's existing loop. Anything that only
pays at factory scale is listed under *Not built* with its reason.

## Dependencies

**Upstream:** `docs/ui-requirements.md` (the brief), `requirements.md` §3 (the loop, the retry ladder),
`guardrails.spec.md` (every law needs a mechanical leg), `agents-skills.spec.md` (roster, models),
`orchestration.spec.md` (gates run by the orchestrator, agents' final message is one JSON object).

**Downstream:** `ui-research`, `design-system`, `screens`, `ui-defects`, `web-testing` and the
agents-skills and guardrails amendments each own some of the rules below (the *Owner* column). This spec
holds the rule; the owner holds its mechanism.

## Research

Sources: agentflow_phase3 `LESSONS.md` (15 lessons), `DECISIONS.md`, `RESEARCH.md`,
`docs/fix-bugs-cost-and-speed-priority-plan.md`, the design-stage skills and agents, and all 98 plan files
(`plans/active` 12, `parked` 9, `superseded` 2, `ideas` 1, `archive` 69: 28 bugs, 22 features, 17
investigations, refactor-010, delivery-013). Read by eight read-only subagents on 2026-10-19. Key
citations were spot-checked against the files by the main session: investigate-014:29,
investigate-002:226, feat-086:31, `LESSONS.md`:184, investigate-022:80, feat-087:40.

### R1. Where the money went

- **Measured:** finance-track-01 `/fix-bugs`: $45.95, 54 bugs, about 5h at concurrency 5; 463 plan files
  for 54 bugs; about 7.4K output tokens per Sonnet dispatch (`fix-bugs-cost…plan.md`:6, :178, :188).
  bug-012/013's run: $24.37 actual against $99.50+ projected, because "~89% of the 81 bugs were
  downstream cascades of ~5-10 actual root causes" (`LESSONS.md`:85-86).
- **Estimated:** mockups $5–10 a run, stylesheet, screens and builder runs $0.50–2.00 each, a whole
  12-feature project $60–100 on API billing (`RESEARCH.md`:15-33, `agent-sdk-auth-providers.md`:170).
- **The worst single drivers:** bugs found late and fixed one at a time (about 28 min each); verbose
  agent output; regeneration and judge loops stacked on the same screen (screens could run about 10
  generations and 8 judge calls per screen in the worst case); a whole stage run in one subagent that
  truncated; parse failures that re-bought the work (bug-215: "$6.55, 0 stages", three full-cost
  retries of a stage whose output only failed to parse).
- **No cap was ever measured to save anything.** Every "after" figure is a projection. This spec
  treats caps as limits on the worst case, not as savings.

### R2. Gates that didn't gate (the costliest class)

- **Built but never wired:** "the craft judge was BUILT + CALIBRATED but NEVER WIRED" (investigate-022:80);
  the wireframe mapper "had NO caller in the live pipeline" (investigate-016:390, bug-205).
- **Fail-open:** "a quality gate that degrades to WARN when its measuring loop was SKIPPED is not a gate —
  it reports PASS exactly when the work is most likely to be bad" (`LESSONS.md`:184); empty vocabulary
  silently disabled the dark-band check (investigate-003, bug-005); a deleted gate script read as
  `[PENDING]` (feat-107:575-583).
- **Counted matches, never omissions:** parity passed a home page missing about 80% of its mockup's
  content (investigate-005:177-182, bug-201); an import with no render counted as "used" (bug-202).
- **Inside the agent's own turn:** "detection lived inside the same turn budget as the work it verifies"
  (investigate-010:143); a monolithic `/analyze` subagent truncated mid-stage and its self-check died
  with it (refactor-009:38).
- **Success reported for unfinished work:** auto-merge failed and the run said `clean` (bug-014); a plan
  archived as `partial` with load-bearing scope unshipped (phase1-step-038).
- **Checker and generator disagreed:** audits that demanded one valid implementation (`fixed` vs
  `sticky`, bug-212), disagreed with themselves on paths (bug-213), or corrupted a TS generic with a regex
  (bug-214).

### R3. The shadow dispatch

- investigate-014:29, in David's words: "I drove the ui-designers with lighter prompts than the skill!"
  The real `/mockups` run referenced the logo/`<img>` rule 21 times; the hand-composed one, 0 times.
  "hand-composition inherently summarizes, dropping exactly the load-bearing mandates" (investigate-014:39).
  It had happened before (investigate-011) and recurred because the fix was filed, not built.
- The built defence, `audit-dispatch-prompt.mjs`, catches **added** layout lists, not **dropped**
  mandates (investigate-014:52). The cure proposed, injecting the hard mandates by script so they can't
  be summarised away, was never built (investigate-014:53, 68).
- The same loss happens without a shadow: a preamble that summarised policy "keeps the prohibitions …
  drops the permissions" and cited a field no artifact contains (bug-032:136-142).

### R4. What actually moved design quality

- **Method as a skill the generator runs, with worked exemplars**, not injected context and not a
  downstream grader (feat-106:219-229, `LESSONS.md`:174). "A judge only rejects; it can't make output
  good. The fix is the GENERATOR" (feat-106:174-176).
- **A method for application surfaces specifically.** investigate-023: a brief with no marketing surface
  gave 16 of 16 dead screens with every gate passing: "The application surface has a way to be judged and
  no way to be made" (investigate-023:290). **The explorer is entirely application surface.**
- **Canonical code inlined, not described:** prose-only kit rules drifted 86% across 12 parallel screens
  (investigate-002:226); fixed by inlining the canonical HTML bytes plus an audit (bug-003/004, feat-002:291).
- **Judge by screenshot:** "an authoring agent's self-report about its own pixels is worthless"
  (bug-033, `LESSONS.md`:193); two dispatches shipped an invisible graphic and reported success.
- **A judge is trusted only after calibration:** the first judges ranked the operator's 3/10 as a 7 and a
  dead screen at 9 (feat-099:26, feat-107:321-330). "A self-consistent judge that inverts the operator's
  ranking is worse than no judge" (`LESSONS.md`:164).
- **States are designed, not assumed:** mockups show "the happy populated state … primitives ship
  stateless" (feat-101:41); "cutting the weakest 10%" also deleted real domain states (feat-107:286-294).
- **A section list per screen:** a page shipped as an 18-line stub because the mockup was truncated out
  of the dispatch (feat-087:31-41).
- **One concept up front:** the same content scored 9.5 with a governing idea and 3 without
  (feat-097:32); the injection and gate plumbing "was never the lever" (feat-097:34).
- **A real-browser sweep:** one probe "found 11 distinct bugs in ~5 min at ~$0 that all existing tiers
  passed clean" (feat-086:31-32).
- **What didn't move it:** permission clauses alone (bug-030: "permission ≠ execution"), "apply all the
  craft" (feat-098: blank sections, worse than restrained), cross-project distinctiveness machinery
  (feat-089/090/092: generic output came back anyway).

### R5. What this harness already holds

Not to be rebuilt: gates run by the orchestrator, never taken from an agent; one JSON object as each
agent's final message, with a schema check; a fresh-context reviewer; the retry ladder with attempt-3
escalation; per-agent models and effort (tester and reviewer on Opus, builder and security on Sonnet, git
on Haiku); the activity hook that logs every Read, Grep, Edit, Write and Bash; the harness lock; the
privacy scan; David's two pauses. This spec extends these to the design stages; it adds no parallel
machinery.

## Options

**A. A lessons spec** (a catalogue of agentflow lessons for agents and David to consult). Cheap to write;
no mechanical leg; agentflow's own record shows prose rules drift (investigate-002, bug-002/003, feat-093:
"a prose rule alone CANNOT fix a prose-override failure"). Rejected as the vehicle.

**B. Port agentflow's machinery** (craft gates with calibration corpora, the concept-survival ledger,
the distinctiveness registry, per-class model routing, `/preview-cost`, the multi-round fix loop).
Covers everything; built for many projects; agentflow's authors say most of it "was never the lever"
(feat-097:34). Rejected.

**C. Rules with legs, and a budget** (recommended). Only the lessons that are cheap here and that this
harness doesn't already cover become rules, each with its mechanical leg and owner; a dispatch budget
per stage; an explicit *Not built* list.

## Recommendation

**C.** One spec, named for what most of agentflow's cost actually was: rework. A separate `lessons.spec`
would duplicate `docs/lessons.md`, which is this project's own record and stays so.

**Second pass (2026-10-19; David: no judge, "that is overkill"; "another pass on what is overkill").**
The first draft had 27 rules. Cut to 12 by three tests, then EFF-13 added from the output-contract
research:
- **Already held** by this harness (R5) → noted, not restated: gates run in the parent (was EFF-3),
  done only through commits (EFF-6), the JSON-only final message and its schema check (EFF-22, EFF-27,
  pending the output-contract research in Open questions), grouping by root cause and fixing upstream once
  (EFF-25, EFF-26: already the failure route and the Defects table in `ui-requirements.md`).
- **No LLM judge** → cut: the design-reviewer agent, its calibration (EFF-18), the merged judge pass
  (EFF-24). The lesson behind them survives as the new EFF-8: David judges, from screenshots a script
  renders.
- **Heavier than the risk at about 12 screens** → cut or merged: saving every prompt to the trail and
  diffing it (EFF-8, EFF-10) is replaced by one hook that refuses a hand-written design prompt (new
  EFF-5); separate rules for fixtures, invariants and omissions (EFF-1, 2, 4, 5, 16) become one gate rule
  (EFF-1) and one parity rule (EFF-2).

## Decision: PENDING (David)

## Contract

### Rules

IDs are `EFF-n`. **Owner** is the spec that holds the mechanism; **Leg** is how it is enforced.

| ID | Rule | Leg | Owner | From |
|---|---|---|---|---|
| EFF-1 | **A gate can fail.** A gate we write ships with one known-bad fixture it fails on, and treats an empty, missing or unparseable input as FAIL, never WARN or pass | The harness self-test runs each gate against its bad fixture | guardrails amendment | investigate-022, `LESSONS.md`:184, bug-005 |
| EFF-2 | **Parity counts omissions.** A built screen is checked against its derived reference by iterating the expected `data-req` regions and states, so a missing section fails | Part of the per-node parity gate | `ui-defects` | investigate-005, bug-201, feat-087 |
| EFF-3 | **One subagent per screen or phase**, never a whole stage in one subagent; the parent checks between units | Stage skills dispatch per unit | orchestration amendment | investigate-010, refactor-009 |
| EFF-4 | **Regeneration is capped at 2 per screen**, triggered only by a failed mechanical gate or by David, with the reason fed back. A third failure goes to David | The stage skill counts attempts in the trail | `ui-defects` | `mockups/SKILL.md`:79, bug-215 |
| EFF-5 | **No hand-written design prompts.** The stage skill renders each design agent's prompt by script from a checked-in template, which carries the hard mandates (honesty, states, tokens only, no external URLs, the screen's section list) copied from their source files, never paraphrased | A `PreToolUse` hook on the `Agent` tool refuses a `ui-designer` dispatch whose prompt isn't byte-identical to a rendered prompt file. The self-test asserts each template carries every mandate | agents-skills, guardrails amendments | investigate-014, bug-032 |
| EFF-6 | **A ported skill reads this project and nothing else** | The porting rule in `ui-requirements.md` §3: Reads / Writes block, path lint, no `Skill` tool for design agents | guardrails amendment | David, 2026-10-19 |
| EFF-7 | **The generator runs a method skill for application screens**, with one good/dead exemplar pair | `interface-craft`, ported and trimmed, preloaded into `ui-designer` | agents-skills amendment | investigate-023, feat-106 |
| EFF-8 | **David judges, from screenshots.** No agent's report about its own output is evidence of how it looks. A script renders every screen at 1440 and 390 in light and dark into one review page per gate | The screenshot script runs in the parent at G2, G4 and pause 2 | `ui-defects` | bug-031, bug-033 |
| EFF-9 | **One concept, written by David:** the governing idea, what the design excludes, the category default it rejects | A required section of the chosen direction; G2 isn't passed without it | `design-system` | feat-097 |
| EFF-10 | **Kit code inlined, not described.** The shared screen preamble carries the kit's canonical code as literal bytes | Built by script from the kit files, as part of EFF-5's rendering | `screens` | investigate-002 (86% drift), feat-002 |
| EFF-11 | **States are designed.** Every data component shows populated, empty, loading and error, plus incomplete and in-flight where the trail has them; restraint never cuts a state | The screens gate checks each catalogued component's states are present | `screens` | feat-101, feat-107 |
| EFF-12 | **Mechanical checks for mechanical faults:** one lint for screens and app (tokens only, no hex or inline style, no external URLs, `data-theme` set), and one real-browser sweep of every route, both themes, both widths, failing on console errors, blank renders and failed requests | ESLint plus a small grep script; a Playwright sweep as a gate from U0b | `design-system`, `web-testing` | investigate-020, feat-086 |
| EFF-13 | **Artifacts on disk, a receipt in the message.** A design agent writes every artifact (HTML, markdown, JSON) to its declared path and returns only a receipt: one JSON object `{status, files[], notes}` with `notes` capped. No artifact content in the message, no prose in an artifact | `gate-subagent.mjs` (SubagentStop) extended with a receipt schema for `ui-designer` and an artifact check per listed file: it exists and isn't empty; `.html` starts with `<!doctype html>` and holds no markdown fences; `.md` starts with its required `# ` heading, with no preamble and no wrapping fence; `.json` parses and validates against its schema. A failure blocks with the reason, using the existing restate-then-escalate path | guardrails amendment | `ui-designer.md`:159-167, analyze workers' "Output discipline", bug-010, bug-215 |

### Dispatch budget

Worst-case limits, not targets. Reaching one stops the stage for David.

| Stage | Dispatches | Model |
|---|---|---|
| Research (`ui-research`) | 3–4 parallel search and read subagents, once | Sonnet |
| Mockups (G2) | 2–3 directions × 2 screens (overview, one interior) = 4–6 `ui-designer`, at most 2 regenerations each | Sonnet |
| Stylesheet (G3) | **amended 2026-10-19 (David):** the kit is 71 React components with tests, so at most 9 `kit-builder` dispatches (8 batches in 4 dependency waves), up to 3 in parallel, at most 2 regenerations each. Was 0–1, set when the stage meant tokens and a static preview | Sonnet |
| Screens (G4) | 1 `ui-designer` per remaining screen (about 10), up to 4 in parallel, at most 2 regenerations each | Sonnet |
| Build nodes | the existing loop and retry ladder, unchanged | as `agents-skills.spec.md` |

About 28 dispatches for the design phases in the normal case (was about 20 before the kit moved into
the stylesheet stage), and no judge calls.

### Not built

| agentflow machinery | Why not here |
|---|---|
| **Any LLM craft judge**: `mockup-craft-reviewer`, `interface-craft-reviewer`, `/visual-review` scoring, calibration pairs and corpora (feat-094/099/106/107) | David: overkill. agentflow's judge needed three calibration rounds and still inverted the operator's ranking; David's eye on rendered screenshots (EFF-8) is the gate at G2, G4 and pause 2 |
| Saved-prompt archive and prompt diffing (`docs/_dispatches/`, `audit-dispatch-prompt.mjs`) | EFF-5's hook refuses the hand-written prompt outright, which also catches dropped mandates, the gap agentflow never closed |
| Cross-project distinctiveness fingerprint and registry (feat-089, investigate-008/012) | One app: nothing to collide with |
| Per-client design-skill authoring, registry, injection verifier, consistency gate (feat-090/091/092/095) | Produced the truncation it was meant to prevent (feat-090:276); one concept (EFF-9) does the job |
| Creative-director stage, narrative spine, unlock states, surface classifier (feat-097/100/104, bug-030) | No marketing surface; David picks among 2–3 directions |
| Concept-survival ledger and scorecard (feat-102, delivery-012) | Factory measurement; EFF-1 and EFF-2 cover the integrity it bought |
| Per-bug-class model routing, `/preview-cost`, `/quota-status`, pipeline USD budgets (bug-012, feat-051..055) | Projections never measured; the dispatch budget bounds the worst case |
| Multi-round `bugs.yaml` fix loop, stale-bug detection, systemic-fixer (bug-011/013/203, idea-01) | Defects go through the existing failure route and `/plan-bug` |
| Wide parallel fan-out and its 18-dimension drift audit (feat-002) | At about 10 screens, 4-wide plus EFF-10 and EFF-12 is enough |
| Utility-class resolution audit (bug-028) | Only if `design-system` chooses Tailwind; otherwise moot |
| Wireframe digest, chrome capture, build-vs-buy ladder, CDN HEAD-checks (feat-103/105/085/084) | No client wireframes, no existing app, no vendors; EFF-12 bans external URLs |
| Phase-resume machinery (refactor-009) | Units are small; a failed unit is re-run |
| Stock photography as the atmosphere lever (bug-031/032) | The explorer's imagery is the trail itself; the lever survives as one honest, data-true signature graphic per screen, owned by `design-system` |

### Output contracts: what agentflow did, and what's ported

agentflow layered three defences, each added when the one before was bypassed: a Zod schema per agent
(`packages/orchestrator-contracts/src/`, e.g. `security.ts`:131-142); sentinel-wrapped JSON
(`<<<TASK_OUTCOME>>>`, "Return ONLY the sentineled JSON", `perceptual-reviewer.md`:94-108); and a
four-strategy extractor (SDK structured output → sentinel → balanced-brace scan → diagnostic). Prose was
kept out of artifacts by the file-vs-message split ("ALWAYS write HTML output to the file path … NEVER
include HTML in response text … Response should ONLY contain file path and status",
`ui-designer.md`:159-167) and per-file "Output discipline" blocks ("Output ONLY raw markdown … Start with
exactly `# Competitive Research` — no preamble", `analyze/research.md`:15-16), with schema validators
such as `validate-screens.mjs` as the only mechanical leg on artifacts. The failures: bug-010 (security
returned prose three times; the parent accepted it and routing silently skipped; "the contract isn't
enforced — it's aspirational") and bug-215 (an end-anchored regex `\}\s*$` failed on JSON-then-prose and
re-ran every stage three times at full cost: $6.55, 0 stages).

**This harness already holds the strong half.** `gate-subagent.mjs` parses the final message as exactly
one JSON object, validates it against its schema, and on failure blocks with a restate instruction, then
escalates after a cap. That is bug-010's fix and bug-215's lesson (retry the reply, not the work) already
in place. **Ported:** the file-vs-message split and the artifact checks, as EFF-13, in the existing hook.
**Not ported:** sentinels (the stop hook reads the last message directly), the Zod package (JSON Schema
is already the contract language), the four-strategy extractor (a strict parse plus restate is simpler
and already proven here), and "Output discipline" prose as a defence on its own (kept only as the
template's wording, EFF-13 is the leg).

## Open questions

None at this draft. Settled on 2026-10-19: no LLM judge (David); output contracts as EFF-13.
