# 28 — Bitmap font atlas and the colour-escape text pipeline

**Phase** 4 · **Depends on** 27 · **Blocks** 33, 37–42

## Goal
Pixel-identical text measurement. **This is the hardest subsystem in the platform layer** and the one most likely to produce a long tail of visual regressions.

## Scope
| Function | Sites | Semantics |
|---|---:|---|
| `DrawString(l,t,align,height,font,text)` | 138 | `align` ∈ `LEFT/CENTER/RIGHT/CENTER_X/RIGHT_X`. Text carries inline `^N` / `^xRRGGBB` colour escapes that change colour mid-string. |
| `DrawStringWidth(height,font,text)` | 108 | Escape-aware physical pixel width. |
| `DrawStringCursorIndex(height,font,text,cx,cy)` | 8 | Pixel coord → byte index. Caret placement. |
| `StripEscapes(text)` | 13 | Exact impl at `_SimpleGraphic.def.lua:298`: `gsub("%^%d",""):gsub("%^x%x%x%x%x%x%x","")` |

Font namespace is fixed and tiny: `FIXED` (16 sites), `VAR` (190), `VAR BOLD` (8), `FONTIN` (13), `FONTIN SC` (98), `FONTIN ITALIC` (4), `FONTIN SC ITALIC` (3).

## The decision that matters
**Keep the bitmap atlases.** The originals are `runtime/SimpleGraphic/Fonts/*.tga` + `.tgf` metrics — one TGA per point size (10, 12, … 64). That is *why* widths are stable integers.

Write a `.tgf` parser and upload the `.tga` sheets as `SKImage`s. ~300 LOC of custom code, and it:
- guarantees pixel-identical layout,
- sidesteps font substitution and hinting entirely,
- avoids the Fontin licensing question.

**Why this is not optional:** layout sizes throughout the app are computed by calling `DrawStringWidth()` at runtime — popup widths (`Main.lua:1680`), tooltip widths (`Tooltip.lua:387`), tooltip column reflow (`Tooltip.lua:216`). Switch to vector fonts and **every hand-tuned pixel constant in 47k lines of UI code becomes subtly wrong.**

If vector is chosen anyway: ship the TTFs, use `SkiaSharp.HarfBuzz`, and budget re-tuning across ~250 call sites.

## Also in scope
- Escape parser producing coloured runs. **Escapes appear in *data*** — item mod strings, tooltip lines, stat names, `CalcSections` entries — not just view code, and can appear mid-word inside mod text.
- `StripEscapes` — port the two gsubs directly.
- `DrawStringCursorIndex` — cumulative-advance scan, ~30 lines.

## Acceptance
`DrawStringWidth` returns byte-identical integers to the Lua host for a corpus of strings sampled from real tooltips and mod text, across all 7 fonts and all shipped sizes.
