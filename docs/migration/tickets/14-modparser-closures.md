# 14 — ModParser: the closure subset

**Phase** 2 · **Depends on** 13 · **Blocks** 15

## Goal
Hand-port the ~700 closures in `modTagList` and `specialModList` that are not mechanical. This is the bulk of the manual work in the parser and the largest single item in the domain port.

## Scope
`src/Modules/ModParser.lua:1320-5955`. Entries of the form `function(num) return {...} end` — they take captured numbers and produce mod arrays.

Target shape: `Func<Captures, Mod[]>` delegates, or named `static` methods with a source-generated dispatch switch (preferred — debuggable, and the generator can enforce completeness).

## Gotchas
- Work oracle-driven: ticket 05 gives you a pass count over 23,215 real mod lines. Sort failures by frequency and work down the list. Do not port closures in file order.
- Many closures are near-duplicates. Deduplicate as you go, the same way ticket 04 found 122 `preDamageFunc` bodies reducing to 59 distinct ones.

## Acceptance
**≥99.5% of the 23,215-line ModCache corpus parses to a byte-identical canonical form.** Remaining failures enumerated with a reason each.
