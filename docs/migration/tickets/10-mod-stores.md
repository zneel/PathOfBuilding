# 10 — ModStore, ModDb, ModList and the query API

**Phase** 2 · **Depends on** 09 · **Blocks** 11, 21–26

## Goal
The storage and query layer. This is the hottest code in the application — `SumInternal`/`MoreInternal` are called tens of thousands of times per build evaluation.

## Scope
Reference: `src/Classes/ModStore.lua` (971), `src/Classes/ModDB.lua` (370), `src/Classes/ModList.lua` (249).

**Two concrete stores:**
- **`ModDb`** (`ModDB.lua:29-35`) — `Dictionary<ModName, List<Mod>>`. Queries enumerate only the named buckets. Used for actor-level DBs (`env.modDB`, `env.enemyDB`, `env.itemModDB`, `minion.modDB`).
- **`ModList`** (`ModList.lua:27-29`) — the object *is* the array; every query is a full linear scan filtering on name. Used for per-item, per-node, per-skill lists (`activeSkill.skillModList`).

Both carry `Multipliers : ModName -> double` and `Conditions : ModName -> bool` — a direct, non-mod fast path set imperatively by calc code (e.g. `modDB.multipliers["WarcryPower"]`, `CalcPerform.lua:1348`).

`ModList` additionally has `MergeMod` (`:73-86`) coalescing same-params `BASE`/`INC`/`MORE` by summing — used heavily by radius-jewel transforms.

**Query semantics, exactly:**
```
Sum(type, cfg, ...names)   Σ over matching mods; tagged mods via EvalMod(...) ?? 0
More(cfg, ...names)        per-name: modResult = Π(1+v/100); then
                             result *= Round(modResult, 2)      -- 2dp rounding PER NAME
                           unless data.highPrecisionMods[name][type] gives precision p:
                             result = Floor(result*modResult*10^p)/10^p
Flag(cfg, ...names)        first FLAG mod whose EvalMod is truthy → true; else parent
Override(cfg, ...names)    first OVERRIDE mod with non-null EvalMod → its value
List(cfg, ...names)        append every LIST mod's evaluated value
Tabulate(type, cfg, ...)   {value, mod} pairs, dropping value==0 unless OVERRIDE
Max/Min(cfg, ...)          reduce Tabulate("MAX"/"MIN")
HasMod(type, cfg, ...)     existence only, no evaluation
```

**Match predicate, identical in all six methods:**
```
mod.Type == queryType
&& (cfgFlags & mod.Flags) == mod.Flags
&& MatchKeywordFlags(cfgKeywordFlags, mod.KeywordFlags)
&& (source == null || mod.SourcePrefix == source)
```

**Parent chain — this is delegation, not inheritance.** `ModStore:ModStore(parent)` (`ModStore.lua:40-46`) sets `parent` and **shares `actor` with it**. Every `*Internal` recurses and combines:
- `Sum`: `result + parent.SumInternal(...)`
- `More`: `result * parent.MoreInternal(...)`
- `Flag`/`Override`: parent's result only if self has none (self wins)
- `List`/`Tabulate`: append parent's
- `GetCondition`: `self.conditions[var] || parent.GetCondition(var, cfg, noMod:true) || self.Flag(cfg, "Condition:"+var)` (`:294`)
- `GetMultiplier`: `Override(...) ?? (self.multipliers[var] + parent.GetMultiplier(var, cfg, noMod:true)) + Sum("BASE", cfg, "Multiplier:"+var)` (`:302`)

Chains in practice: `activeSkill.skillModList (ModList) -> env.modDB (ModDb) -> cachedPlayerDB (ModDb)`.

## Gotchas
- **`context` is threaded separately from `this`.** `SumInternal(context, ...)` is called with `context` = the *originating* store, so `EvalMod` and all tag lookups resolve against the **leaf** store, not the parent being scanned. Getting this wrong is subtle and pervasive.
- **The `noMod:true` flag on parent recursion is load-bearing.** The parent contributes only its literal `multipliers`/`conditions` tables, not its mod-derived contribution, because the child's `Sum`/`Flag` already walks the parent chain. **Double-counting here is the classic port bug.**
- `Round(modResult, 2)` in `More` is per-name and changes results. Use `LuaCompat.Round` (ticket 02), never `Math.Round`.
- `nil` vs `false` vs `0`: `Override` returns null for "no override", `EvalMod` returns null to veto. Model as `double?` / `ModValue?`. Beware `Sum`'s `or 0` coalescing at `ModDB.lua:149`.
- `ConvertModInternal` (`ModDB.lua:93`) does `t_remove` while iterating. Port carefully.

## Performance
- Pass names as `ReadOnlySpan<ModName>` (replaces Lua varargs + `select('#',...)`) — zero allocation, `params` overloads for the 1/2/3-name common cases.
- `damageStatsForTypes` (`CalcOffence.lua:53`) memoizes name arrays per type-flag mask. Port as a precomputed `ModName[][]` indexed by the 5-bit mask.
- **Best single optimisation in the port:** give `ModList` the same `Dictionary<ModName, List<Mod>>` index as `ModDb`. It is currently a full linear scan per name, and offence queries pass 2–6 names. Behaviour-preserving as long as per-name grouping order stays stable for `Override`/`Flag` first-wins — which the current name-major loop already imposes.
- `CollectionsMarshal.GetValueRefOrNullRef` for the bucket lookup, the hottest single line in the engine.

## Acceptance
Ticket 06 dump diffs clean. BenchmarkDotNet baseline established.

## Libraries
`CommunityToolkit.HighPerformance`, `System.Buffers.ArrayPool<T>`.
