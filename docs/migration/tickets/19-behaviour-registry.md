# 19 — Behaviour registry implementation

**Phase** 2 · **Depends on** 04, 18 · **Blocks** 22, 24

## Goal
Write the ~120–140 methods behind the behaviour keys defined in ticket 04.

## Scope
| Category | Count | Notes |
|---|---:|---|
| `preDamageFunc` | 122 sites → **95 distinct bodies** (59 is the count of distinct *opening lines* only), 7 byte-identical brand-frequency copies | Real logic: mutates `skillData.hitTimeOverride`, `dpsMultiplier`, branches on `skillPart`. **The irreducible core.** |
| `ModMap.apply` | 41 | Data-driven, but the schema needs `target` (player vs enemy), `extraArgs` and `type` — see #5. One body (`of Exposure`) does not conform. |
| `initialFunc` | 4 | Pre-pass skill setup |
| `preSkillTypeFunc` | 3 | `act_int.lua:1470,1647` |
| `postCritFunc` | 3 | |
| misc | 3 | `isKeystoneNative`, `explosiveArrowFunc`, `abbreviateModId` |
| legacy string rewriting | ~15 | `Uniques/Special/Generated.lua` — **run at transcode time (ticket 03), zero runtime code** |

**103 distinct keys** (see `dotnet/Pob.Data/behaviour-keys.json`), most methods 1–5 lines. Note the calc seam interfaces are deliberately empty until tickets 21–24 define them — behaviour bodies cannot be written against empty seams.

## Acceptance
- Build-time completeness check from ticket 04 passes: no emitted key lacks a registry entry.
- Golden builds exercising these skills (ticket 07) pass.
