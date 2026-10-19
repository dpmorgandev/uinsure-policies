# Kit dispatch: {{stage}} · {{direction}} · {{screen}} · attempt {{attempt}}

You are the kit-builder. Build the kitchen sink, the page that shows every kit component in every state,
then reply with your receipt only (its shape is in your agent instructions). The sections below were
copied verbatim from their source files by a script; nothing in them is a summary.

## Mandates: these outrank everything below

1. **Write only `src/web/kit/sink/`** (its `main.tsx`, its components and its CSS Modules) and
   `tests/web/kit/sink.test.tsx`. `src/web/kitchen-sink.html` already loads `./kit/sink/main.tsx`.
2. **Render every example of every component.** Import each component's `<id>.examples.tsx`
   (`src/web/kit/<tier>/<id>/`) and render each entry inside a wrapper with
   `data-comp="<id> · <tier> · <state>"`. `<tier>` is `primitive`, `pattern`, `layout` or `project`, and
   `<state>` is the example's `state`, unchanged. An audit compares these tags with the catalog: a missing
   component or state fails the stage.
3. **Both themes, side by side, in one page.** Each section renders once inside `data-theme="light"`
   and once inside `data-theme="dark"`, so one screenshot shows both. Import `../tokens/tokens.css` and
   the two `@fontsource` faces (`@fontsource/atkinson-hyperlegible-next`,
   `@fontsource/atkinson-hyperlegible-mono`).
4. **The type specimen**, in an element with `data-specimen="type"`:
   - every size in the scale, in both families;
   - a column of SHAs, a column of counts and a column of money, set with
     `font-variant-numeric: tabular-nums`, so tabular figures can be judged from a screenshot.
5. **The icon catalogue**, in an element with `data-specimen="icons"`: every Lucide icon named below, each
   rendered from `lucide-react` with `data-icon="<name>"` and its name as text. An icon that
   `lucide-react` doesn't export can't be imported; list it as text with `data-icon-missing="<name>"`
   instead, never a substitute.
6. **Sections in catalog order** (primitives, patterns, layout, project), each component headed by its
   id, with a skip list of ids at the top of the page.
7. **The test** renders the sink and checks that it shows at least one `data-comp` for every component
   listed below.
8. **Tokens only; imports only** `react`, `react-dom/client`, `lucide-react`, the two `@fontsource`
   packages and kit components.
9. No Uinsure name, logo or colours; no emoji.
10. **Your final message is the receipt JSON object only.** No code and no prose in it.

## Previous attempt

{{feedback}}

## The direction

{{territory}}

## Tokens: `src/web/kit/tokens/tokens.css`, verbatim

```css
{{tokens}}
```

## The components, by tier (every one is built)

{{available}}

## The icons to catalogue (from `docs/ui/screens.json`)

{{icons}}

## Read these yourself for the look

{{references}}
