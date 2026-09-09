# 15 — Item model and item-text parsing

**Phase** 2 · **Depends on** 13, 14 · **Blocks** 31, 39

## Goal
Port `src/Classes/Item.lua` (2,736 LOC) — item state, plus the text dialect that 20,792 LOC of uniques and every `<Item>` in every build file are written in.

## Scope
- Item model: sockets, links, mods, implicits/explicits, corruption, enchants, influences, rarity, quality, mod ranges.
- Item **text parsing and serialisation** — the round-trip that `src/Data/Uniques/*.lua` and build XML `<Item>` bodies depend on. See the variant/tag markup dialect at `src/Data/Uniques/amulet.lua:5-25`.
- `src/Modules/ItemTools.lua` (413) — mod-line value ranges, scaling, formatting. `itemLib.applyRange` at `:96`.

## Gotchas
- Serialisation must round-trip **byte-identically** or build files written by the C# port become unreadable by the Lua version, breaking the community ecosystem during any transition period.
- Mod ranges (`{a-b}` syntax) interact with crafting and with `applyRange`. Port the range representation before the crafting UI (ticket 39) touches it.

## Acceptance
- Every unique in `src/Data/Uniques/` parses and re-serialises to identical text.
- Every `<Item>` in `spec/TestBuilds/3.13/*.xml` round-trips identically.
