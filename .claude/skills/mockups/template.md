# Design dispatch: {{stage}} · {{direction}} · {{screen}} · attempt {{attempt}}

You are the ui-designer. Write exactly one file, `{{output}}`, then reply with your receipt only (its
shape is in your agent instructions). Every section below was copied verbatim from its source file by a
script; nothing in it is a summary.

## Mandates: these outrank everything below

1. **Only observed data.** Every number, status, ID, SHA, name and date on the screen comes from the
   "Data excerpt" section. Nothing is interpolated or invented. Where the screen needs something the
   excerpt lacks, show the gap and name it in your receipt's `notes`.
2. **Every state in the screen entry's `states` list is designed and shown,** each under a label naming
   it, beside the populated view. Restraint never cuts a state.
3. **Every region in the screen entry's `regions` list is present,** wrapped in an element whose
   `data-req` attribute lists that region's requirement IDs, space-separated.
4. **One self-contained static file:** `<!doctype html>` first; one inline `<style>`; no `<script>`; no
   external URL in any `src`, `<link href>`, `url()` or `@import` (plain `<a href>` links to GitHub are
   allowed); `<html data-theme="light">`; `<body data-screen-id="{{screen}}">`.
5. **Tokens only:** colours are CSS custom properties defined in `:root` (light) and overridden in
   `[data-theme="dark"]`, taken from this direction's palette. No hex, rgb or hsl colour anywhere else.
6. **Status is a word, a glyph and a colour,** never colour alone.
7. **The direction's signature move** for this screen's half (below) is the screen's one signature move,
   drawn from observed data only, with a text equivalent in the same `<figure>`. The direction's
   `excludes` are binding: none of them may appear.
8. **Fonts:** name the direction's families with a system fallback stack; no font file or font URL.
9. **No Uinsure name, logo or colours;** no imitation of any vendor's brand; no emoji; no lorem ipsum.
10. **Your final message is the receipt JSON object only.** No artifact content and no prose in it.

## Previous attempt

{{feedback}}

## The direction

{{shared_constraints}}

{{style}}

## The screen

```json
{{screen_entry}}
```

## Its components

{{components}}

## Data excerpt: the only data you may show

{{data}}
