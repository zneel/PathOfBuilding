# behaviour-keys

Enumerates every function value in `src/Data/**.lua` and writes the behaviour-key contract to
`dotnet/Pob.Data/behaviour-keys.json`.

The Lua data tables store closures — `preDamageFunc = function(activeSkill, output) ... end`. The
C# port cannot ship closures inside generated data, so the transcoder (ticket 03) emits a **string
key** where Lua had a closure and a hand-written registry resolves it to a delegate:

```
Lua      preDamageFunc = function(activeSkill, output) ... end
data     "preDamageFunc": "BrandActivationFrequency"
C#       BehaviourRoster.cs -> BehaviourRegistry -> GrantedEffectBehaviours.PreDamage
```

This tool produces the list of keys that contract is made of.

## Running it

```sh
lua5.1 dotnet/tools/behaviour-keys/enumerate.lua            # rewrite behaviour-keys.json
lua5.1 dotnet/tools/behaviour-keys/enumerate.lua --check    # exit 1 if the file is out of date
lua5.1 dotnet/tools/behaviour-keys/enumerate.lua --report   # human summary, writes nothing
```

Runs under `lua5.1` and `luajit` with no dependencies; all three produce byte-identical output.
There are no timestamps, no absolute paths and no hash-table iteration in the output, so a rerun on
unchanged data is a zero-line diff.

## What to do after a league data drop

1. Re-run the generator. Any new behaviour appears as a new `key` entry in the diff.
2. `dotnet build` now fails, naming the keys that have no registry entry
   (`VerifyBehaviourKeyRoster` in `Pob.Data/Pob.Data.csproj`).
3. Triage each one by hand in `Pob.Data/Behaviours/BehaviourRoster.cs`:
   - `bodyHash` equal to an existing behaviour means GGG copied a body onto another skill — point the
     new key at the same implementation;
   - otherwise it is new work: add `Pending("Key", BehaviourHook.<hook>)` and implement it.
4. A key that vanished fails the build from the other side: delete the stale roster entry.

That compile error is the whole point of the ticket. Without it, a behaviour arriving in new data
would resolve to nothing at runtime and quietly produce a wrong number.

## How a behaviour is told apart from everything else

`src/Data` contains 244 `function` values and only 133 of them are behaviour. The distinction is
made from the syntax, never from a file list:

| Category | Count | What it is |
|---|---:|---|
| `data-field` | 182 | A function stored in a data table. Behaviour, unless the hook is declarative or the file is transcode-time. |
| `di-wrapper` | 35 | The chunk itself returns the function: `return function(itemBases)`. Dependency injection, not behaviour. |
| `nested-helper` | 7 | Declared inside another function body (`local function hitChance` inside a `preDamageFunc`). Ported with its parent. |
| `named-declaration` | 8 | `function updateColorCode(...)` in `Global.lua` and friends. Ordinary code. |
| `call-argument` | 6 | Passed straight to `gsub` or `table.sort`. Never reaches a data table. |
| `chunk-wrapper` | 5 | `(function() ... end)()` in `ModCache.lua`, splitting a generated file into chunks. |
| `local-value` | 1 | A file-local closure. Ordinary code. |

Of the 182 data-field functions, 41 are `ModMap` `apply` bodies that become data rather than code,
and 8 are `legacyMod` bodies in `Uniques/Special/Generated.lua`, which ticket 03 executes once at
transcode time. That leaves **133 behaviour sites, which deduplicate to 103 keys**.

## Files

| File | Role |
|---|---|
| `enumerate.lua` | Entry point: walks the tree, classifies, groups, names, emits. |
| `lexer.lua` | Lua 5.1 tokeniser. Comments and strings are skipped properly, so the word "function" inside flavour text is not a function. |
| `scan.lua` | Token walk: table paths, binding context, block matching, function extents. |
| `hash.lua` | FNV-1a 64 in pure Lua arithmetic, for grouping identical bodies. |
| `names.lua` | The handful of semantic key names, keyed by body hash. |

Every extracted extent is fed back through `loadstring` before anything is emitted: if the block
matcher ever mis-pairs a `function` with an `end`, the run fails instead of producing a
plausible-looking key list.

## Key names

A key is `<Owner><Hook>` — the lexicographically first skill id in the duplicate group plus the hook
stem, for example `WinterOrbPreDamage`. Names in `names.lua` override that where the body says
plainly what it does (`BrandActivationFrequency`, `HitTimeOverrideFromCooldown`); 9 of the 103 keys
are named that way, the rest are derived. `nameSource` in the JSON records which.

The derived name depends on the group's owners, so a new skill that shares an existing body and
sorts ahead of its current owner would rename the key. The manifest lists every site of a group, so
such a rename is visible in the diff as a paired add and remove rather than a mystery. Any key can
be pinned by adding it to `names.lua`.
