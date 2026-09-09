# 31 — Build XML and build codes

**Phase** 4 · **Depends on** 15, 29 · **Blocks** 41, 42

## Goal
Read and write build files and the shareable build codes. **Compatibility with the existing ecosystem is the whole point of this ticket** — get it wrong and every published build link breaks.

## Scope

**Build files.** Plain XML, root `<PathOfBuilding>`, written by `buildMode:SaveDB` (`src/Modules/Build.lua:2277`), parsed by `LoadDB` (`:2222`) via `runtime/lua/xml.lua`.

Structure (see `spec/TestBuilds/3.13/OccVortex.xml`, 585 lines): `<Build>` with cached `<PlayerStat>` elements, `<Import>`, `<Calcs>`, `<Skills>`→`<Skill>`→`<Gem>`, `<Tree>`→`<Spec>`→`<URL>`/`<Sockets>`/`<EditedNodes>`, `<Items>`→`<Item>`/`<ItemSet>`→`<Slot>`/`<Socket>`, `<Notes>`, `<TreeView>`, `<Config>`→`<Input>`. Attribute-heavy, shallow, with free-text `<Item>` bodies in the dialect from ticket 15.

Savers are **pluggable per section** — `Build.lua:2282-2288` iterates `self.savers`.

**Use `System.Xml.Linq`, not `XmlSerializer`.** The format is dynamic (pluggable savers), has repeated heterogeneous elements, and **must round-trip unknown sections from newer versions without data loss**. `XDocument` handles all three; `XmlSerializer` fights you on every one.

**Build codes.** `base64url(Deflate(xml))` with `+`→`-`, `/`→`_`. Encode: `Classes/ImportTab.lua:505`, `Modules/Build.lua:1598`. Decode: `Modules/Main.lua:77`, `ImportTab.lua:632`, `PartyTab.lua:150`, `CompareTab.lua:1262,1498,1514`, `Common.lua:1098,1103`.

## ⚠️ The one thing to verify before writing any code
`Deflate`/`Inflate` are native functions in `SimpleGraphic.dll`; `src/_SimpleGraphic.def.lua:365,372` are stubs only. **Whether the stream is zlib-wrapped (header + adler32) or raw deflate is not determinable from this repository.** `runtime/zlib1.dll` is present, which points to zlib format — but **test both `ZLibStream` and `DeflateStream` against a real published build code before committing.** Getting this wrong silently breaks compatibility with pobb.in and every community tool.

## Acceptance
- Every file in `spec/TestBuilds/3.13/*.xml` loads, saves, and is byte-identical.
- A published pobb.in code round-trips.
- An XML file containing an unknown section survives a load/save cycle intact.

## Libraries
`System.Xml.Linq`, `System.IO.Compression.ZLibStream`, `Base64Url` (.NET 9) or `Convert.ToBase64String` + replace.
