# 42 — Application shell and build list

**Phase** 5 · **Depends on** 33, 34, 35, 36

## Goal
Port `src/Modules/Main.lua` (1,822) and `src/Modules/Build.lua` (2,335) — the app shell and the God object that owns everything.

## Scope
- Mode switching: `main.modes.{LIST, BUILD}`.
- Build list / file browser: `BuildListControl.lua` (287), `src/Modules/BuildListHelpers.lua` (220), folder navigation, drag into folders.
- Settings (`Settings.xml`), the sidebar stat display driven by `src/Modules/BuildDisplayStats.lua` (293, declarative — port as data).
- Popup stack (`Main.lua:1660-1668`, index 1 = topmost), toast notifications (`ToastNotification.lua`, 254).
- Top/side bar chrome (`Build.lua:1329`).
- The in-app console (`^~`) if kept — otherwise drop it, `ConPrintf` goes to Serilog (ticket 29).

**Decompose the God object.** `Build.lua` currently owns `.spec`, all nine tabs, `.calcsTab.mainEnv`/`.mainOutput`, `.buildFlag`/`.modFlag`, and sidebar controls that read `calcsTab.mainOutput[statData.stat]` directly (`:1035-1074`). Split into: a build state model, a calc-result observable, and a shell ViewModel.

## Gotchas
`main:OnFrame` (`Main.lua:379`) recomputes `screenW`/`screenH` from `GetVirtualScreenSize()`, builds `self.viewPort`, then drains `self.inputEvents`. `main:OnKeyDown/OnKeyUp/OnChar` (`:495-505`) **only append to a queue** — input is already fully decoupled from processing. That is port-friendly; preserve the decoupling rather than wiring handlers directly to widgets.

## Acceptance
Application launches, lists builds, opens one, edits it, saves it.
