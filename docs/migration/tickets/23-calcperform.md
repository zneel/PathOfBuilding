# 23 — CalcPerform: the orchestrator

**Phase** 3 · **Depends on** 22 · **Blocks** 24, 25, 26

## Goal
Port `src/Modules/CalcPerform.lua` (3,990 LOC) — `calcs.perform` at `:1286`.

## Scope
Sequence:
```
modLib.mergeKeystones                         (ModTools.lua:248)
minion skills / DBs
flasks
doActorAttribsConditions  (:138)
doActorLifeMana           (:69)
reservations              (:551)
buffs / auras / curses
doActorCharges            (:980)
doActorMisc               (:636)
calcs.defenceForConditionals (:3484)
→ calcs.defence            (:3792)  [ticket 25]
→ calcs.buildDefenceEstimations
→ calcs.triggers           (:3797)  [ticket 26]
→ calcs.mirages                     [ticket 26]
→ calcs.offence            (:3799)  [ticket 24]
repeat defence/triggers/offence for env.minion (:3803-3808)
```

## Gotchas
- Sets `modDB.multipliers[...]` directly in many places (e.g. `WarcryPower` at `:1348`) — the non-mod fast path from ticket 10. Order relative to mod merging matters.
- The minion pass re-runs the same three stages against a different actor. Structure the code so that is a parameterised call, not a copy.

## Acceptance
Golden build outputs match for the non-offence keys.
