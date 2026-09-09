# 07 — Golden output corpus

**Phase** 1 · **Depends on** 01 · **Blocks** 21–26 (acceptance gate)

## Goal
An end-to-end numeric oracle for the calc engine, and enough of it to be trustworthy.

## Scope
**What exists:** `spec/TestBuilds/3.13/` has 5 golden builds, each an `.xml` input plus a `.lua` expected-output table of ~474 numeric keys. `spec/GenerateBuilds.lua` regenerates them by running the real engine and dumping `build.calcsTab.mainOutput` rounded to 4dp. `spec/System/TestBuilds_spec.lua` replays them (tagged `#builds`, excluded from the default busted run per `.busted`).

**Work:**
1. Generalise `GenerateBuilds.lua` to emit `<name>.golden.json` containing `{ inputXml, output }`, covering **all** modes (`MAIN`, `CALCS`) plus `env.player.output`, `env.minion.output`, `env.enemy.output`, and `SkillDPS`.
2. C# replay: load the same XML, run the engine, compare per key with a tolerance ladder — exact for integers and booleans, `1e-9` relative otherwise, tightened to exact once `LuaCompat` (ticket 02) is verified.
3. **Report per-key diffs sorted by magnitude.** That ordering is what makes debugging a 474-key mismatch tractable.
4. **Expand the corpus.** 5 builds is thin for a 43k-LOC engine. Harvest a few hundred real builds from pobb.in / community exports, run them through the Lua headless harness, freeze the outputs. Cost is a few hours of scripting; it is the difference between a port you can trust and one you can't.

## ⚠️ The 5 frozen 3.13 goldens no longer match the live engine

**Do not use them as a correctness gate.** Measured: the live 3.29 engine differs from the values frozen in `spec/TestBuilds/3.13/*.lua` on **541 of 2,164 keys**, and **359 keys are no longer emitted at all**. This is a pre-existing repo condition, not a generator defect — `.busted` already excludes the `#builds` tag from the default run for this reason.

The right cross-check is against `build.calcsTab.mainOutput` in the *current* engine, which is the same value `spec/System/TestBuilds_spec.lua` asserts on. The #8 generator agrees with it on all 2,164 keys exactly.

Some drift is clearly format (`AnyTakenReflect` frozen `0`, live `false`); some is real recalculation (**Dual Savior `AverageDamage` 257.6858 → 19.74, a 13× drop**) and wants a human eyeball before being assumed intentional. Refreshing those five files is a separate, review-worthy change — see the open question in `docs/migration/README.md`.

## Acceptance
- `.golden.json` generation script in `spec/`.
- ≥200 builds in the corpus.
- C# comparison harness with magnitude-sorted diff reporting.

## Libraries
xUnit v3 (`Assert.Multiple` for per-key diffs), AwesomeAssertions or Shouldly for `BeApproximately`. Note FluentAssertions v8 moved to a paid commercial licence — use the MIT fork.
