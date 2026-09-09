# 38 — CalcsTab and the breakdown panels

**Phase** 5 · **Depends on** 34, 37

## Goal
Port the calculation display: `src/Classes/CalcsTab.lua` (773), `CalcSectionControl.lua` (550), `CalcBreakdownControl.lua` (789).

## Scope
**Section layout data is a gift.** `src/Modules/CalcSections.lua` (2,638 LOC) is **declarative section descriptors** — port it as data, not code. Loaded via `LoadModule` at `CalcsTab.lua:151`.

**Masonry layout.** `CalcsTab:Draw` (`:226-290`) is shortest-column packing over three section groups with variable column spans — not a `Grid`, not a `WrapPanel`. Write a custom `Panel` overriding `MeasureOverride`/`ArrangeOverride`. **~80 LOC, and it will be better than the current code.**

**Breakdown tables.** `DrawBreakdownTable` (`CalcBreakdownControl.lua:571`) is a hand-laid-out table of heterogeneous rows: colour-escaped mod names, source hyperlinks, per-cell tooltips. Use `Avalonia.Controls.DataGrid` for the tabular part — its column virtualisation matters, these run to hundreds of rows. `DrawRadiusVisual` (`:655`) draws a circle-of-effect diagram — custom render.

**Detachable floating panes.** `CalcSectionControl` sections detach into floating overlay panes (`:234-368`) with their own click/drag/release handling that **bypasses `ControlHost` entirely**. `build.overlayPanes` (`Build.lua:1209-1245`) is a hand-rolled second z-plane that **pre-consumes mouse events before the normal input pass**, plus a pinned `CalcBreakdownControl`. → **Dock.Avalonia**, which handles float/dock/proportion persistence.

Also `PowerReportListControl.lua` (153) and `src/Modules/CalcBreakdown.lua` (255, breakdown-string generators — UI-only, gate behind ticket 21's `buildBreakdown` flag).

## Acceptance
Breakdown values match the Lua version for a golden build; panes float, dock and persist.

## Libraries
Avalonia.Controls.DataGrid, Dock.Avalonia.
