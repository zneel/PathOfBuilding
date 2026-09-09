# 01 — Solution scaffold

**Phase** 0 · **Depends on** nothing · **Blocks** everything

## Goal
Stand up `dotnet/` with the project layout, target framework, analyzers and CI so every later ticket has somewhere to land.

## Scope
Create:
```
dotnet/PathOfBuilding.sln
dotnet/Pob.Core/          Pob.Parsing/   Pob.Data/     Pob.Calc/
dotnet/Pob.Platform/      Pob.Rendering/ Pob.App/      Pob.Tests/
dotnet/tools/transcode/
dotnet/Directory.Build.props
```

- Target `net10.0`. `Pob.App` is `net10.0` + Avalonia 11.x.
- `Directory.Build.props`: `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<InvariantGlobalization>true</InvariantGlobalization>`, `<LangVersion>latest</LangVersion>`.
- Central package management via `Directory.Packages.props`.
- `.editorconfig` with analyzer rules.
- GitHub Actions workflow building + testing on `ubuntu-latest` and `windows-latest`.

## Gotchas
- Do not add a DI container. The calc engine needs none — `CalcSession` is passed explicitly (see ticket 20). Avalonia's own service locator covers the app layer.
- `Pob.Core` must not reference Avalonia or SkiaSharp. The headless conformance tests depend on that separation, and it is what makes ticket 05–08 possible.

## Acceptance
- `dotnet build` and `dotnet test` green on both platforms in CI.
- Project reference graph is acyclic and `Pob.Core` has zero UI dependencies.

## Libraries
Central package management, `Microsoft.CodeAnalysis.NetAnalyzers`.
