# 41 — Import, Party and Compare tabs

**Phase** 5 · **Depends on** 30, 31, 32, 38

## Goal
The three tabs whose work is mostly in the async/network layer rather than the UI.

## Scope
**ImportTab** (1,932 LOC) — character import from PoE account and pobb.in, build code import/export. 105 `build.` references. Ordinary forms over ticket 32's API client.

**PartyTab** (1,042 LOC) — party/aura buff sharing from another build. Ordinary forms.

**CompareTab** (5,036 LOC) — side-by-side stat and calc diffing, power report, "buy similar". Supporting classes: `CompareEntry.lua` (552, a headless `Build` wrapper), `CompareCalcsHelpers.lua` (476, stateless calc-tooltip formatting), `CompareBuySimilar.lua` (538, trade URL builder), `ComparePowerReportListControl.lua` (165), `ExtBuildListControl.lua` (440), `ExtBuildListProvider.lua` (63), `PoBArchivesProvider.lua` (150).

## Gotchas
- **`CompareTab` is the newest and least-settled code in the tree.** Porting it is aiming at a moving target — schedule it late and expect churn.
- It instantiates whole shadow builds via `CompareEntry`. That requires ticket 20's `CalcSession` to be genuinely reentrant, and it is the best real-world test of it.
- `ExtBuildListControl` is paged and async — a good fit for ticket 30's `HttpClient` + `IProgress<T>`.

## Acceptance
Import from a live account, compare two builds, generate a working trade URL.
