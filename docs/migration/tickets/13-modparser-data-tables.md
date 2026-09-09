# 13 — ModParser: data tables and the scan algorithm

**Phase** 2 · **Depends on** 05, 09, 12 · **Blocks** 14, 15

## Goal
Port the declarative ~85% of `src/Modules/ModParser.lua` (7,021 LOC) and the matching algorithm.

## Scope
| Table | Lines | Entries | Role |
|---|---|---:|---|
| `formList` | `:72-159` | ~88 | Detects the form (INC/RED/MORE/BASE/CHANCE/DMG/REGEN…), captures numbers |
| `modNameList` | `:161-912` | 724 | English stat phrase → mod name (string, or array for multi-target like `"attributes"` → `{Str,Dex,Int,All}`) |
| `modFlagList` | `:913-1101` | 181 | Trailing phrases → `ModFlag`/`KeywordFlag` |
| `preFlagList` | `:1102-1319` | 190 | Leading phrases → flags/tags |
| `modTagList` | `:1320-2129` | 672 | Conditional/multiplier phrases → tag objects (some closures → ticket 14) |
| `specialModList` | `:2130-5955` | 2,099 | Whole-line special cases (many closures → ticket 14) |
| `unsupportedModList` | `:5956-6150` | 100 | Known-unsupported lines, silently ignored |

Plus `jewelOtherFuncs`, `clusterJewelSkills`, `skillNameList`, `penTypes`, `costTypes`, `suffixTypes`, `dmgTypes`, `regenTypes`, `flagTypes`.

**Export the pure-data tables to JSON from the Lua side** rather than hand-transcribing 4,000 entries. Load at startup, or bake with a source generator.

**The `scan` algorithm** (`:6616-6638`): iterate **every** pattern, keep the match with the *earliest start*, then *longest end*, then *longest pattern string*; return the line remainder with the match excised.

**`parseMod` pipeline** (`:6640`): specials → preFlag → skill name → form → tag → tag2 → name → flags → suffix. Runs twice if needed (`order=1`, then `order=2` swaps the skill-name scan position). Post-processes `misc.addToAura` / `newAura` / `addToMinion` / `addToSkill` / `applyToEnemy` into wrapper `LIST` mods.

## Gotchas
- **Reproduce `scan` exactly.** Lua's `pairs()` order is undefined, so ties beyond earliest/longest/longest-pattern are arbitrary in Lua. Verify against the ModCache corpus that no line depends on a tie.
- Performance: 4,000 patterns × earliest-longest scan per line is O(n·m). Precompile each pattern once, or build an Aho-Corasick prefilter on the literal substrings — most patterns have a long literal core.
- **Consider a Roslyn incremental source generator**: read the exported pattern JSON as `AdditionalFiles`, emit a `[GeneratedRegex]` field per pattern plus a switch-based dispatcher. Startup-free, allocation-free, and the data file stays the single source of truth.

## Acceptance
Measured against ticket 05's oracle. Target for this ticket alone (without ticket 14's closures): the declarative subset of the 23,215 corpus lines.

## Libraries
Roslyn incremental source generators, `[GeneratedRegex]`, `System.Text.Json` source-gen.
