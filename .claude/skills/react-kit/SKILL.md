---
name: react-kit
description: >-
  How the kit-builder writes Docket's React kit: one folder per component with its CSS Module, examples
  and index; tokens only; props in, markup out; behaviour tests per state; BUSY-1; accessibility; the
  kitchen sink.
user-invocable: false
---
# react-kit

The kit is the concept made reusable. Every screen is later composed only from it, so a component that
is wrong here is wrong everywhere. Build each one small, honest and complete.

## One component

```text
src/web/kit/<folder>/<id>/
  <Name>.tsx            the component (named export), typed props
  <Name>.module.css     its styles: var(--token) only
  <id>.examples.tsx     export const examples: { state: string; element: JSX.Element }[]
  index.ts              export { <Name> } from './<Name>'; export type { <Name>Props } from './<Name>';
tests/web/kit/<id>.test.tsx
```

`<Name>` is the id in PascalCase (`rule-chip` → `RuleChip`). The folder and tier tag are given for each
component in your prompt.

1. **Props describe what is shown,** not where it comes from. No fetching, no context lookups for data,
   no business rules. A refund sum renders the API's numbers; it never computes them.
2. **States are props.** Every state in the row's States column must be reachable by props alone, so
   the examples file can show it. A state that needs the API's response takes that response as a prop.
3. **Compose, don't copy.** Use an already-built component (relative import) rather than restyling the
   same thing. Use `Button` for any action, so BUSY-1 is honoured everywhere.
4. **Semantics first:** `<button>` for actions, `<a>` for navigation, `<ol>` for an ordered spine,
   `<table>` with `<caption>` and `<th scope>` for tables, a label for every input, `aria-describedby`
   for hints and errors, `aria-invalid` on an invalid field.

## Styling

- Only the custom properties in `tokens.css`: `--color-*`, `--font-sans`, `--font-mono`, `--text-<px>`,
  `--space-<n>`, `--radius`, `--duration-*`. Stylelint fails a raw colour.
- Theme comes from `data-theme` on an ancestor; components never read the theme.
- Status is a word, a Lucide glyph and a colour; colour alone is never the signal. Status text never sits
  on an `--color-accent` fill.
- Figures (counts, SHAs, money) use `font-variant-numeric: tabular-nums`. SHAs, IDs, paths and JSON use
  `--font-mono`.
- Motion only where it carries order (a rung completing, a spinner), with `--duration-*`. Those durations
  drop to 0 under `prefers-reduced-motion`.
- Layout stays readable at 390 wide: no horizontal page scroll; wide tables scroll inside their own
  container.

## BUSY-1

Any control that starts a process takes `busy` and a `busyLabel`. While busy it:
- renders a `spinner` (still under reduced motion);
- shows the busy label;
- is `disabled` with `aria-busy="true"` and `aria-disabled="true"`;
- ignores clicks.

The form keeps its values.

## Examples: observed data only

Example props come from the reference mockups your prompt names (`docs/ui/mockups/r2/<territory>/`) or
from the catalog row: real rule IDs, SHAs, dates and amounts from the trail and the API's recorded
responses. Where a state needs a value no source shows, the example says so visibly ("placeholder").
`state` is the exact text of one entry in the row's States column; one example per entry.

## Tests (Vitest + Testing Library)

One file per component, `tests/web/kit/<id>.test.tsx`, importing from
`../../../src/web/kit/<folder>/<id>/<Name>`:
- every example in `<id>.examples.tsx` renders without throwing;
- queries by role and accessible name, not by class or test id;
- the component's own contract: the right element and attributes per state (for example
  `aria-invalid`, `aria-current`, `aria-expanded`), keyboard behaviour where it has any;
- BUSY-1 where it applies: disabled, `aria-busy`, label, and a click while busy doesn't fire;
- no snapshot tests, no timers left running, no network.

## The kitchen sink (the `sink` batch only)

`src/web/kit/sink/main.tsx` renders every example of every component, in catalog order. Each example sits
in a wrapper with `data-comp="<id> · <tier> · <state>"`. Each section appears once under
`data-theme="light"` and once under `data-theme="dark"`, side by side. The sink also holds the type
specimen (`data-specimen="type"`) and the icon catalogue (`data-specimen="icons"`, one
`data-icon="<name>"` per icon). It imports `../tokens/tokens.css` and the two `@fontsource` faces. An
audit compares the tags with the catalog, so a missing tag is a failed stage, not a cosmetic gap.

## Self-check before the receipt

Re-read each file once against your prompt's mandates:
- every listed component and every state is present;
- there is no raw colour;
- only approved imports are used;
- tests exist for every component;
- no prose or fences are in the code.

This is a check of your text, not a claim that anything passes.
