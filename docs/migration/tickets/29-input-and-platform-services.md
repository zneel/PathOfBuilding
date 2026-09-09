# 29 — Input mapping and platform services

**Phase** 4 · **Depends on** 27 · **Blocks** 33, 42

## Goal
The easy 75% of the host layer, where libraries do the work.

## Scope

**Input.** Avalonia `KeyDown`/`KeyUp`/`TextInput`/`PointerPressed`/`PointerWheelChanged` on the host control.
- Custom: a **~60-entry key-name mapping table** to PoB's strings. Mouse buttons and wheel arrive as *pseudo-keys*: `LEFTBUTTON, RIGHTBUTTON, MIDDLEBUTTON, MOUSE4, MOUSE5, WHEELUP, WHEELDOWN`, alongside `RETURN, ESCAPE, TAB, BACK, DELETE, HOME, END, PAGEUP, PAGEDOWN, F1..F6, PAUSE, PRINTSCREEN`, arrows, digits.
- `doubleClick` is free — `PointerPressedEventArgs.ClickCount`. The host currently does double-click detection, not Lua.
- `IsKeyDown` (93 sites) queries only 8 names: `ALT, CTRL, SHIFT, UP, DOWN, LEFT, RIGHT, LEFTBUTTON` → `KeyModifiers`.
- `GetCursorPos` (46 sites) → pointer position in virtual (DPI-divided) coords. Note `GetVirtualScreenSize` is **Lua, not host** — `src/Modules/Common.lua:1118`, a DPI-dividing wrapper.

**Filesystem.** `System.IO` covers all of it: `NewFileSearch` (20 sites) → `Directory.EnumerateFileSystemEntries`; `MakeDir`/`RemoveDir` → `Directory.CreateDirectory`/`Delete(recursive)`; `GetUserPath` → `Environment.GetFolderPath` (XDG on Linux); `GetScriptPath`/`GetRuntimePath` → `AppContext.BaseDirectory`. Add `Microsoft.Extensions.FileSystemGlobbing` if the spec strings need real globbing.

**Clipboard.** `Copy` (19) / `Paste` (4) → `TopLevel.Clipboard`. It is async — needs a shim or call-site change.

**Process.** `OpenURL` (11) / `SpawnProcess` (3) → `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`. `Restart()` → `Process.Start(Environment.ProcessPath); Environment.Exit(0)`. `SetForeground()` → `Window.Activate()`. `TakeScreenshot()` → `RenderTargetBitmap` + `SKImage.Encode`.

**Timing.** `GetTime()` (87 sites) → `Environment.TickCount64`. One line.

**Compression.** `Deflate`/`Inflate` → **`System.IO.Compression.ZLibStream`, not `DeflateStream`.** See ticket 31 — this must be verified empirically before it is trusted.

**Logging.** `ConPrintf` (184 sites) → `Microsoft.Extensions.Logging` + Serilog. `ConPrintTable` (11) → `System.Text.Json` indented. `SetProfiling` → drop, use dotnet-trace.

**Stub for now:** `GetCloudProvider` (`Main.lua:1761`) exists only to warn users their saves are in OneDrive/Dropbox. Return null initially; path-prefix matching later if wanted.

## Acceptance
Each service has a unit test; the key-map is exercised against the full name list.

## Libraries
`System.IO`, `Microsoft.Extensions.FileSystemGlobbing`, Serilog, `System.IO.Compression`.
