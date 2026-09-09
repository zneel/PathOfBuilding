# 30 — HTTP layer: delete LaunchSubScript

**Phase** 4 · **Depends on** 01 · **Blocks** 32, 41

## Goal
Replace libcurl and the entire background-thread mechanism with `async`/`await`. This ticket is mostly deletion.

## Scope

**What `LaunchSubScript` actually is** (`_SimpleGraphic.def.lua`, 7 call sites):
- `scriptText` is Lua **source**, not a function. The host spawns a fresh Lua state on a separate OS thread. **No shared memory** — no upvalues, no globals, no closures cross the boundary.
- Arguments are primitives only (`nil|boolean|number|string`), copied in as varargs.
- `funcList` — main-thread host functions the child may call synchronously.
- `subList` — names the host **materialises as real globals inside the child state**; calling one marshals args back to the parent's `OnSubCall` (`Launch.lua:201-208`, which does `_G[func](...)`). See `UpdateCheck.lua:222`: `if UpdateProgress then UpdateProgress(...)` — the guard exists because the same file also runs standalone where that global does not exist.
- Returns marshal to `OnSubFinished(id, ...)`, errors to `OnSubError(id, errMsg)`. Callbacks run **on the main thread between frames** and mutate UI state with no locking.

**All seven uses are network I/O**, and all build their script bodies by **string-concatenating values into Lua source**:
1. `Launch.lua:315` — generic `DownloadPage`
2. `Launch.lua:348` — runs `UpdateCheck.lua` in a worker
3. `Classes/PoEAPI.lua:112` — long-lived OAuth loopback server
4. `Modules/BuildSiteTools.lua:50` — paste-site upload
5. `Classes/TreeTab.lua:769` — poeurl.com redirect resolution
6. `Classes/PoBArchivesProvider.lua:51`
7. `Classes/TradeQueryGenerator.lua`

**Replacement: `HttpClient` via `IHttpClientFactory`, one named client per integration.** Every curl option used maps 1:1:
| curl | .NET |
|---|---|
| `OPT_HTTPHEADER` | `DefaultRequestHeaders` |
| `OPT_USERAGENT` | `UserAgent` |
| `OPT_ACCEPT_ENCODING` | `AutomaticDecompression` |
| `OPT_FOLLOWLOCATION` | `AllowAutoRedirect` |
| `OPT_PROXY` | `WebProxy` |
| `OPT_IPRESOLVE` | `ConnectCallback` with an `AddressFamily` |
| `OPT_SSL_VERIFYPEER/HOST` | `ServerCertificateCustomValidationCallback` |
| `INFO_REDIRECT_URL` | `AllowAutoRedirect=false` + read `Location` |

`subList` progress callbacks → `IProgress<T>`. Continuations back to the UI → `Dispatcher.UIThread.Post`.

## Gotchas
- **`src/Modules/BuildSiteTools.lua:47` writes `response = LaunchSubScript(...)` as if it were synchronous. It is not.** Latent bug in the Lua — fix it in the port rather than reproducing it.
- The 8 build-hosting sites are a **runtime-configurable table** with regex URL rewriting (`BuildSiteTools.lua:20-43`: Maxroll, pob.codes, pobb.in, PoeNinja, Pastebin.com, PastebinP.com, Rentry.co, poedb.tw), each with `matchURL`/`regexURL`/`downloadURL`/`postUrl`/`postFields`. **Keep this data-driven with raw `HttpClient`** so sites can be added without a recompile. Do not use Refit here.

## Acceptance
All 7 integrations working; no Lua-source string concatenation anywhere.

## Libraries
`HttpClient` + `IHttpClientFactory`, `Microsoft.Extensions.Http.Resilience` / Polly.
