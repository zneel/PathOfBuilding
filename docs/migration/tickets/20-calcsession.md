# 20 — CalcSession: eliminate global mutable state

**Phase** 3 · **Depends on** 10 · **Blocks** 21–26

## Goal
Thread all implicit global state through an explicit session object. **Do this in the first commit of phase 3.** Retrofitting it after `calcFullDPS` exists is a second rewrite.

## Scope
State that is currently global and implicitly single-threaded:

| State | Where | Strategy |
|---|---|---|
| `GlobalCache.cachedData[mode][uuid]` | `src/Data/Global.lua:356`, keyed by `cacheSkillUUID` (`Common.lua:897`) | Instance field on `CalcSession`, **never static** |
| `modLib.parseModCache` | `ModParser.lua:7020` | Parser instance state |
| `globalOutput` / `globalBreakdown` | `CalcOffence.lua:65-67` file-locals | **Hidden parameters — make them real parameters** |
| `data` | `Modules/Data.lua` | Genuinely immutable after load → `static readonly`, frozen |
| `build` | global | Explicit parameter |
| `colorCodes` | global | Static constant |

## Why this is ticket 20 and not ticket 26
The pipeline is **deeply re-entrant**: `calcs.buildActiveSkill` calls `initEnv` + `perform` again for every skill (`Calcs.lua:357`), `calcs.mirages` calls `perform` again (`CalcMirages.lua:52`), and trigger simulation reads other skills' cached outputs. `GlobalCache` is the memo that keeps this from exploding; `env.limitedSkills` (`Calcs.lua:363-369`) is the recursion guard. Every one of those paths touches global state.

Getting this right is also the precondition for **parallelising `calcFullDPS` across skills** (ticket 26), which is the single largest available speedup.

## Acceptance
- No static mutable state in `Pob.Calc` (enforced by analyzer).
- Two `CalcSession` instances can run concurrently on different builds with identical results.
