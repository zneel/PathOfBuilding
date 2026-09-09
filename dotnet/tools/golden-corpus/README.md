# golden-output-corpus

An end-to-end numeric oracle for the calc engine port — migration ticket 07
(`docs/migration/tickets/07-golden-output-corpus.md`, issue
[#8](https://github.com/zneel/PathOfBuilding/issues/8)).

Each build in the corpus is one file pairing the exact XML the engine was given with every
value it produced, for both calc modes and all three actors. The C# side replays them:
`dotnet/Pob.Tests/Golden*.cs`.

```
luajit ../dotnet/tools/golden-corpus/generate.lua      # run from src/ — writes the corpus
luajit ../dotnet/tools/golden-corpus/legacy-check.lua  # cross-check against the Lua project's own goldens
luajit ../dotnet/tools/golden-corpus/selftest.lua      # prove the engine-error detector fires
```

Output lands in `dotnet/Pob.Tests/oracles/golden/`: `<name>.golden.json.gz` per build plus
`manifest.json`. Environment overrides: `POB_GOLDEN_OUT`, `POB_GOLDEN_LIMIT`,
`POB_GOLDEN_FILTER`, `POB_GOLDEN_NOGZIP=1`.

## Files

| File | Role |
|---|---|
| `generate.lua` | Driver. Defines the build matrix and writes the corpus. |
| `matrix.lua` | The build space: gear kits, gem classification and quotas, weapon resolution. |
| `chassis.lua` | Turns one spec into a real build — tree, items, gems — plus the engine-error detector. |
| `dump.lua` | Flattens actor output tables into the flat key/value maps a golden file stores. |
| `xmlcanon.lua` | Deterministic replacement for `common.xml.ComposeXML`. |
| `json.lua` | Deterministic JSON writer with lossless number formatting. |
| `legacy-check.lua` | Cross-checks the capture path against `spec/TestBuilds/3.13/*.lua`. |
| `selftest.lua` | Demonstrates that a swallowed engine error is caught, not recorded. |

## Why this is a new tool and not a change to `spec/GenerateBuilds.lua`

The existing script does two things that a port oracle cannot live with.

It only reads `.xml` files that someone has already checked in, so the corpus is exactly as
large as the set of builds a person exported by hand — five, all from 3.13. This generator
constructs its builds from the engine's own data (every keystone on the tree, every
class/ascendancy pair, a stratified sample of the gem list, every weapon base class), so the
corpus is a function of the data the port has to handle and it re-derives itself when the game
data changes.

And it writes `round(value, 4)`. Four decimal places is enough to catch a broken formula and
not enough to ever tighten the comparison to bit-exact, and rounding at generation time cannot
be undone. This generator writes `%.17g` — the shortest spelling that round-trips an IEEE-754
double — so `GoldenTolerance` can climb from `1e-9` relative to bit-exact without the corpus
being regenerated.

Nothing here modifies `spec/`. Whether `GenerateBuilds.lua` should later be replaced by a thin
wrapper over `dump.lua` is a reasonable follow-up; see the note at the bottom.

## Three things that would silently poison the corpus

**Swallowed engine errors.** `launch:OnFrame` (`src/Launch.lua:112`) runs the whole
calculation inside `PCall` and does **not** rethrow: on error it stashes the message in
`launch.promptMsg` and continues, and `Launch.lua:115` ejects a build that crashed on its first
calculation back to the build list. A generator that wraps only its own calls in `pcall` sees a
clean run and reads a `build.calcsTab.mainOutput` that still holds the *previous* build's
numbers — no exception, plausible values, permanently wrong answer key. `chassis.frame` and
`chassis.assertNoEngineError` check `launch.promptMsg`, the pending deferred mode switch, and
`main.mode` after every frame, and clear the prompt between builds so a failure cannot leak
forward. `selftest.lua` injects a real calculation failure and shows the detector firing, the
stale value that would otherwise have been written, and the next build recovering.

**Non-deterministic serialisation.** `build:SaveDB()` writes attributes in `pairs()` order, and
several savers build their child lists by iterating hash tables (`ConfigTab` over inputs and
placeholders, `ItemsTab` over slots, `PassiveSpec` over `allocNodes`). LuaJIT does not promise a
stable iteration order across processes and in practice does not deliver one, so the same build
serialises to different bytes on every run. `xmlcanon.lua` composes the XML itself: attributes
sorted, `<Spec nodes>` sorted numerically, children sorted only for element names whose order
carries no meaning, and the output-cache elements (`<PlayerStat>`, `<MinionStat>`,
`<FullDPSSkill>`, `<URL>`, `<Section>`) dropped because `Build:Load` never reads them and an
input file should not contain its own answers.

**Escaping into the object graph.** `output.ReqStrItem.sourceItem` and `.sourceGem` are live
references to the Item and gem instance a requirement came from, and following them reaches the
entire gem and mod database: walking an output table naively produces 780,000 keys and a 160 MB
file per build. `dump.lua` cuts at those references, caps depth at three path segments, and
raises if a section still exceeds 20,000 keys.

## File format

```jsonc
{
  "schema": 1,
  "name": "keystone-resolute-technique",
  "group": "keystone",
  "notes": { "treeVersion": "3_29", "classId": 4, "totalDps": 26550.6, "keyCount": 1764, ... },
  "inputXml": "<?xml version=\"1.0\" ...",
  "sections": {
    "MAIN/player":  { "Life": 3575, "TotalDPS": 35353.982737566998, "MainHand/CritChance": 5.5, ... },
    "MAIN/enemy":   { ... },
    "MAIN/minion":  { ... },          // only when the main skill summons something
    "CALCS/player": { ... },
    "CALCS/enemy":  { ... },
    "CALCS/minion": { ... }
  }
}
```

Nested output tables are flattened with `/`, so `SkillDPS` — the full-DPS breakdown the ticket
asks for — appears as `SkillDPS/1/dps`, `SkillDPS/1/name` and so on, and needs no separate
comparison path. Values are JSON numbers, booleans or strings; the doubles JSON cannot spell
are written as `"__inf"`, `"__-inf"` and `"__nan"`, and a table that was deliberately not
followed as `"__table"`.

`MAIN` and `CALCS` are genuinely different calculations, not a duplicate: `CALCS` resolves the
Calcs tab's own skill and skill-part selection where `MAIN` resolves the build's.

## Size

Full-precision output for 270 builds is roughly 26 MB of JSON. The generator gzips each file
with `gzip -9n` (`-n` keeps the name and mtime out of the header, which is what makes two runs
byte-identical), bringing the corpus to ~2.4 MB — the same order as the existing
`Pob.Tests/oracles/luacompat.json`. Set `POB_GOLDEN_NOGZIP=1` to read one by eye; the C# loader
accepts both shapes, and falls back to uncompressed automatically when `gzip` is not on the
PATH.

## Determinism

Two full runs must produce byte-identical files. Check it with:

```sh
cd src
luajit ../dotnet/tools/golden-corpus/generate.lua
( cd ../dotnet/Pob.Tests/oracles/golden && ls | sort | tr '\n' '\0' | xargs -0 cat | sha256sum )
POB_GOLDEN_OUT=/tmp/golden-b luajit ../dotnet/tools/golden-corpus/generate.lua
( cd /tmp/golden-b && ls | sort | tr '\n' '\0' | xargs -0 cat | sha256sum )
```

This is a run-to-run check under LuaJIT, and only that. Stock `lua5.1` cannot run the engine at
all (`goto` in `src/Data.lua`, `jit.opt.start` in `src/Launch.lua`, `bit.*` throughout), so
there is no cross-interpreter determinism claim to make here.

## Cross-check against the existing oracle

`legacy-check.lua` runs the five builds in `spec/TestBuilds/3.13` through this tool's capture
path and reports two separate numbers:

- **capture path vs `build.calcsTab.mainOutput`** — does this tool read the same values
  `spec/System/TestBuilds_spec.lua` reads? This is the statement about the tool, and it must be
  zero.
- **live engine vs the frozen `.lua` values** — does today's engine still produce what was
  frozen in 3.13? It does not, and that is a pre-existing property of the repository, not of the
  corpus: those values are 3.13-era while the tree, the gem data and the calc code have all moved
  on, which is why `.busted` excludes the `#builds` tag from the default run. The five builds are
  included in the corpus with their *current* outputs; the stale `.lua` files are left untouched.

## Possible follow-up: upstreaming to `spec/GenerateBuilds.lua`

Worth doing, but as a separate change, and probably not as a rewrite of that script.

The reusable part is `dump.lua` — the flattening and the choice of what to capture. The Lua
project's spec suite would get real value from capturing `CALCS` mode and the minion and enemy
actors instead of `mainOutput` alone, since those are calculation paths its `#builds` suite
currently cannot see at all. Emitting full precision would help it too: `TestBuilds_spec.lua`
rounds at comparison time anyway, so it would lose nothing and gain the ability to tighten later.

What should **not** be upstreamed is the rest of this tool. `xmlcanon.lua` exists because a
corpus has to be hashable, which a hand-maintained set of five exported builds does not; the
build matrix in `matrix.lua` targets a port's coverage needs rather than the Lua project's
regression needs; and changing `GenerateBuilds.lua`'s output format would invalidate five
checked-in golden files that the Lua project still uses. The honest sequencing is: refresh those
five `.lua` goldens against the current engine first (a separate decision with its own review),
and only then consider whether the generation script should share code with this one.
