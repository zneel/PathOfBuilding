# 44 — Linux packaging and release CI

**Phase** 6 · **Depends on** 42, 43

## Goal
Ship it. Linux is a first-class target — that was the point of the rewrite.

## Scope
- Self-contained publish per RID: `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`.
- Consider NativeAOT. It is viable **only if** ticket 03's MessagePack AOT resolver and ticket 13's source-generated regexes are in place — both were chosen partly for this. Measure startup gain against build complexity before committing.
- Linux packaging: AppImage and/or Flatpak. A `.desktop` entry, icon, MIME association for `.xml` build files.
- Release workflow: tag → build all RIDs → Velopack release → GitHub release.
- Data artifacts (ticket 03 MessagePack packs, TreeData) versioned and shipped separately from the binary so a league update does not require a full app release.

## Gotchas
`src/Export/` stays in Lua and is **excluded from shipping** (`manifest.cfg:14`). Keep it excluded. It is Windows-bound anyway — it needs `bun_extract_file.exe` (not in the repo, `.gitignore:30-31`), Oodle decompression, and GIMP 3 console batch scripting (`Export/Tree/GimpBatch/gimp_batch.lua:9`).

## Acceptance
A tagged release produces working artifacts on all four RIDs, and the Linux build runs on a clean machine with no .NET installed.
