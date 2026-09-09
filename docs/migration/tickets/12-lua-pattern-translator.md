# 12 — Lua-pattern → Regex translator

**Phase** 2 · **Depends on** 01 · **Blocks** 13, 14

## Goal
A small translator so the ~4,000 mod patterns can be converted mechanically instead of by hand.

## Scope
`src/Modules/ModParser.lua` uses **Lua patterns, not regex**. Differences that matter:
- `%d %a %s %l %w` character classes; `%%` escape; `%` is the escape char, not `\`
- `.-` is lazy-star; `.` **does** match newline
- `[%+%-]` bracket classes
- Captures via `()`
- **No alternation, no `|`, no `?` on groups, no backreferences**

Authors work around missing alternation with per-character classes like `"[hd][ae][va][el]"` (matching "have"/"deal", `ModParser.lua:1104`). A naive translation is *safe* but must handle those literal classes correctly.

~200 LOC. Translate to `System.Text.RegularExpressions.Regex`; **do not write a Lua-pattern interpreter.**

## Acceptance
- Round-trip test: every pattern in `ModParser.lua` translates, and translated patterns produce identical match positions and captures against the ModCache corpus (ticket 05) as the Lua originals.

## Libraries
`System.Text.RegularExpressions`, `[GeneratedRegex]` (.NET 7+) for compile-time-generated matchers — faster than `RegexOptions.Compiled` and AOT-safe. `RegexOptions.NonBacktracking` where the pattern permits.
