# Pob.Tests/Infrastructure

Shared test infrastructure for the conformance oracles (migration ticket 08,
[#9](https://github.com/zneel/PathOfBuilding/issues/9)).

Tickets 05, 06, 07 and 08 each produce a corpus dumped from the Lua engine, and each will
eventually be replayed against the C# port. If each grows its own notion of "where is the
repository", "how do I read a Lua-spelled double" and "how close is close enough", the
migration's headline number — *we match the reference engine on N keys* — means three different
things depending on which suite reports it. This directory is that notion, once.

## Types

| Type | Role |
|---|---|
| `RepoPaths` | Locates the repository from the test assembly's output directory, anchored on `dotnet/PathOfBuilding.sln`. Also `RequireFile`, which fails with the command that regenerates a missing corpus. |
| `LuaNumber` | The wire spelling every Lua dump script uses for a number, and its inverse. |
| `ScalarValue` | One corpus scalar, tagged with the Lua type it had. |
| `ComparisonPolicy` | How strict a comparison is: `Exact`, `Default`, `SpecRelative`. |
| `NumericComparison` | The tolerance ladder. |
| `CorpusDiff` | Compares two keyed sets and reports every disagreement at once. |
| `CorpusReader` | Opening a corpus, checking its `format`, reading its scalars. |
| `FuzzCorpus` | Typed model over `dotnet/tools/fuzz/corpus/*.json`. |
| `SpecScanner` | Re-derives the mechanical half of the spec inventory from `spec/System`. |
| `SpecInventory` | Typed model over `spec-inventory.json`. |

## The tolerance ladder

`NumericComparison.Compare`, in order:

1. **Different Lua types never match.** A corpus `false` against a port `0` is a bug in the
   port, not a rounding question. This is why `ScalarValue` carries its kind: by the time a
   value has been widened to `double`, `true` and `1` are indistinguishable.
2. **Booleans and strings compare exactly**, strings ordinally.
3. **Identical bit patterns match, first and always.** `==` cannot do this job — it says `-0.0`
   equals `0.0` and that NaN equals nothing, and those are precisely the cases a conformance
   corpus exists to pin down.
4. **Under `BitExact`, nothing else matches.**
5. **NaN matches NaN**; an infinity matches only the same infinity.
6. **Under `IntegralsExact` (on by default), two whole numbers must be equal.** Counts,
   charges, levels, resistances and flags are integers by construction; at a magnitude of 4×10⁹
   a 1e-9 relative epsilon would swallow an off-by-one whole.
7. **Otherwise** the difference must be within
   `max(RelativeEpsilon × max(|expected|, |actual|), AbsoluteFloor)`.

The absolute floor is not decoration. A purely relative test on values near zero is effectively
a bit-exact test, because the tolerance shrinks with the values — which is wrong when the two
sides reached a near-zero number by different but equally valid summation orders.

`ComparisonPolicy.Default` is 1e-9 relative with a 1e-12 floor. That is loose enough to absorb
a differently-ordered summation and roughly seven orders of magnitude tighter than any
divergence the engine's own rounding produces: `More` rounds to two decimals per mod name, so a
genuine rounding disagreement shows up at the 1e-2 scale.

`ComparisonPolicy.Exact` is the setting for anything the port claims to reproduce bit for bit —
the `LuaCompat` primitives today, `Sum`/`More`/`EvalMod` when they exist.

## Note for tickets 05, 06 and 07

Those tickets were implemented concurrently with this one, so their suites were not migrated
onto these types. They should be, and it is a small change in each:

- `LuaCompatOracleTests.SolutionDirectory()` is a private copy of `RepoPaths.SolutionDirectory`;
  its `ParseNumber` is a private copy of `LuaNumber.Parse`. Both can be deleted in favour of the
  shared versions. Its bit-pattern comparison is exactly `ComparisonPolicy.Exact`, and routing
  it through `NumericComparison` would keep that claim in one place.
- The ModCache oracle (#6), mod-store dump (#7) and golden corpus (#8) should read through
  `CorpusReader` and compare through `CorpusDiff`, so that "how many keys do we match" is one
  number computed one way.

The golden corpus in particular should *not* invent its own epsilon: if it needs one looser than
`ComparisonPolicy.Default`, that is a finding about the port worth naming, and the right move is
to add a named policy here rather than a local constant there.

## spec-inventory.json

The ticket-08 survey of the busted spec suite: one entry per file in `spec/System`, with what it
exercises, which output keys and mod queries it asserts on, and which GitHub issue has to land
before it can be ported to xUnit.

Half of each entry is machine output and half is human review. `SpecScanner` re-derives the
machine half from the Lua sources on every test run, and `SpecInventoryTests` fails the build if
what is committed disagrees — a stale inventory understates what the engine tickets have to
satisfy, which is the one thing it exists to state.

Unlike the Lua-generated corpora this file is ordinary JSON with ordinary JSON numbers: it is
written on the C# side of the fence, so the "spell doubles as strings" convention does not apply
and would only obscure it.
