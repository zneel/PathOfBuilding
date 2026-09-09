# 32 — PoE OAuth and trade API

**Phase** 4 · **Depends on** 30 · **Blocks** 41

## Goal
Character import and trade search.

## Scope
**OAuth** — `src/Classes/PoEAPI.lua` (238 LOC). Authorization-code + **PKCE S256** against `https://www.pathofexile.com/oauth/authorize` and `/oauth/token`, `client_id=pob`, loopback redirect (`:105, 131-135`), refresh-token flow (`:40-41`), API base `https://api.pathofexile.com` (`:22`).

The loopback listener is currently `src/LaunchServer.lua` running via luasocket as a long-lived subscript — binds `localhost:49082/49083/49084`, serves a hardcoded HTML page, reads `code`+`state` back. → `System.Net.HttpListener`.

**Hand-roll the OAuth.** It is ~200 LOC (`RandomNumberGenerator` + `SHA256` + `HttpListener`). Full IdentityModel/MSAL is heavy for one non-standard provider.

**Trade API** — `src/Classes/TradeQuery.lua` (1,415), `TradeQueryGenerator.lua` (1,609), `TradeQueryRequests.lua` (515), `TradeQueryRateLimiter.lua` (285), `TradeHelpers.lua` (623).
- Endpoints: `/api/trade/data/stats` (`TradeQueryGenerator.lua:380`), `/api/trade2/data/stats`.
- `src/Data/TradeSiteStats.lua` (92,095 LOC) and `QueryMods.lua` (67,465) are **lazy-loaded** — keep them lazy. `QueryMods` has a network fallback that regenerates it from GGG's stats API.
- **The rate limiter has a real algorithm in it** — port the logic. Use Polly for the retry/circuit-breaker scaffolding around it, not instead of it. The trade API returns `X-Rate-Limit-*` headers and is aggressive.

## Gotchas
`TradeQuery.lua` builds its own controls — it is mixed UI and logic. Extract the logic here; the UI is ticket 41.

## Acceptance
Character import from a live account works; a trade query round-trips.

## Libraries
`System.Net.HttpListener`, `System.Security.Cryptography`, Polly, Refit (**only** for the PoE API — not for build sites, see ticket 30).
