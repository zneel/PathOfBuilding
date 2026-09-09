# 26 — Triggers, mirages, and full DPS

**Phase** 3 · **Depends on** 24, 25

## Goal
Port the re-entrant tail of the pipeline and close out the engine.

## Scope
- `src/Modules/CalcTriggers.lua` (1,627) — trigger-rate simulation: CoC, CwC, focus, unique triggers. Reads other skills' cached outputs.
- `src/Modules/CalcMirages.lua` (427) — Mirage Archer / Warrior. **Calls `perform` again** (`:52`).
- `src/Modules/Calcs.lua` (872) — `buildOutput` (`:384`), `calcFullDPS` (`:142`), `getMiscCalculator` (`:89`), `buildActiveSkill` (`:357`).

`calcFullDPS` runs a **full `initEnv` + `perform` per skill in the build** — linear in skill count with a very large constant. `env.limitedSkills` (`:363-369`) is the recursion guard; `GlobalCache` keyed by `cacheSkillUUID` (`Common.lua:897`) is the memo.

## Opportunity
With ticket 20's `CalcSession` in place, **`calcFullDPS` parallelises across skills**. This is the single largest available speedup in the port and the main reason the global-state work came first.

## Acceptance
- Full golden corpus passes end to end.
- BenchmarkDotNet comparison against the Lua engine documented.
