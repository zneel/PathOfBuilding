# 17 — PassiveSpec: allocation, pathing, cluster jewels

**Phase** 2 · **Depends on** 16 · **Blocks** 21, 40

## Goal
Port `src/Classes/PassiveSpec.lua` (2,494 LOC) — the allocation state model.

## Scope
- Allocation state, BFS pathing between nodes, dependency computation.
- **Cluster-jewel subgraph generation** — synthesising nodes that do not exist in the base tree.
- Tree serialisation (the `<URL>` encoding in build XML).
- Mastery effect selection, timeless jewel transforms.
- Mixes in `UndoHandler` — undo state capture (see ticket 33 for the undo strategy).

## Gotchas
- Cluster-jewel node IDs are generated, not from tree data. Their stability across saves matters for build-file compatibility.
- `src/Modules/DataJewelFileLoader.lua` handles the **170 MB of split-zip TimelessJewel LUT binaries** — index-plus-blob, inflated to a `.bin` disk cache. **Port the reader, not the format.** Use `MemoryMappedFile` to keep them off the managed heap.

## Acceptance
Tree URLs from `spec/TestBuilds/3.13/*.xml` decode to identical allocation sets and re-encode identically.

## Libraries
`System.IO.Compression.DeflateStream`, `System.IO.MemoryMappedFiles`.
