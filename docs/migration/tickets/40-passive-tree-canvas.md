# 40 — PassiveTreeView canvas

**Phase** 5 · **Depends on** 16, 17, 27, 37

## Goal
Port `src/Classes/PassiveTreeView.lua` (1,856 LOC). **Risk item #1. Budget this as its own project — no library does this and there is no shortcut.**

## Scope
- **Coordinate transform**, rebuilt per frame (`:280-291`): `treeToScreen` / `screenToTree` from `zoom = 1.2^zoomLevel`, `zoomX`/`zoomY` pan offsets, scaled by `min(viewportW, viewportH) / tree.size`.
- **Hit testing** (`:302-332`): currently an **O(n) linear scan over every node, every frame**, squared-distance against per-node `node.rsq`. ~1,300 base nodes plus cluster subgraphs, plus a second pass over the compare spec. No spatial index.
- **Connectors are arbitrary textured quads, not lines**: `DrawImageQuad` with 8 position floats *and* 8 UV floats into an atlas region (`:712-741`). Arcs between nodes are curved sprites.
- **8 interleaved draw sub-layers** (15/20/25/30 and 99/100 for hover) — nodes, connectors, frames, overlays.
- Node artwork from sprite sheets with **per-zoom mipmap variants** (`PassiveTree.lua:262-295`).
- Interaction: Ctrl-click zoom, shift path-trace mode with an accumulating `tracePath`, drag-pan, node hover → BFS path highlight or dependency highlight, node power heat-mapping.
- **Nested rendering**: `src/Modules/ItemSlotHelper.lua:10-37` (`DrawViewer`) and `TimelessJewelSocketControl` embed a live tree view inside a dropdown-sized rect. The renderer is already re-entrant and viewport-parameterised — **that is the saving grace, keep it that way.**

## Implementation
- **SkiaSharp `SKCanvasView`** or a custom control overriding `Render` with `ICustomDrawOperation`. Non-negotiable.
- `DrawImageQuad` → `SKCanvas.DrawVertices(SKVertexMode.Triangles, ...)` with texture coords. Correct primitive, and it batches.
- **Avalonia.Controls.PanAndZoom** (`ZoomBorder`) for matrix pan/zoom, wheel zoom-to-cursor and bounds clamping — replaces the manual clamping at `PassiveTreeView.lua:275-278`.
- **Replace the O(n) hit test** with `RBush` (R-tree) or a uniform grid. This is free performance the Lua version never had.
- **Do not use Avalonia.Svg.Skia here** — tree assets are PNG sprite sheets, not SVG.

Also in scope: `TreeTab.lua` (2,888) — spec management, node search, mastery/jewel popups, tree import/export. 130 `build.` references, the highest in the codebase.

## Acceptance
Visual parity with the Lua renderer at several zoom levels; hit testing correct at all zoom levels; frame time measured and documented.

## Libraries
SkiaSharp, Avalonia.Controls.PanAndZoom, RBush.
