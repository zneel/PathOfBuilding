# Pob.Benchmarks

Performance baselines for the migration (ticket 08,
[#9](https://github.com/zneel/PathOfBuilding/issues/9)).

## Running

```
dotnet run -c Release --project dotnet/Pob.Benchmarks -- --filter '*' --job short
```

Drop `--job short` for full-precision runs. `--list flat` shows what is available. Reports land
in `BenchmarkDotNet.Artifacts/` next to this file (gitignored).

**This project is not in `PathOfBuilding.sln` yet.** Ticket 08 landed alongside tickets 05–07,
which share that file; adding it is a merge-time step:

```
dotnet sln dotnet/PathOfBuilding.sln add dotnet/Pob.Benchmarks/Pob.Benchmarks.csproj
```

It builds and runs standalone in the meantime, and it must not be added to a CI `dotnet test`
target — BenchmarkDotNet spawns a child process per benchmark and takes minutes.

## What is measured, and what is not

The ticket asks for baselines on `Sum`, `More`, `EvalMod` and `calcDamage`. **None of those
exist in C# yet** — they arrive with issues 11, 12 and 22–27 — and a benchmark over a
placeholder type measures the placeholder. So the benchmarks here cover `Pob.Core.LuaCompat`
(ticket 02, [#3](https://github.com/zneel/PathOfBuilding/issues/3)), which does exist.

That is not a consolation prize. All four of the ticket's targets are built out of these
primitives:

- `ModDB:More` rounds to two decimals once per mod name — `src/Classes/ModDB.lua:197`.
- A `Multiplier` tag floors with a `+0.0001` epsilon on every evaluation —
  `src/Classes/ModStore.lua:403`. This is the hottest single line in the mod engine.
- `ScaleAddMod` does a round-then-`modf` per scaled mod — `src/Classes/ModStore.lua:82`.
- `data.highPrecisionMods` takes a different floor with no epsilon at all —
  `src/Classes/ModDB.lua:194-195`.

Each Lua-compatible helper is benchmarked next to the raw `System.Math` call it replaces, so
the report answers a specific question: what does bit-for-bit Lua compatibility cost over the
naive .NET call? That number is what justifies — or eventually challenges — the banned-API rule
in `dotnet/BannedSymbols.txt`.

Inputs come from `BenchmarkInputs`, a fixed set built with the same Lehmer LCG the fuzz tooling
uses, for the same reason: `Random` with a fixed seed is not a stable contract across .NET
versions, and a benchmark comparison across two commits has to be comparing the same workload.
The mix spans six orders of magnitude, both signs, and includes exact halves — the fork where
half-up and half-to-even part company, and the branch a benchmark over well-behaved positive
numbers would never take.

Every benchmark returns an accumulated value, which is what stops the JIT deleting the loop.

## Baseline as of the ticket-08 branch

`--job short`, Intel Xeon 2.10GHz, .NET 10.0.11, Ubuntu 24.04, 4096 values per operation:

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `Math.Round` (banker's — wrong for the engine) | 10.278 µs | 1.00 | – |
| `LuaCompat.Round` | 2.577 µs | 0.25 | – |
| `LuaCompat.Round(v, 2)` | 5.085 µs | 0.49 | – |
| `LuaCompat.MathFloor` | 10.193 µs | 0.99 | – |
| `LuaCompat.Floor(v, 2)` | 5.089 µs | 0.50 | – |
| `LuaCompat.RoundSymmetric` | 3.746 µs | 0.36 | – |
| `LuaCompat.Modf` | 5.948 µs | 0.58 | – |
| site: `ModDB:More` scale | 5.228 µs | 0.51 | – |
| site: `Multiplier` tag floor | 5.437 µs | 0.53 | – |
| site: `ScaleAddMod` value scale | 9.503 µs | 0.92 | – |
| site: high-precision `More` | 5.176 µs | 0.50 | – |

Two things worth noting. `LuaCompat.Round` is roughly **four times faster** than `Math.Round`,
because Lua's `floor(v + 0.5)` is a single rounding operation while banker's rounding has to
inspect the midpoint case — so the correctness rule costs nothing here, it pays. And every
entry allocates zero bytes, which is the property that has to survive contact with the calc
engine: a per-mod allocation in `EvalMod` would be invisible in a unit test and fatal in a
recalculation loop.

## Next

When issues 11, 12 and 22–27 land, add benchmark classes for `ModStore.Sum`, `ModStore.More`,
`EvalMod` and `calcDamage` beside this one — those are the ticket's actual acceptance criteria,
and this project exists so that adding them is one file rather than a new harness.
