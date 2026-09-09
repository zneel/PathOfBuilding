# 43 — Replace the self-updater with Velopack

**Phase** 6 · **Depends on** 42

## Goal
Delete the hand-rolled updater wholesale.

## Scope
**Removed:** `src/UpdateCheck.lua`, `src/UpdateApply.lua`, `src/LaunchInstall.lua`, `runtime/Update.exe`, the `lzip` native dependency, `manifest.xml`/`manifest.cfg` update plumbing, `update_manifest.py`, and the `SpawnProcess`-based restart dance at `Launch.lua:329`.

**Added:** Velopack — modern, cross-platform, actively maintained. Handles delta updates, staged rollout, and the restart-into-new-version flow.

`lzip` was used only for reading update archives (`UpdateCheck.lua:12, 236-246`). `System.IO.Compression.ZipFile` covers any residual need.

## Gotchas
`manifest.xml` also drives which files ship (`part="tree"` for the 831 TreeData files). Whatever replaces it must still express that grouping so tree data can be updated independently of the binary.

## Acceptance
An update from version N to N+1 applies cleanly on Linux and Windows.

## Libraries
Velopack.
