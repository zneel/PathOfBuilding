# 02 — LuaCompat numeric layer

**Phase** 0 · **Depends on** 01 · **Blocks** 10, 11, 21–26

## Goal
Bit-exact reproduction of Lua's arithmetic and rounding. This is the single largest source of silent golden-test drift in the whole port.

## Scope
`Pob.Core/LuaCompat.cs`. Implement and test against the Lua originals:

- `Round(double, int places)` — port `round` from `src/Modules/Common.lua`. **.NET's `Math.Round` defaults to banker's rounding; Lua's helper does not.** Get this wrong and every DPS number drifts in the 4th decimal.
- `Floor`, `Modf` — matching `math.floor` / `math.modf` semantics.
- Everything is `double`. **Never `decimal`.** Lua has one number type.

Sites that must be replicated literally:
| Site | Expression |
|---|---|
| `src/Classes/ModDB.lua:197`, `ModList.lua:147` | `round(modResult, 2)` applied **per name** inside `More` |
| `src/Classes/ModStore.lua:403` | `m_floor(base/div + 0.0001)` in the `Multiplier` tag |
| `src/Classes/ModStore.lua:77-83` | `m_modf(round(v*scale, 2))` in `ScaleAddMod` |
| `src/Modules/Data.lua:436` | `data.highPrecisionMods` — per-name/per-type precision override: `floor(result*modResult*10^p)/10^p` |

## Gotchas
The `round(..., 2)` in `More` is **not cosmetic** — it changes results. It is applied once per mod name, not once per query.

## Acceptance
- A `LuaCompat` test suite that diffs against values dumped from the Lua runtime for a few thousand random inputs across the identified call shapes.
- A Roslyn analyzer (or an `.editorconfig` banned-API rule via `Microsoft.CodeAnalysis.BannedApiAnalyzers`) forbidding raw `Math.Round` / `Math.Floor` inside `Pob.Core`, `Pob.Calc`, `Pob.Parsing`.

## Libraries
`Microsoft.CodeAnalysis.BannedApiAnalyzers`.
