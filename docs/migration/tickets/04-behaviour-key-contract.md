# 04 — Behaviour-key contract

**Phase** 0 · **Depends on** 01 · **Blocks** 03, 19

## Goal
Define the indirection that lets generated data reference hand-written code, and enumerate every behaviour that needs one. **Do this before the transcoder** — the keys are the schema contract between generated data and hand-written code, and discovering them late means re-emitting everything.

## Scope
`src/Data/` contains 230 `function(` occurrences. 35 are file-level DI wrappers (not behaviour). **~191 real function values:**

| Key | Count | What it does | Where |
|---|---:|---|---|
| `preDamageFunc` | 122 | Mutates `skillData` before damage calc — hit-time overrides, DPS multipliers, skill-part branching | `Skills/act_int.lua` (69), `act_dex.lua` (42), `act_str.lua` (16) |
| `apply` | 41 | Map mod → `enemyModList:NewMod(...)` | `ModMap.lua` |
| `initialFunc` | 4 | Pre-pass skill setup | `Skills/` |
| `preSkillTypeFunc` | 3 | Adjusts skill types before resolution | `act_int.lua:1470,1647` |
| `postCritFunc` | 3 | Post-crit adjustment | `Skills/` |
| misc one-offs | 3 | `isKeystoneNative`, `explosiveArrowFunc`, `abbreviateModId` | `Minions.lua`, `Spectres.lua`, `SkillStatMap.lua` |
| legacy string rewriting | ~15 | gsub callbacks, sort comparators | `Uniques/Special/Generated.lua:223-334,472,698,766` |

**Deduplication matters:** the 122 `preDamageFunc` bodies reduce to **59 distinct opening lines**, with 7 byte-identical copies of the brand-frequency formula (`act_int.lua:553,716,896,…`). Realistic hand-written surface is **~120–140 small methods, ~2–3k LOC of C#** — a week or two, not a rewrite.

## Design
1. Transcoder emits a **string key** where Lua had a closure: `"preDamageFunc": "BrandActivationFrequency"`.
2. A hand-written static registry resolves it to a delegate.
3. Keys resolve **once at load** into a delegate field on the record — never per call.

## Deliverable for this ticket
- The full enumerated key list, derived by script from the Lua source, committed as `dotnet/Pob.Data/behaviour-keys.json`.
- The registry interface and resolution mechanism.
- **A build-time completeness check: emitting a behaviour key with no registry entry fails the build.** This turns each league's data update into a compile error instead of a silent runtime null.

Implementation of the method bodies is ticket 19.

## Gotchas
`ModMap.apply` (41 entries) is nearly declarative — every one is a sequence of `enemyModList:NewMod(name, type, value, source)` calls (`ModMap.lua:12-41`). Make these fully data-driven with a `{name, type, valueExpr, source}` schema and skip code entirely. That eliminates a third of the registry.
