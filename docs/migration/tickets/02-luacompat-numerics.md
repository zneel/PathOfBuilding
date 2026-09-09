# 02 — LuaCompat numeric layer

**Phase** 0 · **Depends on** 01 · **Blocks** 10, 11, 21–26

## Goal
Bit-exact reproduction of Lua's arithmetic and rounding. This is the single largest source of silent golden-test drift in the whole port.

## Scope
`Pob.Core/LuaCompat.cs`. Implement and test against the Lua originals:

- Everything is `double`. **Never `decimal`.** Lua has one number type.
- **`src/Modules/Common.lua` defines eight helpers, not three** — `round` (709), `floor` (722), `roundSymmetric` (733), `alwaysPositiveRound` (753), `floorSymmetric` (766), `ceilSymmetric` (778), `ceil_b` (1013), `floor_b` (1019). Port all of them plus `math.floor`/`ceil`/`modf`.
- `round` is **half-up toward positive infinity** (`floor(val*10^dec + 0.5)/10^dec`), so `-2.5` → `-2`. .NET's `Math.Round` is banker's rounding. Get this wrong and every DPS number drifts in the 4th decimal.
- Common.lua's global `floor(val, dec)` carries a **`+0.0001` epsilon**; `math.floor` does not. They are different functions and both are used. The global does not shadow `math.floor`, which is a table field.
- **`dec = 0` is not the same call as omitting `dec`** — Lua treats `0` as truthy, so `floor(v, 0)` still adds the epsilon. Model as separate overloads.
- **`floorSymmetric(val)` with no `dec` returns two values** (`select(1, math.modf(val))`), and `alwaysPositiveRound` tail-calls it and inherits that. All real call sites assign to one variable, but pin the raw form in the corpus.

Sites that must be replicated literally:
| Site | Expression |
|---|---|
| `src/Classes/ModDB.lua:197`, `ModList.lua:147` | `round(modResult, 2)` applied **per name** inside `More` |
| `src/Classes/ModStore.lua:403` | `m_floor(base/(tag.div or 1) + 0.0001)` in the `Multiplier` tag — open-coded against the file-local `m_floor` alias (`ModStore.lua:10`), **not** a call to the global `floor`. The `tag.noFloor` branch on the next line is a bare division. |
| `src/Classes/ModStore.lua:77-83` | `m_modf(round(v*scale, 2))` in `ScaleAddMod` |
| `src/Classes/ModDB.lua:194-195`, `ModList.lua:144-145` | `data.highPrecisionMods` precision override: **raw `math.floor(result*modResult*power)/power`** — note this is `math.floor`, NOT Common.lua's global `floor`, so there is **no `+0.0001` epsilon** here |

## Gotchas
The `round(..., 2)` in `More` is **not cosmetic** — it changes results. It is applied once per mod name, not once per query.

## Acceptance
- A `LuaCompat` test suite that diffs against values dumped from the Lua runtime for a few thousand random inputs across the identified call shapes.
- A Roslyn analyzer (or an `.editorconfig` banned-API rule via `Microsoft.CodeAnalysis.BannedApiAnalyzers`) forbidding raw `Math.Round` / `Math.Floor` inside `Pob.Core`, `Pob.Calc`, `Pob.Parsing`.

## Libraries
`Microsoft.CodeAnalysis.BannedApiAnalyzers`.
