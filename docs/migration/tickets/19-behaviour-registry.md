# 19 — Behaviour registry implementation

**Phase** 2 · **Depends on** 04, 18 · **Blocks** 22, 24

## Goal
Write the ~120–140 methods behind the behaviour keys defined in ticket 04.

## Scope
| Category | Count | Notes |
|---|---:|---|
| `preDamageFunc` | 122 bodies → **~59 distinct**, 7 byte-identical copies of the brand-frequency formula (`Skills/act_int.lua:553,716,896,…`) | Real logic: mutates `skillData.hitTimeOverride`, `dpsMultiplier`, branches on `skillPart`. **The irreducible core.** |
| `ModMap.apply` | 41 | Nearly declarative — sequences of `enemyModList:NewMod(name, type, value, source)` (`ModMap.lua:12-41`). **Make these data-driven and skip the code entirely.** Eliminates a third of the registry. |
| `initialFunc` | 4 | Pre-pass skill setup |
| `preSkillTypeFunc` | 3 | `act_int.lua:1470,1647` |
| `postCritFunc` | 3 | |
| misc | 3 | `isKeystoneNative`, `explosiveArrowFunc`, `abbreviateModId` |
| legacy string rewriting | ~15 | `Uniques/Special/Generated.lua` — **run at transcode time (ticket 03), zero runtime code** |

**Estimated ~2,000–3,000 LOC of C#**, most methods 1–5 lines.

## Acceptance
- Build-time completeness check from ticket 04 passes: no emitted key lacks a registry entry.
- Golden builds exercising these skills (ticket 07) pass.
