# 22 — CalcActiveSkill

**Phase** 3 · **Depends on** 19, 21 · **Blocks** 23–26

## Goal
Port `src/Modules/CalcActiveSkill.lua` (934 LOC) — `calcs.createActiveSkill` (`:83`), `calcs.buildActiveSkillModList` (`:229`).

## Scope
Builds the `ActiveSkill` object: `{ skillModList, skillCfg, weapon1Cfg, weapon2Cfg, skillFlags, skillTypes, skillData, minion, effectList, buffList }`.

Also `src/Modules/CalcTools.lua` (303): `calcLib.mod` = `(1 + Sum(INC)/100) * More` (`:16`), skill-type expression evaluation, gem helpers.

## Gotchas
- `skillTypes` is string-keyed dispatch in Lua. Port as `HashSet<SkillType>` or a bitset — it is queried inside `EvalMod`'s `SkillType` tag, which is hot.
- `skillCfg` is the `ModCfg` passed to nearly every query downstream. Its construction determines which mods apply to what; errors here manifest as wrong numbers far away in `CalcOffence`.
- Hook the `preSkillTypeFunc` and `initialFunc` behaviour keys (ticket 19) here.

## Acceptance
Golden builds' `activeSkill.skillModList` dumps match (ticket 06 extended).
