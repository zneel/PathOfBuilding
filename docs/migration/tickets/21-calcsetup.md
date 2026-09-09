# 21 — CalcSetup: environment construction

**Phase** 3 · **Depends on** 17, 18, 20 · **Blocks** 22–26

## Goal
Port `src/Modules/CalcSetup.lua` (1,975 LOC) — `calcs.initEnv` at `:475`. Everything downstream reads the `env` this builds.

## Scope
Ordered steps, from `CalcSetup.lua:475`:
1. `ClearMatchKeywordFlagsCache`
2. New player/enemy/item `ModDb`; set actor links (`player.enemy`, `enemy.enemy`)
3. Base class stats, Life/Mana per level, ManaRegen (a `PerStat` tag)
4. `calcs.initModDB` (`:19`) — config-derived mods
5. Merge every item's modList into `modDB` and per-slot lists
6. Radius-jewel functions, cluster jewels
7. `calcs.buildModListForNodeList` (`:259`) — every allocated passive node
8. Socket groups → gems → supports (`addBestSupport` `:428`) → `calcs.createActiveSkill`

Returns `env` plus `cachedPlayerDB` / `cachedEnemyDB` / `cachedMinionDB` for accelerated re-runs.

Also port `src/Modules/ConfigOptions.lua` (2,380) — **declarative**, each entry has `apply = function(val, modList, enemyModList)` emitting mods. Model as `record ConfigOption(..., Action<ModList, ModList> Apply)`.

## Gotchas
- `env.buildBreakdown` (`:504`) gates all breakdown-string generation. **Gate it behind null in C#** — it is pure UI cost and it is on the hot path.
- Step ordering is significant; mods merged later can be overridden by earlier `OVERRIDE` mods depending on insertion order.

## Acceptance
Ticket 06's mod-store dump matches for all golden builds — this is the ticket that proves setup before any math is compared.
