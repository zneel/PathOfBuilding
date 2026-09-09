# luacompat-oracle

Dumps a bit-exact conformance corpus for `Pob.Core.LuaCompat` (migration ticket 02) out of a
real Lua interpreter.

```
luajit dotnet/tools/luacompat-oracle/dump.lua [outputPath]
```

Default output is `dotnet/Pob.Tests/oracles/luacompat.json`, which is committed and replayed by
`Pob.Tests/LuaCompatOracleTests.cs`. Regenerate it whenever the helpers in
`src/Modules/Common.lua` change, or whenever a new call-site shape needs pinning, and commit the
result together with the C# change.

## Why it sources Common.lua instead of restating it

`dump.lua` reads `src/Modules/Common.lua` at run time, extracts the `local m_xxx = math.xxx`
alias block and the verbatim text of the top-level `round`, `floor`, `roundSymmetric`,
`alwaysPositiveRound`, `floorSymmetric`, `ceilSymmetric`, `ceil_b` and `floor_b` function
bodies, and `loadstring`s them. Nothing is retyped, so an upstream edit to a helper flows into
the corpus on the next run, and an upstream rename or deletion makes the script fail loudly
rather than silently pinning a stale copy.

The whole module cannot simply be `dofile`d: `Common.lua:25-30` requires `lcurl.safe`, `xml`,
`base64`, `sha1` and `lua-utf8`, and line 42 touches the `launch` global, none of which exist
outside the PoB host.

The four call-site shapes the ticket names — `round(modResult, 2)` in `More`, the
`data.highPrecisionMods` precision override, `m_floor(base / div + 0.0001)` in the `Multiplier`
tag, and `m_modf(round(v * scale, 2))` in `ScaleAddMod` — are transcribed into `dump.lua` under
`site.*` names, because they are expressions inside methods rather than reusable functions and
there is nothing to source. Each carries the file and line it was copied from; check those
before trusting them.

## Corpus format

```json
{ "format": 1, "caseCount": 92345,
  "cases": [ ["round", ["2.5", "2"], ["2.5"]], ... ] }
```

Each case is `[functionName, [arguments], [results]]`. Arguments and results are **strings, not
JSON numbers**, so no JSON reader gets a chance to reinterpret a double; `null` as an argument
means the Lua parameter was absent (`dec == nil`), which is a genuinely different code path from
`dec == 0` because Lua treats `0` as truthy. `results` has more than one entry where the Lua
function returns more than one value.

Numbers are written in the shortest `%g` precision that round-trips, and the script asserts the
round-trip before emitting; `nan`, `inf`, `-inf` and `-0` have explicit spellings. .NET's parser
is correctly rounded, so the string maps back to the identical double.

## Interpreter agreement

The corpus is generated with `luajit`, because that is what PoB ships (`runtime/lua51.dll`).
Regenerating it with stock `lua5.1` produces **the same doubles for all 92,345 cases** — zero
semantic differences — but roughly 100 cases come out with a different decimal *spelling*: the
two printf implementations break the tie differently on the last significant digit (LuaJIT's own
`dtoa` rounds half away from zero, glibc half to even), so e.g. the double `999999999999999.625`
prints as `999999999999999.63` under LuaJIT and `999999999999999.62` under glibc. Both parse
back to the identical double, and the script's round-trip assertion covers exactly that. Do not
"fix" a diff of that shape by regenerating with the other interpreter; check the doubles.

`math.random` is deliberately not used — LuaJIT and Lua 5.1 ship different generators. The
pseudo-random spread comes from a Lehmer LCG whose whole orbit stays under 2^53, so it is exact
and identical in both.
