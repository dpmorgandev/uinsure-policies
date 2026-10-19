# Kit dispatch: {{stage}} · {{direction}} · {{screen}} · attempt {{attempt}}

You are the kit-builder. Build exactly the components listed under "Build these", each with its test and
its examples, then reply with your receipt only (its shape is in your agent instructions). The sections
below were copied verbatim from their source files by a script; nothing in them is a summary.

## Mandates: these outrank everything below

1. **Build every component listed, and only those.** Each goes in `src/web/kit/<tier>/<id>/`:
   `<Name>.tsx`, `<Name>.module.css`, `<id>.examples.tsx` and `index.ts`. `<Name>` is the id in
   PascalCase. Its test goes in `tests/web/kit/<id>.test.tsx`. Write nowhere else, and never edit
   `src/web/kit/tokens/`.
2. **Every state in the component's row is an example.** `<id>.examples.tsx` exports
   `examples: { state: string; element: JSX.Element }[]`, one entry per state in the row's States column.
   `state` is the exact text between commas in that column. The kitchen sink renders these, and an
   audit fails on any missing state.
3. **Tests prove behaviour, not snapshots.** One test file per component:
   - every example renders;
   - its accessibility attributes are correct;
   - any control that starts a process meets BUSY-1: disabled, `aria-busy="true"`, the label says
     what is happening, and it can't be fired twice.
   Use Vitest and Testing Library.
4. **Tokens only.** Colours, fonts, sizes, spacing, radius and durations come from the `var(--…)` custom
   properties in tokens.css below. No raw colour anywhere (Stylelint fails it). Status text is never set
   on an `--color-accent` fill.
5. **Props in, markup out.** No data fetching, no business logic, no computed business values (no date
   windows, no refund maths). A component renders what it is given.
6. **Import only** `react`, `lucide-react` and other kit components by relative path (the list under
   "Already built" for earlier waves; others in this batch). No other package.
7. **Example data is observed data.** Example props use only values that appear in the reference mockups
   or the catalog row (HONEST-1). Anything else is labelled as a placeholder in the example itself.
8. **Accessibility:** WCAG 2.2 AA; status is a word, a glyph and a colour; visible focus; real semantics
   (`<button>`, `<ol>`, `<table>` with `<th>` and `<caption>`); `prefers-reduced-motion` respected.
9. **The direction's `excludes` are binding.** No Uinsure name, logo or colours; no emoji.
10. **Your final message is the receipt JSON object only.** No code and no prose in it.

## Previous attempt

{{feedback}}

## The direction

{{territory}}

## Tokens: `src/web/kit/tokens/tokens.css`, verbatim

```css
{{tokens}}
```

## Build these

{{components}}

## Already built (earlier waves; import by relative path)

{{available}}

## Read these yourself for the look

{{references}}
