# 05 — ModCache parser oracle

**Phase** 1 · **Depends on** 01 · **Blocks** 12–14 (as the acceptance gate)

## Goal
Turn `src/Data/ModCache.lua` into a machine-readable answer key for the ModParser port. **This is free — the corpus already exists in the repo.**

## Scope
`src/Data/ModCache.lua` is 4 MB / 23,215 lines: `c[<mod line>] = { <parsed mod array or nil>, <unparsed remainder or nil> }`, written by `main:SaveModCache` (`src/Modules/Main.lua:315-340`). It covers ~23,207 real mod lines.

1. A ~50-line Lua script reading `modLib.parseModCache` and re-emitting it as JSON using `runtime/lua/dkjson.lua`.
2. Normalise mods to **strings** via `modLib.formatSourceMod` / `formatMod` (`src/Modules/ModTools.lua:231-237`) so comparison is on canonical text, not nested structures.
3. C# side: xUnit `[Theory]` + `MemberData` over the corpus, asserting `Format(Parse(line)) == expected`.

## Why this matters
It gives the parser port a **quantified daily completeness metric**: "we parse 21,840 / 23,215 lines correctly." That number drives the work in tickets 12–14 and tells you when to stop.

## Gotchas
- `ModCache.lua` is emitted as 5 IIFE chunks of 5,000 statements (`ModCache.lua:2,5004,10006,15008,20010`) to dodge LuaJIT's constant limit. The reader must handle that structure.
- Cache regeneration is available via `REGENERATE_MOD_CACHE=1` (`Main.lua:136`) if the corpus needs refreshing against current data.

## Acceptance
- `dotnet/Pob.Tests/oracles/modcache.json` committed (or generated in CI).
- A test that reports the pass count as a first-class number, not just pass/fail.

## Libraries
`dkjson`, xUnit v3, `System.Text.Json` source-gen.
