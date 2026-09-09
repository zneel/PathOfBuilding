# 06 — Headless mod-store dump harness

**Phase** 1 · **Depends on** 01 · **Blocks** 10, 11, 21

## Goal
Isolate *setup* bugs (item/tree/skill mod construction) from *math* bugs, before any DPS number is ever compared.

## Scope
`src/HeadlessWrapper.lua` already runs the whole app with no graphics via `src/_SimpleGraphic.def.lua` stubs, exposing `newBuild()`, `loadBuildFromXML(xml, name)`, `build`, `runCallback`. It already runs in CI. Extend it to dump, per test build:

- `env.modDB`
- `env.enemyDB`
- `env.itemModDB`
- `env.player.mainSkill.skillModList`
- `env.minion.modDB` where present

Format with `ModDB:Print()`-style output (`src/Classes/ModDB.lua:336-370`), sorted deterministically, as JSON.

C# side: build the same DB, dump, diff.

## Gotchas
- **Sort output deterministically.** Lua's `pairs()` iteration order is undefined; any diff that depends on it is noise.
- Include `mod.source` in the dump. Source-prefix filtering is used by every query, and mis-attributed sources are the failure mode this harness exists to catch (see ticket 10's note on `createMod` overload sniffing).

## Acceptance
- Dumps for all 5 builds in `spec/TestBuilds/3.13/` committed as golden files.
- A snapshot test in C# that diffs against them.

## Libraries
**Decision: a hand-rolled snapshot comparer, not Verify.** `ModStoreSnapshot.Verify(name, text)` implements the same `.received`/`.verified` workflow with `POB_MODSTORE_ACCEPT=1 dotnet test` as the regenerate switch. Verify.XunitV3 remains registered in `Directory.Packages.props` if the tree later wants its diff tooling; swapping in is one csproj line plus one line per call site. Not worth a dependency for behaviour already covered and tested.

**`env.minion.modDB` is absent from all five 3.13 test builds.** `env.minion` is `env.player.mainSkill.minion` (`CalcPerform.lua:1316`), and none of them has a minion *main* skill — four carry a golem, but not as main. The generator handles the store and a test pins the gap, but that path is unexercised and #22–#27 will have **no minion-setup oracle** until a build with a minion main skill is added to the corpus (ticket 07).
