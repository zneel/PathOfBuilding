# 11 — EvalMod: the tag evaluator

**Phase** 2 · **Depends on** 09, 10 · **Blocks** 21–26

## Goal
Port `ModStore:EvalMod` — 600 lines that decide whether a mod applies and at what scale. Every conditional mechanic in the game routes through here.

## Scope
Reference: `src/Classes/ModStore.lua:363-971`.

Walks the tag array in order. Each tag either **scales `value`** or **vetoes the mod by returning null**. 21 tag types:

**Scaling:** `Multiplier` (var/varList, div/divVar, base, limit/limitVar/limitStat, limitTotal/limitNegTotal, invert, noFloor, actor, limitActor), `PerStat`, `PercentStat`, `Limit`, `DistanceRamp`, `MeleeProximity`.

**Gating:** `MultiplierThreshold`, `StatThreshold`, `Condition`, `ActorCondition`, `ItemCondition`, `SocketedIn`, `SkillName`, `SkillId`, `SkillPart`, `SkillType`, `BaseFlag`, `SlotName`, `ModFlagOr`, `KeywordFlagAnd`, `MonsterTag`.

**Cross-cutting:**
- `tag.actor` redirects the lookup to another actor's `modDB` via `getActor` (`ModStore.lua:48-54`) — `"parent"`, `"enemy"`, `"player"` with fallback chains. **If the actor doesn't exist, the mod is vetoed.**
- `tag.neg` inverts gating tags.
- `globalLimit`/`globalLimitKey` (`:960-969`) applies a **cross-mod running cap**, mutating a `globalLimits` dictionary allocated per-query in `SumInternal`/`MoreInternal`/`TabulateInternal`. **Order-dependent.**

## Gotchas
- ⚠️ **`tag.div = GetMultiplier(...)` at `:401` and `:499` mutates the tag table in place** — a shared, cached data structure. Do not port as-is; compute into a local. Verify against golden output that nothing depends on the mutation persisting (it shouldn't — it's recomputed each call).
- `m_floor(base/div + 0.0001)` in the `Multiplier` tag (`:403`) — use `LuaCompat.Floor`, keep the `+ 0.0001`.
- `EvalMod` allocates `copyTable(value)` for every tagged mod with a table value (`:423, 512, 562`) and a `globalLimits` dict per query that hits a tagged mod. **This is the port's biggest allocation risk** — profile it.
- Implement as a `switch` expression over the sealed tag record hierarchy from ticket 09. Exhaustiveness checking catches missing tag types at compile time.

## Acceptance
Ticket 06 dump diffs clean, ticket 07 goldens pass for builds exercising conditional mechanics.

## Libraries
PerfView / dotnet-trace for allocation profiling.
