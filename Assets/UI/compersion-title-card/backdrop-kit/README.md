# Compersion title-card backdrop kit

Source: [Figma — Compersion Title Card & In-Game Text Assets](https://www.figma.com/design/g5NDCTmSeMNUDbCqd1Dyfm/Compersion-%E2%80%94-Title-Card---In-Game-Text-Assets?node-id=0-1)

Use these alongside the existing transparent type sprites in the parent directory. The exported art deliberately replaces the earlier disconnected purple and blue rectangles with one dark, storybook palette.

## Assets

| File | Figma artboard | Exported bounds | Intended use |
| --- | ---: | ---: | --- |
| `EXPORT-01—Full-Screen-Vector-Backdrop.svg` | 520×1052 | 520×1052 | Full-screen aspect-fill background |
| `EXPORT-02—Title-Backdrop.svg` | 920×300 | 923×303 | Title and poly-flag panel; 1.5 px border bleed on each edge |
| `EXPORT-03—Content-Panel-Wide.svg` | 920×260 | 923×263 | Definition-lead and definition-body panels; 1.5 px border bleed on each edge |
| `EXPORT-04—Compact-Panel.svg` | 920×190 | 923×193 | Combined pronunciation/noun panel; 1.5 px border bleed on each edge |
| `EXPORT-05—Continue-Ribbon.svg` | 920×32 | 920×32 | Bottom prompt ornament |
| `MOCKUP—Dramatic-Fade-Sequence.png` | 520×1052 at 2× | 1044×2108 | Visual reference, not the runtime background |

The small difference between the panel artboard dimensions and exported SVG bounds is the visible outer stroke. Do not crop it away.

Every `EXPORT-*.svg` also has a transparent `@2x.png` derivative in this folder for Unity projects that do not import SVGs. Their dimensions are 1040×2104, 1846×606, 1846×526, 1846×386, and 1840×64 respectively.

The five runtime PNGs are imported as single UI Sprites with transparent alpha, bilinear filtering, clamp wrapping, and mipmaps disabled. The framed panels use 9-slice metadata so their border bleed and corner strokes survive the mobile layout:

| Runtime PNG | Pixels Per Unit | Sprite border (L/B/R/T) |
| --- | ---: | ---: |
| `EXPORT-02—Title-Backdrop@2x.png` | 400 | 120 / 120 / 120 / 120 |
| `EXPORT-03—Content-Panel-Wide@2x.png` | 400 | 100 / 100 / 100 / 100 |
| `EXPORT-04—Compact-Panel@2x.png` | 400 | 100 / 100 / 100 / 100 |

The full-screen backdrop and continue ornament are unsliced. `Assets/UI/UI.prefab` assigns all five PNGs through `GameUIManager.Compersion Title Skin`; do not assign the mockup as a runtime surface.

## Palette

- Ink: `#020507`
- Midnight teal: `#071013`
- Deep teal: `#0B3438`
- Panel teal: `#08282C`
- Warm umber: `#2A1715`
- Burgundy shadow: `#2A1013`
- Primary gold: `#D9A94F`
- Title face: `#F8DEA1`
- Content gold: `#F6D58C`
- Warm cream: `#E9DDBF`
- Highlight cream: `#F7F0DC`

Use the gold at roughly 55–72% opacity for secondary rules and borders. Keep body copy cream/gold rather than white.

## Reveal choreography

1. Title: `0.00s`
2. Pronunciation and “noun”: `0.36s`
3. Definition lead: `0.72s`
4. Definition body: `1.08s`
5. Continue prompt: `1.62s`

Each beat should use a `420ms` opacity fade with an `18px` downward-settle ease-out. Preserve the existing fast-finish behavior: the first tap or Space during the sequence reveals everything immediately; a later tap or Space dismisses the card.

## Integration constraints

- Preserve the current Spike/letter completion callback and story order.
- Block gameplay and mobile controls while the card is active, then release only this presentation's suspension on dismissal.
- Keep the deterministic title-card admin checkpoint working and keep the admin panel above the title card.
- Scale the full-screen backdrop with aspect-fill. Respect safe areas for all text and prompt content.
- Keep panel borders proportional. If the Unity SVG pipeline cannot preserve them, render lossless raster derivatives and configure the panels as 9-sliced sprites.
- Keep the supplied polyamory flag directly in the title composition.
- Treat the PNG mockup as a visual QA reference; use the separate backdrop/panel assets at runtime.
