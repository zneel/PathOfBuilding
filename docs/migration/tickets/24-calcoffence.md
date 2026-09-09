# 24 — CalcOffence

**Phase** 3 · **Depends on** 19, 23 · **Blocks** 26

## Goal
Port `src/Modules/CalcOffence.lua` (6,263 LOC) — `calcs.offence` at `:348`. The largest single file in the engine and the hottest.

## Scope
All damage, DPS, ailment and crit math.

**Hot path — `calcDamage` (`:70-142`)**: recursive over the conversion chain (Physical→Lightning→Cold→Fire→Chaos), ~8 `Sum`/`More` queries per level, called per damage type × per pass (main hand / off hand / generic, `passList` at `:1998-2065`) × per ailment source. Easily thousands of store queries per evaluation.

## Gotchas
- **`globalOutput` / `globalBreakdown` are file-locals acting as hidden parameters** (`:65-67`). Thread them as real parameters — ticket 20 requires it.
- **Dynamic string keys everywhere**: `output[damageType.."MinBase"]` (`:91`), `"Min"..damageType.."Damage"` (`:116-119`). **Precompute all combinations into static readonly arrays indexed by a damage-type enum. Never concatenate in a loop.**
- `damageStatsForTypes` (`:53`) memoizes name arrays per type-flag mask — port as a precomputed `ModName[][]` indexed by the 5-bit mask.
- `output` is a plain string-keyed table with ~474 keys populated for a typical build. `src/Modules/CalcBase.lua` has LuaLS `---@class Output` annotations giving you the field list for free.
- Hook the `preDamageFunc` and `postCritFunc` behaviour keys (ticket 19) here.

## Acceptance
Golden build DPS values match to the tolerance ladder in ticket 07, then tightened to exact.

## Libraries
BenchmarkDotNet — this file is where the port's performance is won or lost.
