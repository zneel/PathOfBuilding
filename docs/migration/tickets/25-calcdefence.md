# 25 — CalcDefence

**Phase** 3 · **Depends on** 23 · **Blocks** 26

## Goal
Port `src/Modules/CalcDefence.lua` (3,874 LOC) — `calcs.defence` (`:651`), `calcs.buildDefenceEstimations` (`:1674`).

## Scope
Resistances, armour/evasion/block, life/mana/ES/ward pools, EHP and max-hit estimation.

`buildDefenceEstimations` is iterative (it solves for a hit magnitude), so it is more sensitive to rounding than most of the engine — verify against ticket 02's `LuaCompat` early.

## Acceptance
Golden build defensive keys match.
