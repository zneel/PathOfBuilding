# Differential fuzzer — random build generator

Migration ticket 08 (`docs/migration/tickets/08-test-infrastructure.md`, issue
[#9](https://github.com/zneel/PathOfBuilding/issues/9)).

Generates randomised Path of Building builds from a seed — random tree allocations, random
items assembled from `src/Data/Mod*.lua`, random gem setups, random config — runs them through
the Lua engine via `src/HeadlessWrapper.lua`, and writes a deterministic JSON corpus of their
outputs.

The hand-written spec suite tests the mechanics somebody thought to test. This exists for the
ones nobody did: mod-tag combinations that only arise when a flask mod lands on a bow and a
cluster-jewel notable lands on a belt. That is the shape that drives `EvalMod`
(`src/Classes/ModStore.lua`) down tag-evaluation paths a curated test never reaches.

## Usage

Recipes only — no engine, runs under **both** `lua5.1` and `luajit`, from the repository root:

```
lua5.1 dotnet/tools/fuzz/generate.lua --plan-only --seed 20260909 --count 100
luajit dotnet/tools/fuzz/generate.lua --plan-only --seed 20260909 --count 100
```

Full corpus — boots the engine, **LuaJIT only**, and must run from `src/`:

```
cd src && luajit ../dotnet/tools/fuzz/generate.lua --seed 20260909 --count 50
```

| Option | Meaning |
|---|---|
| `--seed N` | RNG seed (default 20260909) |
| `--count N` | number of builds (default 25) |
| `--out PATH` | output file (default under `dotnet/tools/fuzz/corpus/`) |
| `--plan-only` | emit recipes without running the engine |
| `--quiet` | suppress the progress line |

Committed corpora live in `corpus/`:

- `fuzz-plan.json` — 100 recipes, seed 20260909, no engine.
- `fuzz-corpus.json` — the first 50 of those recipes plus the Lua engine's outputs for them.

`dotnet/Pob.Tests/FuzzCorpusTests.cs` reads both and asserts the properties the future
differential diff depends on.

## Reproducibility

Same seed, same count, same checked-in data ⇒ **byte-identical output**. Three separate
mechanisms hold that up.

**No `math.random`.** LuaJIT ships its own Tausworthe generator and stock Lua 5.1 forwards to
C `rand`; a corpus seeded through `math.random` reproduces on neither. `rng.lua` is a Lehmer
LCG (Park–Miller, 16807 mod 2³¹−1) whose entire orbit stays under 2⁵³, so every step is exact
in double arithmetic on any conforming Lua. `dotnet/tools/luacompat-oracle/dump.lua` solved the
same problem the same way.

**No `pairs` reaches a decision or the output.** Lua's hash iteration order is unspecified and
differs between the interpreters. Every pool in `pools.lua` is a dense array in a defined order
— sorted, or the source file's own order — and `json.lua` emits object keys in `table.sort`
order.

**Numbers are strings.** `json.lua` writes doubles as JSON strings: integers bare, `inf`,
`-inf`, `nan` and `-0` spelled explicitly, everything else in the shortest `%g` form that
round-trips. A JSON number invites a reader to re-round it; a string cannot be.

Per-build seeding is derived rather than sequential: the master stream produces a child seed
per build, and each build draws only from its own. So build 40's recipe does not shift when the
number of draws inside build 39's branch changes, and a 100-build plan is a strict superset of
a 50-build one from the same seed.

## Why the engine half is LuaJIT-only

`src/Launch.lua:18` calls `jit.opt.start`, and `src/Modules/Common.lua:19` indexes the `bit`
library — built into LuaJIT, and available to stock Lua 5.1 only through the LuaBitOp rock,
which is not installed here. So the engine boots under LuaJIT alone.

That is exactly why the generator was split from the runner. `pools.lua` and `plan.lua` read
`src/Data/*.lua`, `src/TreeData/<version>/tree.lua` and `src/GameVersions.lua` straight off
disk as plain data, never through the running engine, so `--plan-only` runs under either
interpreter and proves the *generator* is interpreter-independent. Only `apply.lua` touches the
engine.

## Files

| File | Role |
|---|---|
| `generate.lua` | CLI: parse options, load pools, generate recipes, optionally run them, emit JSON |
| `rng.lua` | Lehmer LCG |
| `json.lua` | deterministic JSON writer |
| `pools.lua` | pure loaders for tree nodes, gems, item bases, mod lines and config options |
| `plan.lua` | recipe generation |
| `apply.lua` | the only file that touches the engine |

## Failures are findings

`launch:OnFrame` (`src/Launch.lua:112`) runs the whole calculation inside `PCall` and, on
error, does **not** rethrow — it funnels the message into `launch:ShowErrMsg` →
`self.promptMsg` and carries on, and when a build crashes on its first calculation it also
calls `main:SetMode("LIST")`, detaching the build tab. A fuzzer that only wrapped its own calls
in `pcall` would report a clean run over a build that blew up, then read a **stale**
`mainOutput` as if it were the answer.

So `apply.lua` takes the prompt after every callback and checks the pending mode switch. A
message there is recorded as a note against the build, outputs are withheld, the prompt is
cleared, and the run continues. Build status is `ok` (nothing recorded), `partial` (recorded
something but still calculated) or `error` (no usable outputs).

As of the run that produced the committed corpus, **800 generated builds across five seeds
produced zero errors and zero notes.** That is a finding about the reference engine, not an
absence of testing — but it is bounded by what this generator does not yet produce; see below.

## Not yet fuzzed

Each of these is a real gap, listed so the next person does not have to rediscover it:

- **`list`-type config options.** `pools.lua` scans `src/Modules/ConfigOptions.lua` as text,
  because that file interpolates `data.monsterLifeTable` into a tooltip at load time (line 155)
  and so cannot be `dofile`d standalone. A text scan can recover `check`, `count`, `integer`
  and `float` options and their names — 508 of them — but not the value enumeration a `list`
  option needs. Setting one to an invalid value would produce noise, not findings.
- **Unique items.** Every generated item is a rare assembled from affix lines. Uniques carry
  hand-written mods with conditional and closure behaviour (`src/Data/Uniques/`), which is a
  richer source of tag corners than any affix.
- **Jewels in sockets.** Jewel bases are generated but not socketed into tree jewel sockets, so
  radius-jewel and cluster-jewel expansion paths go unexercised.
- **Timeless jewels, cluster jewel crafting, item enchants, corruptions, catalysts.**
- **Skill part / stance / trigger selection.** Socket groups are pasted with default settings.
- **Minions and party/aura interactions.**

## When the C# engine exists

The corpus's recipes are engine-agnostic on purpose: a class id, an ascendancy id, a level, a
list of tree node ids, item text in PoB's own format, socket-group paste strings, and config
variables. Once `Pob.Calc` exists (issues 22–27) the differential test is: read
`fuzz-corpus.json`, replay each `recipe` through the C# engine, and run the two output sets
through `Pob.Tests/Infrastructure/CorpusDiff.cs`. That is the diff ticket 08 asks for; the
corpus is one half of it, already recorded.
