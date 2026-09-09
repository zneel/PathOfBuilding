# 04 — Behaviour-key contract

**Phase** 0 · **Depends on** 01 · **Blocks** 03, 19

## Goal
Define the indirection that lets generated data reference hand-written code, and enumerate every behaviour that needs one. **Do this before the transcoder** — the keys are the schema contract between generated data and hand-written code, and discovering them late means re-emitting everything.

## Scope
`src/Data/` contains 230 `function(` occurrences. 35 are file-level DI wrappers (not behaviour). **~191 real function values:**

| Key | Count | What it does | Where |
|---|---:|---|---|
| `preDamageFunc` | 122 | Mutates `skillData` before damage calc — hit-time overrides, DPS multipliers, skill-part branching | `Skills/act_int.lua` (~65), `act_dex.lua` (~39), `act_str.lua` (~13), plus `other.lua` (4), `minion` (2), `spectre` (1), `sup_str` (1) |
| `apply` | 41 | Map mod → `enemyModList:NewMod(...)` | `ModMap.lua` |
| `initialFunc` | 4 | Pre-pass skill setup | `Skills/` |
| `preSkillTypeFunc` | 3 | Adjusts skill types before resolution | `act_int.lua:1470,1647` |
| `postCritFunc` | 3 | Post-crit adjustment | `Skills/` |
| misc one-offs | **1** | `explosiveArrowFunc` at `Skills/act_dex.lua:6699`. `isKeystoneNative` (`Generated.lua:612`) and `abbreviateModId` (`Generated.lua:425`) are **locals in the transcode-time file**, not behaviours. `Minions.lua`, `Spectres.lua` and `SkillStatMap.lua` contain only their DI wrapper. |
| legacy string rewriting | 20 | 8 `legacyMod` fields, 3 gsub callbacks, 3 sort comparators, 4 named declarations, 2 locals | `Uniques/Special/Generated.lua` — all transcode-time, emit no keys |

**Deduplication matters, but measure it on bodies, not first lines.** The 122 `preDamageFunc` bodies share only 59 distinct *opening lines* — many begin `local skillData = activeSkill.skillData` and then diverge. Deduplicating on the **whole body** gives 95 distinct preDamage behaviours. Across all hooks: **133 behaviour sites → 103 distinct keys**, 19 of which cover more than one site. 7 sites are byte-identical copies of the brand-frequency formula (`act_int.lua:552,715,895,13901,14015,17373,17470`).

Realistic hand-written surface is therefore **103 methods**, not 120–140.

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
`ModMap.apply` (41 entries) is nearly declarative, but **`{name, type, valueExpr, source}` is not a sufficient schema.** Three more fields are required:

- **`target`** — a substantial minority of the `NewMod` calls go to `modList` (the player), not `enemyModList` (20 of 57 call sites). Omitting this **silently applies player debuffs to the monster.**
- **`extraArgs`** — 6 mods carry trailing tags: `ModFlag.Attack` (Impaling), `{type="Condition", var="RareOrUnique"}` (Overlord ×2, Titan ×2), two `SkillType` tags (of Doubt).
- **`type`** (check / list / count) — decides the argument list `apply` is called with (`ConfigOptions.lua:120-126`). 21 list, 12 count, 8 check.

One body does not conform: **`of Exposure`** (`ModMap.lua:239`) has an `if values[val][2] ~= 0` guard and a `local roll`. Five entries (`Mirrored`, `Punishing`, `of Balance`, `of Blinding`, `of Transience`) are empty — map mods PoB does not model.

The 55 value expressions reduce to **six shapes**, so the config layer needs a closed set of value kinds, not a Lua expression evaluator.
