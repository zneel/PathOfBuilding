# 27 — Avalonia shell and the layer-sorted Skia draw queue

**Phase** 4 · **Depends on** 01 · **Blocks** 28, 33, 37–40

## Goal
The window, event loop, and the drawing primitive every custom-rendered control sits on. Independent of phases 2–3 — run this concurrently from day one.

## Scope
Reference: `src/_SimpleGraphic.def.lua` — a complete, verified-exhaustive `---@meta` declaration of the host API. Treat it as the interface definition.

**Live rendering surface (everything else in the stub is dead):**
| Function | Sites | Semantics |
|---|---:|---|
| `SetDrawColor(r,g,b,a)` / `SetDrawColor(escapeStr)` | 418 | Current tint. String overload takes `^xRRGGBB`/`^7`. |
| `DrawImage(handle,l,t,w,h[,tcL,tcT,tcR,tcB])` | 278 | Axis-aligned textured quad. **`handle=nil` → solid rect — this is how PoB draws all solid fills.** |
| `SetDrawLayer(layer[,subLayer])` | 101 | Painter's-algorithm layer. Draw calls are **sorted, not immediate.** |
| `SetViewport(x,y,w,h)` / `SetViewport()` | 57 | Scissor + origin. No-arg resets to full screen. **Global stack of depth one.** |
| `DrawImageQuad(handle,x1..y4[,s1..t4])` | 26 | Arbitrary 4-point textured quad (rotated tree art, arc bands). |
| `GetDrawColor` / `GetDrawLayer` | 5 | Save/restore, e.g. `Tooltip.lua:613` |

**Implementation:**
- Avalonia 11 + `Avalonia.Skia`. Get the `SKCanvas` via `ImmediateDrawingContext.TryGetFeature<ISkiaSharpApiLeaseFeature>()`.
- `DrawImage` → `SKCanvas.DrawRect` / `DrawImage` with `SKPaint.ColorFilter` for the tint.
- `SetViewport` → `SKCanvas.ClipRect`.
- **`DrawImageQuad` → `SKCanvas.DrawVertices(SKVertexMode.Triangles, positions, texCoords, colors, paint)`** — handles the arbitrary-corner UV mapping exactly. Existing library, no custom code.
- `SetDrawLayer` → sort a `List<DrawCmd>` by `(layer, subLayer, seq)` before flushing. **~50 lines of custom code, unavoidable.**

**Draw layer map, for reference:**
| Layer | Use |
|---|---|
| -100 | Tree background (`Main.lua:1487`) |
| 0 | Default content |
| 1 | Tree tab (`TreeTab.lua:474`) |
| 5 | Top/side bar chrome; dropped dropdown list |
| 10 | Modal scrim + popup (`Main.lua:438-442`) |
| 15/20/25/30 | Tree sub-layers: nodes, connectors, frames, overlays |
| 99/100 | Tooltips, node hover |
| 1000 | Launch-level error overlay (`Launch.lua:123`) |

Also: `RenderInit("DPI_AWARE")` → app manifest config, no-op at runtime. `GetScreenSize`/`GetScreenScale` → `ClientSize`/`RenderScaling`. `ConExecute("set vid_mode 8")`/`vid_resizable 3` → hardcode window setup, do not port the cvar console.

## Gotchas
`src/Modules/ItemSlotHelper.lua:10-37` nests a whole passive-tree render inside an item slot via the viewport stack, and its own comment warns it clobbers the global viewport and draw layer. The draw queue must support save/restore properly even though the Lua original does not.

## Acceptance
A test harness rendering a known draw-call sequence to an `SKSurface` and diffing against reference PNGs.

## Libraries
Avalonia 11.x, Avalonia.Skia, SkiaSharp.
