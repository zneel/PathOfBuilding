# 39 — ItemsTab

**Phase** 5 · **Depends on** 15, 35, 37

## Goal
Port `src/Classes/ItemsTab.lua` (5,055 LOC) — the largest UI file, and 5k lines of tangled UI + model + persistence.

## Scope
- 20+ equipment slots, item sets, shared item sets.
- Item editor: sockets, links, enchants, corruption, crafting, mod ranges.
- Unique and rare item databases with search and filters: `ItemDBControl.lua` (400), `ItemListControl.lua` (387), `NotableDBControl.lua` (293), `MinionListControl.lua` (107).
- `ItemSlotControl.lua` (185) — a composite: `ComboBox` + embedded tree preview + flask-activate checkbox. → `UserControl`.
- `GemSelectControl.lua` (854) — deferred here from ticket 36.
- `TimelessJewelListControl.lua` (352), `TimelessJewelSocketControl.lua` (57).

**What ports cleanly:** the socket *editor* is ordinary widgets — per-socket colour `DropDownControl`s at 64px pitch plus link `CheckBoxControl`s between them (`ItemsTab.lua:465-515`).

**What does not:** the socket/link *visualisation* in tooltips, the embedded tree viewer per jewel slot, and the drag-and-drop mesh.

## Gotchas
- **Cross-tab reach-through is pervasive.** `ItemsTab.lua:241-245` mutates `build.skillsTab.socketGroupList` and `build.mainSocketGroup`; `:1189` adds `build.controls.mainSkillMinion` as a drag target. **Extract a model layer first** — this tab cannot be ported incrementally without one.
- Coupling density: 98 `build.`/`buildFlag` references in this file alone.
- `src/HeadlessWrapper.lua` and `src/Classes/CompareEntry.lua` (552) both construct tabs *without* UI chrome to run calcs headlessly. **They tell you exactly which parts of this tab are model and which are chrome** — read them before starting.

## Acceptance
Full item editing round-trips through ticket 31's build format byte-identically.
