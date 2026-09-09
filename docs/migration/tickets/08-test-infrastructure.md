# 08 — Test infrastructure and differential fuzzing

**Phase** 1 · **Depends on** 05, 06, 07

## Goal
The harness that runs the oracles, plus a fuzzer to find what hand-written tests miss.

## Scope
1. **Port the 43 busted spec files** in `spec/System/` to xUnit incrementally. (The directory holds 44 entries; the 44th is `SampleCharacter.json`.) `dotnet/Pob.Tests/Infrastructure/spec-inventory.json` is the machine-readable survey: 43 files, 545 static `it(` cases, each mapped to its unblocking ticket. These encode *intent* — regression cases with explanatory names, a decade of fixed edge cases — which goldens do not capture. Style reference: `spec/System/TestOffence_spec.lua:17-33` builds a character programmatically then asserts on `build.calcsTab.mainOutput.X`.
   Files include `TestOffence_spec`, `TestDefence_spec`, `TestAilments_spec`, `TestImpale_spec`, `TestTriggers_spec`, `TestSkills_spec`, `TestItemMods_spec`, `TestBifurcatedCrit_spec`.
2. **Differential fuzzing.** The headless wrapper is scriptable: generate random builds (random tree allocations, random items from `src/Data/Mod*.lua`, random gem setups), run both engines, diff. This finds the tag-evaluation corners hand-written tests miss.
3. Benchmark baseline on day one — the Lua engine's speed is a real product constraint and regressions are invisible without measurement.

## Acceptance
- All 43 spec files ported or explicitly deferred with a reason.
- Note `TestBuilds_spec.lua` reports 1 static test but generates one per (build, output key) at run time — several thousand assertions.
- Fuzzer running in CI on a nightly schedule.
- BenchmarkDotNet baselines for `Sum`, `More`, `EvalMod`, `calcDamage`.

## Libraries
xUnit v3, CsCheck or FsCheck for property-based fuzzing, BenchmarkDotNet, dotnet-trace / dotnet-counters for allocation profiling.
