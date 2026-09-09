# 36 — Simple tabs: Notes, Config, Skills

**Phase** 5 · **Depends on** 34, 35

## Goal
The three tabs that port cleanly. Do these first — they validate the MVVM foundation before the hard screens.

## Scope
**NotesTab** (`src/Classes/NotesTab.lua`, 112 LOC) — a thin wrapper over `EditControl`. Becomes a `TextBox`, ~20 LOC.

**ConfigTab** (1,362 LOC) — an `ItemsControl` over `src/Modules/ConfigOptions.lua` (2,380 LOC) with a `DataTemplateSelector`. **The options table is declarative data — reuse it directly** (see ticket 21). Also `ConfigVisibility.lua`, `ConfigModBrowser.lua` (311), and the custom-mod block editor.
- Its hover preview needs ticket 34's separate debounce path.

**SkillsTab** (1,502 LOC) — `ListBox` + form panel. Socket groups, gem lists, gem quality/level editing, skill sets. Only `GemSelectControl` is exotic and it is deferred: see below.

**Deferred to ticket 39:** `GemSelectControl` (854 LOC) — an `EditControl` subclass that is simultaneously an autocomplete, a fuzzy matcher, and a live tooltip preview host. Map to `AutoCompleteBox` + custom tooltip popup; most of the 854 LOC is matching logic that survives as-is. Needs ticket 37's tooltip renderer.

## Gotchas
Each tab implements `Load(xml)`/`Save(xml)` — **the build file format is serialised straight out of UI classes** (`Build.lua:637, 681`). Extract persistence into the model layer (ticket 31) rather than carrying it in ViewModels.

## Acceptance
All three tabs functional; config changes trigger a correct debounced recalc.
