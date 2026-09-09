# modstore-dump

Dumps the mod stores the calc engine builds for every test build in `spec/TestBuilds/3.13/`
(migration ticket 06), so that *setup* bugs — item, tree and skill mod construction — can be
separated from *math* bugs before any DPS number is compared.

```
cd src && luajit ../dotnet/tools/modstore-dump/dump.lua [outputDir]
```

Default output is `dotnet/Pob.Tests/oracles/modstore/`, which is committed and replayed by
`Pob.Tests/ModStoreDumpTests.cs`. Regenerate and commit the result whenever the engine's mod
construction changes.

The working directory must be `src/`: `HeadlessWrapper.lua` `dofile()`s its siblings by
relative path. Nothing under `src/` is modified — the wrapper is loaded as-is and the stores
are read out of `build.calcsTab.mainEnv` afterwards.

## What is dumped

Per build, one JSON file, holding these stores:

| store | path | class |
|---|---|---|
| `modDB` | `env.modDB` | ModDB |
| `enemyDB` | `env.enemyDB` | ModDB |
| `itemModDB` | `env.itemModDB` | ModDB |
| `skillModList` | `env.player.mainSkill.skillModList` | ModList |
| `minionModDB` | `env.minion.modDB` | ModDB, **when present** |

`env.minion` is `env.player.mainSkill.minion` (`src/Modules/CalcPerform.lua:1316`), so it exists
only when the *main* skill summons. **No 3.13 test build has one** — four of the five carry a
golem in their skill list, but none as the main skill — so the minion branch is currently
unexercised. `ModStoreDumpTests.MinionStore_IsAbsentFromEveryCurrentTestBuild` records that as a
known gap; adding a minion build to `spec/TestBuilds/` would close it.

## Schema

```jsonc
{
  "format": 1,
  "build": "OccVortex",
  "buildFile": "spec/TestBuilds/3.13/OccVortex.xml",
  "mainSkill": "Vortex",
  "usedModCache": true,          // false when CI=1 suppressed src/Data/ModCache.lua
  "storeCount": 4,
  "stores": [
    {
      "name": "modDB", "path": "env.modDB", "class": "ModDB",
      "order": "canonical", "hasParent": false,
      "modCount": 613, "conditionCount": 49, "multiplierCount": 106,
      "conditions": ["Effective", "..."],                 // truthy entries, sorted
      "multipliers": [{ "name": "PowerCharge", "value": 3 }],
      "mods": [ /* see below */ ]
    }
  ]
}
```

A mod:

```jsonc
{
  "name": "EnemyModifier",
  "type": "LIST",
  "value": { "mod": { /* a whole nested mod, same schema */ } },
  "flags": 0, "flagNames": "-",
  "keywordFlags": 0, "keywordFlagNames": "-",
  "source": "Tree:47630",                                  // null when the mod carried none
  "tags": [ { "type": "ActorCondition", "actor": "enemy", "var": "Chilled" } ],
  "extra": { "sourceSlot": "Helmet" },                     // present only when non-empty
  "print": "{mod=[-10 = Damage|INC|-|-|-]} = LIST|-|-|type=ActorCondition/actor=enemy/var=Chilled|Tree:47630"
}
```

* **`source` is deliberately three-valued.** A string, the empty string, or `null`. They are
  different things: `ModStore`'s source filtering matches `mod.source:match("[^:]+")`, and
  `modLib.createMod` (`src/Modules/ModTools.lua:38-49`) only assigns `source` when its fourth
  positional argument happens to be a *string* — a number there lands in `flags` instead. A mod
  that lost its source is invisible to every source-filtered query, which is exactly the class
  of bug this dump exists to catch. The empty string is real too: `CalcSetup.lua:424` passes
  `modSource or groupCfg.slotName or ""`.
* **`tags` is the mod's array part.** Tags decide whether a mod applies at all
  (`ModStore:EvalMod`, `src/Classes/ModStore.lua:363-971`); a dump without them cannot catch
  the most common setup bug. Tag parameter values may themselves be lists (`varList`) or
  nested tables.
* **`value` nests.** It may be a number, a boolean, a string, a record (`{ key=…, value=… }`),
  a whole modifier under `mod`, a list, or a function. Nested modifiers are emitted with the
  full mod schema — including their own `source`, which `modLib.setSource` writes through to
  (`ModTools.lua:238-244`).
* **Functions** are radius-jewel closures (`value.func`, mod type `JewelFunc`). They are
  written as `{"__lua":"function","def":"Modules/ModParser.lua:6555"}` — the definition site
  from `debug.getinfo`, because the address changes every run. Porting the bodies is ticket
  04's behaviour-key contract; all this dump can pin is that the mod still points at the same
  definition.
* **`print`** is the line `ModDB:Print()` would emit (`src/Classes/ModDB.lua:336-370`),
  produced by calling `modLib.formatValue` / `formatFlags` / `formatTags` themselves rather
  than by restating them, so the golden line and the engine's own debug output cannot drift.
  `ModStoreTextFormat` on the C# side rebuilds this string from the parsed structure and the
  test suite asserts the two agree.

## Determinism

Lua's `pairs()` order is undefined, and running the generator twice over the same build really
does produce a different array order — `-17 MORE Damage … Skill:Enfeeble` lands at different
indices of `enemyDB`'s `Damage` bucket, because the code that fills the stores walks item
slots, skills and buff sources with `pairs()`. So:

* mods are sorted by their **own canonical encoding**, which begins with the name, so the file
  still reads as `ModDB:Print()`'s name-grouped listing. Sorting on the full encoding rather
  than just the name makes the order total: two mods that compare equal are byte-identical, so
  which one wins a tie cannot change the output;
* **store array order is not preserved.** Pinning it would pin hash-table iteration order into
  a golden file;
* every JSON object emitted from a Lua hash table has its keys sorted; conditions and
  multipliers are sorted by name;
* numbers are written in the shortest spelling that round-trips, and the round-trip is
  asserted before the value is emitted. **Negative zero is written as `-0`**: the engine really
  does store it (an inactive Chill contributes `-0 INC ActionSpeed`), Lua prints it as `-0`,
  and `"%d"` would silently drop the sign;
* functions are written as their definition site, never as `function: 0x…`.

Two consecutive runs produce byte-identical files. That is an acceptance criterion of the
ticket, and it is easy to break: any new field read out of a Lua table with `pairs()` has to be
sorted on the way out.

## Interpreter

**LuaJIT only.** Stock `lua5.1` cannot load Path of Building at all, for three independent
reasons — the tree uses `goto`/labels in 39 places outside `TreeData` (`src/Modules/Data.lua:228`
is the first one reached), which is Lua 5.2 syntax that LuaJIT 2.1 implements and stock 5.1
does not parse; `src/Launch.lua:18` calls `jit.opt.start()` unconditionally; and `bit.*`
(`src/Modules/Common.lua:19`) is a LuaJIT builtin. The script asserts on `jit` up front rather
than failing halfway through loading the data set. LuaJIT is also what PoB ships
(`runtime/lua51.dll`), so the corpus is generated by the same interpreter the reference
implementation runs on.

## ModCache

`HeadlessWrapper.lua:41` wires `__mainObject__.continuousIntegrationMode` to the `CI`
environment variable, which suppresses `src/Data/ModCache.lua` and forces every mod line to be
parsed live. The two paths are supposed to agree, but they are different code, so which one
produced a dump is recorded in `usedModCache` rather than left to chance. Generate the
committed corpus with `CI` unset.
