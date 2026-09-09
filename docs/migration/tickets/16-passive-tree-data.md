# 16 — PassiveTree data and sprite atlases

**Phase** 2 · **Depends on** 03 · **Blocks** 17, 40

## Goal
Load tree data and the sprite atlas map. Data layer only — rendering is ticket 40.

## Scope
Reference: `src/Classes/PassiveTree.lua` (1,062).

- `src/TreeData/` holds **41 tree-version directories**: `2_6`, `3_6` … `3_29`, with `_ruthless`, `_alternate`, `_ruthless_alternate` variants from 3.22 onward. 61 `.lua` files, 4,112,746 LOC, dominated by one `tree.lua` per version (~90k–139k LOC each, 2.9 MB for `3_29`).
- Format is JSON mechanically dumped to Lua table syntax (`return {` with `["key"]= value` — note the `]=` spacing). Lossless round-trip back to JSON is near-free.
- **Assets sit flat in each version dir, there is no `assets/` subdirectory.** 625 PNG + 112 JPG + 32 WebP: `skills-3.jpg`, `mastery-active-effect-3.png` (4.7 MB), `group-background-3.png`, `ascendancy-3.webp`, `jewel-radius.png` (1.5 MB). `sprites.lua` holds atlas UV coordinates and `extraImages` placements.
- Sprite sheets have **per-zoom-level mipmap variants** (`PassiveTree.lua:262-295`) and the only async image load in the codebase (`:263`).
- Node pre-parse, `node.rsq` radius-squared caching (`:397`).

## Gotchas
- **Load one tree version on demand, never all 41.** Current Lua loads lazily by necessity; the port should keep that.
- TreeData ships to users — 831 files under `part="tree"` in `manifest.xml`.

## Acceptance
All 41 versions load; sprite lookups resolve; memory for a single loaded version measured and documented.

## Libraries
SkiaSharp (`SKImage.FromEncodedData`) for PNG/JPEG/WebP decode.
