# 18 — Data loaders

**Phase** 2 · **Depends on** 03, 04, 09 · **Blocks** 19, 21

## Goal
Port `src/Modules/Data.lua` (1,483 LOC, ~60 `LoadModule` call sites) — the orchestrator that assembles all game data into memory.

## Scope
Load and index: skills, gems, item bases, uniques, mods, minions, spectres, essences, monsters, stat descriptions, cluster jewels, pantheons, costs, mod scalability, tattoo passives.

Consume the MessagePack artifacts from ticket 03. Apply the metatables that the Lua loader applies (`Data.lua:725, 1034, 1067`) — **the data files themselves contain no `setmetatable`; the loader adds it**. In C# these become computed properties or explicit index structures.

Key structures: `data.highPrecisionMods` (`:436` — needed by ticket 10's `More`), `data.skillStatMapMeta` (`:1033`).

## Gotchas
- **Respect the eager/lazy split from ticket 03.** Do not port `Data.lua`'s eagerness wholesale — it loads ~608k LOC at startup because Lua gave it no better option.
- `data` is genuinely immutable after load. Make it `static readonly` and freeze it. This is what lets ticket 20 parallelise `calcFullDPS`.

## Acceptance
Cold-start time measured. All data reachable by the same keys the Lua engine uses.
