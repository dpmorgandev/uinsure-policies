# Design dispatch: {{stage}} · all territories · attempt {{attempt}}

You are the creative-director. Read the files listed below yourself, then write exactly one file,
`{{output}}`, and reply with your receipt only (its shape is in your agent instructions). The sections
below were copied verbatim from their source files by a script; nothing in them is a summary.

## Mandates: these outrank everything below

1. **Read every file in "Read these yourself" before writing.** The research is your raw material. You
   are given no summary of it, and no earlier design directions: invent from the evidence.
2. **Exactly three territories, genuinely unlike.** Each has its own governing idea, layout archetype and
   tone register. Palette or font variations of one layout are one territory, not three.
3. **Concept first; layout derived from the concept.** The governing idea is what the design expresses,
   not a theme it applies. The layout archetype is the composition that idea demands.
4. **Both halves, one concept.** Each territory must serve the trace explorer (evidence, read-only) and the
   policy workbench (forms, problems, money and dates) under the same idea. Name a data-true signature
   move for each half.
5. **Every territory is cited.** At least 5 distinct research entry IDs; at least 2 from the workbench
   prior art (B) and at least 1 from the explorer prior art (P or C). Every ID must exist in the research
   files. An uncited territory is dropped.
6. **Depart from the category default** that `prior-art-agent-traces.md`, `prior-art-ci-traceability.md`
   and `prior-art-workbench.md` name, and say how.
7. **The mood's keywords and avoid list bind every territory.** The governing metaphor is yours to
   propose; the mood file deliberately gives none.
8. **Palette:** every token, light and dark, as `#rrggbb` with its source (a research ID, or
   `"proposed"`). Text tokens must reach 4.5:1 on the background in both themes; a token used only for
   glyphs and marks may be marked `"graphicsOnly": true` and must reach 3:1. **Type:** a sans and a mono,
   each a researched T-id.
9. **No Uinsure name, logo or colours;** no imitation of any vendor's brand.
10. **Your final message is the receipt JSON object only.** No territory content and no prose in it.

## Previous attempt

{{feedback}}

## The brief: context

{{brief}}

## Read these yourself

{{reads}}

## The file you write: `{{output}}`

Exactly this shape. Every string is non-empty; `name` is kebab-case.

```json
{
  "version": 1,
  "territories": [
    {
      "name": "kebab-case-name",
      "governingIdea": "one sentence: the concept the design expresses",
      "singleMindedProposition": "one sentence: what the reader should come away believing",
      "excludes": ["at least 3 things this territory refuses"],
      "layoutArchetype": "the composition the idea demands; unique across territories",
      "toneRegister": "unique across territories",
      "signatureMove": {
        "explorer": "one move, drawn from real trail data, naming that data",
        "workbench": "one move, drawn from real API data, naming that data"
      },
      "categoryDeparture": "how it departs from the category defaults the prior art names",
      "derivedStyle": {
        "palette": {
          "background": { "light": "#rrggbb", "dark": "#rrggbb", "source": "K1 or proposed" },
          "surface": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "border": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "accent": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "textPrimary": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "textSecondary": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "primary": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "error": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "success": { "light": "#rrggbb", "dark": "#rrggbb", "source": "..." },
          "warn": { "light": "#rrggbb", "dark": "#rrggbb", "source": "...", "graphicsOnly": false }
        },
        "type": { "sans": "T3", "mono": "T3" },
        "dials": { "design_variance": 3, "motion_intensity": 1, "visual_density": 5 }
      },
      "citations": ["B3", "B9", "C1", "M6", "D1"]
    }
  ]
}
```

The parent checks the file with a script after you stop. It fails unless all three territories are
complete, their names, archetypes and tones are pairwise distinct, every citation resolves, each cites
both halves, every text token passes contrast in both themes, and every type entry is a researched T-id.
