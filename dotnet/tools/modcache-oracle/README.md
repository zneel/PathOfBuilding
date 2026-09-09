# modcache-oracle

Turns `src/Data/ModCache.lua` into the answer key for the C# `ModParser` port (migration
tickets 12–14), and gives that port a daily completeness number.

```
luajit dotnet/tools/modcache-oracle/dump.lua [outputPath]
```

Default output is `dotnet/Pob.Tests/oracles/modcache.json`, which is committed and replayed by
`Pob.Tests/ModCacheOracleTests.cs`.

LuaJIT specifically, not stock `lua5.1`: `src/Modules/ModTools.lua:15` binds `bit.band`, and the
bit library is a LuaJIT extension. LuaJIT is also what PoB ships (`runtime/lua51.dll`).

## What the corpus is

`src/Data/ModCache.lua` is written by `main:SaveModCache` (`src/Modules/Main.lua:315-348`) and
read back by `main:Init` (`Main.lua:140-142`) into `modLib.parseModCache`. Each statement is

```lua
c[<mod line>] = { <parsed mod array or nil>, <unparsed remainder or nil> }
```

and the pair is exactly what `modLib.parseMod` returns (`src/Modules/ModParser.lua:6992`):

```lua
return modList, line:match("%S") and line
```

`line` at that point is the input with every fragment the parser consumed blanked out, so the
second element is `nil` **iff PoB consumed the whole line**. That is the definition of "parsed
correctly" this oracle scores against.

Regenerate the cache itself, not just this JSON, with `REGENERATE_MOD_CACHE=1`
(`src/Modules/Main.lua:136`) — that makes PoB rebuild `ModCache.lua` from current data. Only do
that deliberately: it changes the answer key, and the score becomes incomparable with yesterday's.

## Numbers, as of the committed corpus

| | |
|---|---:|
| `src/Data/ModCache.lua` raw lines | 23,215 |
| framing lines (`local c = {}`, 5 IIFE open/close, `return c`) | 8 |
| **mod lines (corpus entries)** | **23,207** |
| entries PoB consumes whole (`remainder == nil`) | 19,313 |
| entries PoB leaves a remainder on | 3,894 |
| entries with at least one mod | 19,684 |
| entries with a present-but-empty mod list | 709 |
| entries with no mod list at all | 2,814 |
| of those, entries with a remainder / with neither | 2,793 / 21 |
| individual mods | 23,324 |
| distinct canonical mod strings | 21,121 |

The 3,894 remainder entries are lines **PoB itself does not fully parse**. They are still
useful — the mods PoB *did* extract from them and the exact leftover text are both pinned — but
they must not be counted against the C# port as failures. `luaFullyParsedCount` (19,313) is the
denominator for "the port is done"; `entryCount` (23,207) is the denominator for "the port
reproduces PoB, warts and all". `ModCacheOracleTests` reports both.

An entry can have mods *and* a remainder: `"(2-4)% chance to deal Double Damage"` yields two mods
and leaves `"(2-4)% chance to deal   "` behind. Partial parses are the normal case, not an error.

## The five IIFE chunks

`SaveModCache` emits the file as `local c = {}` followed by five `(function() ... end)();`
blocks of at most 5,001 statements each (`ModCache.lua:2, 5004, 10006, 15008, 20010`), then
`return c`. The split exists because `LoadModule`/`require` wrap a loaded file in *another*
function, and a single prototype holding 23k string constants blows LuaJIT's per-prototype
constant limit.

`dump.lua` therefore uses `loadfile`, not `LoadModule` or `require`: the split is already inside
the file, so a plain chunk load handles it with no reassembly. It asserts the chunk count is 5
so that a future `SaveModCache` change to the chunking is noticed rather than silently absorbed.

## Canonical form

Each mod is emitted as **one string**, produced by the real `modLib.formatMod`
(`src/Modules/ModTools.lua:231-233`), so the C# comparison is on text rather than on nested table
structure:

```
<value> = <name>|<type>|<modFlags>|<keywordFlags>|<tags>
```

- `<value>` is `modLib.formatValue`: `tostring` for scalars; for tables, `{k=v/k=v}` with keys
  sorted and `type` hoisted first, and a nested mod rendered as `mod=[<formatMod of it>]`.
- `<modFlags>` / `<keywordFlags>` are `modLib.formatFlags`: every name in `ModFlag` /
  `KeywordFlag` whose bits are *all* set, sorted, comma-joined, or `-` when none match. Note
  that this includes the composite masks (`SourceMask`, `WeaponMask`, …) when their full bit
  pattern is present — a C# port must mirror that, not just the primitive flags.
- `<tags>` is `modLib.formatTags`: `modLib.formatTag` of each tag, comma-joined, or `-`.

`modLib.formatSourceMod` (`ModTools.lua:235-237`) was **not** used: it interpolates `mod.source`,
and `SaveModCache` never writes a `source` field, so every entry in this corpus would carry a
literal `nil` in the middle of the string. `formatMod` is the same text without that dead field.

### What the canonical form does not capture — measured, not assumed

`formatMod` is a *rendering*, not a serialisation, and it has no inverse:
`modLib.parseFormattedSourceMod` (`ModTools.lua:98-116`) only reads back scalar values and a
single un-comma-joined flag name, so `format → parse` is lossy in general. Concretely:

1. **Value type is erased.** `formatValue` is `tostring`, so the number `5`, the string `"5"`
   and (for `true`) the boolean and the string `"true"` all render identically.
2. **Delimiters are not escaped.** A string value containing `|`, `/` or `,` would be
   indistinguishable from structure.
3. **`source` is dropped** — irrelevant here, as above.

So `dump.lua` measures the damage instead of assuming it away. It computes, alongside each
canonical string, a fully faithful structural signature of the mod table (sorted keys, typed
scalars, `%.17g` numbers) and checks whether one canonical string ever stands for two different
tables. Over all 23,324 mods it does so **twice**, and both are the same phenomenon:

```
"Hits have {15,50}% chance to ignore Enemy Physical Damage Reduction while you have Sacrificial Zeal"
  -> 15 = ChanceToIgnoreEnemyPhysicalDamageReduction|BASE|-|-|-
```

Those two mod tables carry a `Condition/var=SacrificialZeal` tag at array index **2** with index
1 nil. `modLib.createMod` builds the tag array with `select(tagStart, ...)` inside a table
constructor (`ModTools.lua:56-64`), so a nil in the middle leaves a hole. `formatTags` walks the
tags with `ipairs`, which stops at the hole — and so does `ModStore:EvalMod`
(`src/Classes/ModStore.lua:368`), which is the only thing that ever reads them. **The tag is
dead in the engine, not lost by the formatter.** The canonical form is therefore faithful to what
PoB actually evaluates, which is the property the oracle needs; it is simply not a lossless dump
of the raw table, and it does not claim to be.

Zero type-ambiguity collisions (limitation 1) and zero delimiter collisions (limitation 2) occur
on this corpus. That is an empirical fact about these 23,324 mods, re-checked on every
regeneration: the count and the offending pairs are written into the corpus header as
`ambiguousCanonicalCount` / `ambiguousCanonical`, and `ModCacheOracleTests` asserts the count and
that every one of them is a shadowed-tag case. If a future cache introduces a *real* ambiguity,
that test fails and the canonical form has to be extended rather than quietly weakened.

## Determinism

- `os.setlocale("C")`, and string sorting goes through an explicit byte comparator rather than
  Lua's `<`, which is `strcoll`-backed and therefore locale-dependent.
- `entries` is pre-sorted by line; `ambiguousCanonical` by canonical string then line.
- `dkjson.encode` is called with no state table, so it sorts object keys itself
  (`runtime/lua/dkjson.lua:355-364`).
- No floating-point values reach the JSON: mod values are already baked into the canonical
  strings by Lua's own `tostring`, and the header carries only integers.

Two consecutive runs, and runs under different locales, are byte-identical
(`sha256 c75417ea…202ee`, 2,984,600 bytes).

## Corpus format

```json
{ "format": 1,
  "source": "src/Data/ModCache.lua",
  "canonicalForm": "modLib.formatMod",
  "sourceLineCount": 23215, "sourceChunkCount": 5,
  "entryCount": 23207, "modCount": 23324, "distinctModCount": 21121,
  "luaFullyParsedCount": 19313, "luaRemainderCount": 3894,
  "luaWithModsCount": 19684, "luaEmptyModListCount": 709, "luaNilModListCount": 2814,
  "luaNilModsWithRemainderCount": 2793, "luaNilModsNoRemainderCount": 21,
  "shadowedTagCount": 3,
  "ambiguousCanonicalCount": 2, "ambiguousCanonical": [ ... ],
  "entries": [ ["<mod line>", ["<canonical mod>", ...] | null, "<remainder>" | null], ... ] }
```

Each entry is `[line, mods, remainder]`, mirroring the `[function, args, results]` triple shape
of `oracles/luacompat.json`. `mods` is `null` where the cache stored `nil` and `[]` where it
stored an empty table — a distinction `SaveModCache` preserves and the port must too.

## Why the JSON is committed

2.9 MB, against the 3.9 MB Lua source it is derived from and the 4.5 MB
`oracles/luacompat.json` already committed next to it. Generating it in CI instead would mean a
LuaJIT toolchain on every test runner to reproduce a file that changes only when
`src/Data/ModCache.lua` or `modLib.formatMod` changes — and when it *does* change, the diff is
the single most interesting review artifact in the parser port, because it is the answer key
moving. A compact non-JSON encoding was rejected for the same reason: the score has to be
auditable line by line by a human reading a diff.

It is written minified (no indentation) and read with `System.Text.Json`; pretty-printing it
would roughly double the size to buy nothing a JSON-aware diff tool cannot already do.
