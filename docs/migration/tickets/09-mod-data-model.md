# 09 — Mod data model

**Phase** 2 · **Depends on** 02 · **Blocks** 10, 11, 13, 14

## Goal
The `Mod` record, flag enums, tag hierarchy and query config. Everything else in the engine is built on these types, so get them right first.

## Scope
Reference: `src/Modules/ModTools.lua:33-67` (`modLib.createMod`), `src/Data/Global.lua:102-218` (`ModFlag`, `KeywordFlag`, `MatchKeywordFlags`, `SkillType`).

A Lua mod is **both a record and an array**:
```lua
{ name=..., type=..., value=..., flags=0, keywordFlags=0, source=...,  -- named fields
  [1]=tag, [2]=tag, ... }                                              -- array part = tag list
```

**Fields:**
- `name` — string stat key. Conditions and multipliers are **not** a separate mechanism: they are mods whose name is prefixed (`"Condition:Moving"`, `"Multiplier:PowerCharge"`), interned via a memoizing `__index` at `src/Classes/ModStore.lua:21-28`.
- `type` — `BASE` (summed), `INC` (summed, applied as `1+Σ/100`), `MORE` (multiplied, **with per-name rounding**), `FLAG` (boolean OR), `OVERRIDE` (first non-nil wins), `LIST` (collects payloads), `MAX`/`MIN`. Parser-internal transient types (`RED`, `LESS`, `CHANCE`, `PEN`, `DMG`, `DOUBLED`) never reach the store — normalised in `parseMod`.
- `flags` (`ModFlag`) — 32-bit mask. **Semantics: applies iff `(cfg.flags & mod.flags) == mod.flags`** — the mod's flags are a *subset* of the query context. AND-all.
- `keywordFlags` (`KeywordFlag`) — **different semantics: match ANY by default**; a mod carrying `MatchAll (0x40000000)` switches to match-all. See `Global.lua:189-214`.
- `source` — provenance like `"Item:Weapon 1"`, `"Tree:12345"`, `"Skill:Fireball"`. **Filtering compares only the prefix before the first `:`** — literally `mod.source:match("[^:]+") == source` in every query.
- Array part — tags. `mod[1] == nil` is the fast path: value used directly, no evaluation.
- `value` — `number | boolean | string | table`. Table values are common and recursive: `{ mod = <inner mod> }`, `{ key=..., value=... }`, `{ mod=..., onlyAllies=... }`.

**Proposed C# shapes:**
```csharp
public readonly record struct ModName(int Id);          // interned
public readonly record struct SourceId(int Id);
public readonly record struct SourcePrefix(int Id);     // precomputed prefix-before-':'

[Flags] public enum ModFlag : uint { None=0, Attack=1, Spell=2, Hit=4, Dot=8, Cast=0x10,
    Melee=0x100, Area=0x200, Projectile=0x400, Ailment=0x800, MeleeHit=0x1000, Weapon=0x2000,
    Axe=0x10000, /* ... */ Weapon2H=0x2000_0000 }
[Flags] public enum KeywordFlag : uint { None=0, Aura=1, /* ... */ MatchAll=0x4000_0000 }
public enum ModType : byte { Base, Inc, More, Flag, Override, List, Max, Min }

public readonly struct ModValue {            // discriminated union, struct to avoid boxing
    public readonly double Number; public readonly bool Bool;
    public readonly object? Obj; public readonly ModValueKind Kind;
}

public sealed class Mod {
    public ModName Name; public ModType Type; public ModValue Value;
    public ModFlag Flags; public KeywordFlag KeywordFlags;
    public SourceId Source; public SourcePrefix SourcePrefix;
    public ModTag[] Tags;                    // Array.Empty<ModTag>() == the mod[1]==nil fast path
}
```

Tags: **abstract record hierarchy** (`sealed record MultiplierTag(...) : ModTag`) with a `switch` expression in `EvalMod`. The JIT produces a type-check chain comparable to Lua's `tag.type ==` chain and you get exhaustiveness checking. Avoid a property bag — the 40+ optional tag fields belong as nullable members per tag type.

## Gotchas
- **`createMod`'s positional type-sniffing is the most error-prone thing in the whole port.** After `(name, type, value)`: a `string` becomes `source`, then a `number` becomes `flags`, then a `number` becomes `keywordFlags`, remainder are tags (`ModTools.lua:38-49`). So `NewMod("X","BASE",5,{tag})` has no source while `NewMod("X","BASE",5,"Item",{tag})` does. **Do not replicate the sniffing.** Write explicit named-argument overloads or a builder and mechanically fix all ~5,000 call sites. Silently mis-attributing an argument produces mods that look right and query wrong.
- Make `Mod` immutable (`init`-only) with an explicit `WithValue(double)`. `ScaleAddMod` mutates copies (`ModStore.lua:56-91`) and its precision logic must be ported literally.
- `KeywordFlag` needs no memo cache. Lua's two-level cache at `Global.lua:189` exists because LuaJIT's `bit` library is slow, not because the logic is — it's two ANDs and a compare.

## Acceptance
Ticket 06's mod-store dump diffs clean against the Lua reference for all 5 test builds.
