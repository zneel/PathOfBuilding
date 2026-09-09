# 37 — Tooltip renderer

**Phase** 5 · **Depends on** 28, 35 · **Blocks** 38, 39, 40

## Goal
Port `src/Classes/Tooltip.lua` (698) and `GemTooltip.lua` (261). **This is not a `ToolTip` — it is a rich document renderer**, and half the application's information density lives in it.

## Scope
- `GetDynamicSize` (`:194`), `CalculateColumns` (`:216`) — **reflows content into multiple columns** when it would overflow the viewport, then flips placement to stay on screen.
- Item headers are **9-slice sprite composites**: 15 rarity/type configs × left/middle/right PNGs, each with its own height and text offsets (`:412-445`), plus influence icon overlays.
- Content: mod lines with background bars, separators, colour escapes, multi-column layout.
- `TooltipHost.lua` (27) — the mixin giving any control a `tooltip` + `tooltipFunc`.
- Save/restore of draw colour (`:613`).

Host in an Avalonia `Popup`; render the content on a custom-drawn control (`Render(DrawingContext)` or `SKCanvas`).

## Gotchas
- **`CalculateColumns` depends entirely on ticket 28's text measurement.** If widths differ from the Lua host, tooltips reflow differently and will need re-tuning wholesale. This is the strongest argument for keeping the bitmap atlases.
- Tooltips are drawn at layers 99/100 and must escape their parent's clip. Avalonia `Popup` handles this; the tree's embedded viewers (ticket 40) do not get that for free.

## Acceptance
Visual diff against reference screenshots for a sampled set of items across all 15 header configs.
