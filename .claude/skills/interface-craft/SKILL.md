---
name: interface-craft
description: >-
  The generative method the ui-designer runs to author an application screen that is alive rather than
  a spreadsheet: the question, the place, the data's shape, type, one signature, every state, one action.
user-invocable: false
ported-from: agentflow_phase3 .claude/skills/interface-craft/SKILL.md (2026-10-19), trimmed for one static explorer
---
# Interface craft: author a screen that is alive, don't assemble a table

## Reads / Writes

- **Reads:** the dispatch prompt only (rendered by the stage skill; its data excerpt is the only data).
- **Writes:** `docs/ui/mockups/**`, `docs/ui/kit/**`, `docs/ui/screens/**`: the one path the prompt names.

This is a **procedure**: run it top to bottom for every screen. A critique checklist tells you whether a
screen is right; it never tells you what to do first. Handed only a checklist, competent authors ship
screens that answer every question on it and still read as a spreadsheet.

## The bar

An application screen isn't trying to be remembered. It's trying to make a person **understand something
in under two seconds and feel that it mattered.** The enemy is the spreadsheet: a correct, legible table
that answers its question and gives no reason to care about the answer. Competent and inert is a fail; a
screen that maxes every lever is noise and also a fail. **The bar: does the screen have a point of view
about which fact matters, and does it show the shape of what happened?** No marketing grammar here: no
hero, no slogan, no conversion arc.

## The honesty contract (it outranks the sequence)

This product's whole claim is that its evidence is real. A screen that invents anything breaks the
product, however good it looks.

- **Draw only observed values.** If the trail holds six events, draw six marks. Never interpolate,
  smooth or extrapolate: a line through unobserved points is a defect, not a flourish.
- **Every number, status, ID, SHA, name and date comes from the prompt's data excerpt.** If the screen
  needs something the excerpt lacks, show the gap and say so in your receipt's `notes`.
- **A gap is information.** Render it as a gap. `not built yet` is a fact, not an error.
- **A derived value is legibly derived:** say what it's counted from ("8 of 29 rules, from the matrix").
- **Provenance is part of the datum:** generated, recorded or written; the snapshot's SHA and time.
- **Where two sources disagree, show both** with their sources; never pick one silently.
- **Engagement never licenses invention.** If a screen becomes interesting only by showing something the
  trail doesn't hold, you haven't made it engaging. You've made it wrong.

## The sequence: in order (skipping step 1 is why output is a table)

### 1. Name the question: one sentence, before any layout

Write it literally, first. Examples for this product:
- overview: "Was this really built test-first, and was the AI really checked?"
- rule: "Did this rule's test fail before its code made it pass, and who signed it off?"

If you can't write it in one sentence, the screen is doing two jobs: pick the dominant one. **The answer
is the loudest object on the screen**, louder than its label, the nav or the app name.

### 2. The trail is the place: no photographs

This product's place is its evidence, not a location. There is **no imagery**: no photographs, stock or
illustration, no decorative motif standing in for one. A screen establishes where you are with its
headline answer, the snapshot stamp (SHA and time) and the provenance of what it shows. The entry
screen carries the product's thesis in words and real figures; interior screens trust it and get to the
point. Step 3 carries the visual load, and it can.

### 3. Draw the data's own shape: the move that does the real work

Every interesting dataset has a shape: a FAIL and its fix, a red that came before a green, an attempt
ladder, a gap where a step is missing. **Find it and draw it once, at feature scale,** from observed
values only.

- Ask: "What is the most surprising true sentence this data supports?" Draw that.
- **One graphic at feature scale**, not a sparkline in every row.
- **Position and shape carry the meaning; colour only reinforces.** It must survive greyscale and a
  screen reader: give it a text equivalent (a table or list) in the same `<figure>`.
- **Annotate the shape with the fact.** A mark with no label is a mood.
- **Marks, not lines,** where the interval between events is unobserved.

If the screen's data truly has no shape (a glossary, a not-found page), skip this step honestly. Don't
manufacture a chart.

### 4. Type as structure: the datum is the loudest object

- **Numerals are objects:** counts and columns use `font-variant-numeric: tabular-nums lining-nums`.
- **The mono face is a semantic signal:** it says "machine-observed", so SHAs, rule IDs, file:line,
  commands, timestamps and JSON are mono; prose isn't.
- **Status is a word, a glyph and a colour, never colour alone.**
- A small label (eyebrow) names each region and sets the register.
- The hierarchy must survive 200% text scale.

### 5. One static signature move, realized fully, then stop

Use **the direction's** signature graphic, named in your prompt's style block: not one you invent. It
must read in a still screenshot with no JavaScript; motion doesn't count. Let it recur two or three times
across the screen so it reads as authorship. **If it doesn't help someone read the screen faster, it's
garnish: cut it.**

### 6. Design every state: the half-designed screen is the default failure

Every data region owes: **populated**, **empty** (what's absent, why, and where to go), **loading** (a
static skeleton, never a shimmer), **error** (what failed, in a sentence). Plus the states this domain
has, listed in your prompt's screen entry: in flight, trail incomplete, git-only, not built yet, sources
disagree, redacted, long content. In a static design artifact, **show each state** beside the populated
view, each under a small label naming it.

### 7. One primary action; subordinate the rest

One unmistakable next step per screen (for a reader: the link that answers "show me the proof"). Other
actions are visibly secondary. Advanced detail sits behind a disclosure. Interactive things look
interactive. Density is calibrated to the task, per the direction's dials.

## Restraint: the good version came from editing

After the seven steps, **cut the weakest 10%** of what you added. Keep exactly one detail that rewards
close attention. One signature, one primary action, one graphic.

**Restraint never cuts a state**, the honesty contract, the primary action or accessibility. You may cut
what you added. You may not cut what the domain already had. (An earlier run cut real domain states while
"cutting the weakest 10%": that was a defect in the instruction, now fixed here.)

## Anti-patterns: what "dead" looks like

- **The spreadsheet:** uniform rows of equally weighted fields, no point of view. Fix with steps 1 and 3.
- A chart that interpolates between observed events.
- Sparklines or status dots in every row instead of one real graphic.
- Colour-only status; proportional figures in a column.
- Only the populated state designed.
- A state deleted in the name of restraint.
- A shimmer skeleton; content hidden behind `opacity: 0` until scrolled.
- A marketing hero, slogan or KPI tile with no source, bolted onto a tool.
- "Apply all the craft": ten effects instead of one move.

## Worked exemplar pair (this product's own data)

- **Dead:** the rule page as a key-value table: `Rule SELL-5 · Node F1 · Tests 16/16 · Red 04cae24 ·
  Green 7d3f791 · QA #8 · Sign-off David 2026-10-18 · PR #10`. Every fact is there, all at the same
  weight; nothing says whether red came first or why it matters.
- **Alive:** the same facts with a point of view. The answer leads, as one sentence stating the order
  the trail's own dates show for red 04cae24 and green 7d3f791. The signature graphic draws the two
  commits at their real dates, the QA step and David's sign-off following, and shows no step the trail
  doesn't hold. The test count says where it's counted from. The same nine facts; one is now loudest.

## Hard constraints: a file that breaks one is not done

- **One self-contained file:** `<!doctype html>` first; all CSS inline in one `<style>`; **no
  `<script>`**; **no external URL** in any `src`, `href` of a `<link>`, `url()` or `@import`. Plain `<a
  href>` links to GitHub are allowed.
- **Both themes in one file:** `<html data-theme="light">`, tokens defined as CSS custom properties in
  `:root` and overridden in `[data-theme="dark"]`. The parent switches the attribute to render each theme.
- **Tokens only:** no hex, rgb or hsl colour outside those two token blocks.
- **`<body data-screen-id="<screen id>">`**, and every region from the screen entry wrapped in an element
  with `data-req="<its requirement IDs, space-separated>"`. A region with no tag is a failure.
- **Fonts:** name the direction's family with a system fallback stack; font files aren't available yet,
  so the screenshot shows the fallback. Never load a font from a URL.
- **Real semantics:** headings in order, real tables (`<th scope>`, `<caption>`), `<figure>` and
  `<figcaption>` for the graphic, labelled controls, visible focus styles. Static-safe: renders whole
  with no JavaScript.
- Real copy from the excerpt (no lorem ipsum), no emoji, no Uinsure name, logo or colours.

## Before you report

You can't see your file render, so don't claim anything about how it looks. Check your text instead:
1. Is the question written down, and is its answer the first and largest thing in the markup?
2. Does every number, ID and date appear in your prompt's data excerpt? Point at each.
3. Does the graphic draw only observed events, with a text equivalent?
4. Is every state from the screen entry present and labelled?
5. Is every region tagged with `data-req`? No `<script>`, no external URL, no colour outside the tokens?
6. One primary action, one signature move?

Then reply with the receipt only.
