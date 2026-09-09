# 33 — MVVM foundation: kill the function-valued property system

**Phase** 5 · **Depends on** 27, 29 · **Blocks** 34–42

## Goal
Replace PoB's pull-based, 60 Hz re-evaluation model with change-driven bindings. **This is risk item #2 and the highest-volume manual work in the port.**

## The problem
PoB's controls are objects with persistent state, but there is **no invalidation, no dirty rect, no layout pass, and no event dispatch tree**. Every frame the entire UI is re-walked and re-drawn.

`Control:GetProperty` (`src/Classes/Control.lua:73-79`) resolves any field by calling it if it is a function. So `width`, `height`, `x`, `y`, `shown`, `enabled`, `label`, `title` can each be a closure re-evaluated every frame. `src/Modules/Build.lua:148-153` computes the build-name field's width from six sibling controls' current sizes.

**There is no observable, no `INotifyPropertyChanged`, no change notification anywhere in the application.**

## Scope
1. Establish the MVVM base: **CommunityToolkit.Mvvm**, not ReactiveUI. PoB's model is overwhelmingly "property changed → recompute"; `[ObservableProperty]` + `[RelayCommand]` source generators give you `INotifyPropertyChanged` for free. Reserve `System.Reactive` for the debounced calc trigger (ticket 34) without adopting the whole framework.
2. Use **compiled bindings** (`x:CompileBindings="True"`). With this much binding, reflection binding will hurt.
3. **Audit every function-valued property individually.** Each closure must be analysed for its true dependencies and converted to a binding, a `MultiBinding`, or a computed property with correct `PropertyChanged` propagation. **Miss a dependency and you get a stale UI that only sometimes updates** — the worst class of bug to diagnose. There is no automated path; budget accordingly.
4. Port the anchor system where it is still needed. `Control:GetPos` (`Control.lua:92-119`) resolves the parent's position and size **recursively and uncached** on every call. Most anchors become ordinary Avalonia layout. The exception: **`anchor.collapse`** (`Control.lua:93-98`) has no XAML analogue — if the anchor parent is hidden, the control collapses onto the parent's position rather than offsetting, producing an auto-compacting stack (`ItemSlotControl.lua:31`). And `IsShown` (`:126`) cascades: hiding a control implicitly hides everything anchored to it. Model both explicitly.
5. Undo: `src/Classes/UndoHandler.lua` (58 LOC) is mixed into `ItemsTab`, `SkillsTab`, `ConfigTab`, `CalcsTab`, `PassiveSpec`. Keep it as-is or move to a command-pattern stack — decide here, once.

## Gotchas
- `ControlHost.controls` is **string-keyed and iterated with `pairs()`** — draw order is genuinely non-deterministic (`ControlHost.lua:32, 110`), and overlap is resolved purely by `SetDrawLayer`. Do not try to preserve "existing" order; there isn't one.
- **Multiple inheritance.** `newClass` (`src/Modules/Common.lua:83-130`) builds an `__index` chain over a parent list. `DropDownControl` is `Control + ControlHost + TooltipHost + SearchHost` — four bases. C# single inheritance cannot express this: use interfaces + composition, or C# default interface members.

## Acceptance
A representative screen (ticket 36's ConfigTab) renders and updates correctly with zero per-frame property evaluation.

## Libraries
CommunityToolkit.Mvvm, Avalonia compiled bindings.
