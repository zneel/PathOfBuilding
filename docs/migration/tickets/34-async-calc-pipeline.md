# 34 — Async calc pipeline

**Phase** 5 · **Depends on** 20, 26, 33 · **Blocks** 36, 38, 39

## Goal
Move the calculation engine off the UI thread. **Risk item #4.**

## The problem
`build.buildFlag = true` is **the only invalidation signal in the entire program**. Roughly 30 sites in `Build.lua` alone set it, plus widget callbacks like `ItemSlotControl.lua:28`. Once per frame `buildMode:OnFrame` checks it and runs **the entire calculation engine synchronously on the draw thread** (`src/Modules/Build.lua:1258-1272`):

```
wipeGlobalCache() → skillsTab:UpdateSocketGroups() → calcsTab:BuildOutput() → RefreshStatList()
```

PoB gets away with this because an immediate-mode loop has no notion of responsiveness — a slow frame is just a slow frame. **In Avalonia this freezes the window.**

## Scope
1. `buildFlag` → a debounced `PropertyChanged` subscription (`Observable.Throttle`).
2. Run the engine on a background thread with **cancellation of superseded runs**.
3. **Snapshot / immutable input model.** The engine currently reads mutable tab state directly. It needs an immutable snapshot to run against, or results will be torn.
4. Marshal results back via `Dispatcher.UIThread.Post`.
5. **`ConfigTab`'s hover preview runs the engine during mouse-over** — `calcsTab:GetMiscCalculator()` (`Build.lua:1273`) hands the config UI a live closure into the engine. This needs its own debounce plus cancellation, separate from the main recalc.

## Gotchas
Ticket 20 (`CalcSession`) is a hard prerequisite. Without it the engine has global mutable state and cannot run concurrently with anything, including itself.

## Acceptance
- UI stays responsive during a full recalc on the heaviest golden build.
- Rapid config changes produce exactly one final correct result, with intermediate runs cancelled.

## Libraries
`System.Reactive` (`Observable.Throttle`), `CancellationToken`.
