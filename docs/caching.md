# Caching + rate limiting

The `rails-parity` **P4.3** batteries item. Unlike jobs (P4.1) and email
(P4.2), every piece here is **first-party** — `HybridCache`, `OutputCache`,
and `AddRateLimiter` all ship in ASP.NET Core / `Microsoft.Extensions.*`, no
third-party library or seam decision needed.

## Decision: in-memory only, scoped to the new Listings API

- **`HybridCache`** (`Microsoft.Extensions.Caching.Hybrid`, .NET 9+) — an
  in-process cache with a first-party `IDistributedCache` backplane story
  built in, so adopting it now costs nothing later. **In-memory only** here;
  a Redis-backed distributed cache is **(vNext)** — add it only when the app
  runs more than one instance (per the existing scoping decision in
  `rails-parity-plan.md`, it then also becomes the SignalR backplane and Data
  Protection key store).
- **Scope:** the cache sits in front of `Endpoints/ListingsApiEndpoints.cs`
  only (`Features/Listings/ListingQueries.cs`), not threaded through the
  existing Blazor `Listings`/`ListingDetails` pages, which keep reading
  `AppDbContext` directly. The API is a new, self-contained surface built
  specifically to demonstrate this capability — same reasoning as P4.1/P4.2's
  worked examples being new, focused additions rather than a rewrite of
  existing pages. Threading caching through the Blazor UI too would mean
  invalidation-correctness risk across more call sites for no clear benefit:
  each user's Blazor Server circuit is already a live, stateful connection,
  not a stateless request repeatedly re-fetching the same data the way an API
  client would.
- **`OutputCache`** caches the *whole HTTP response*; `HybridCache` caches
  the *query result* underneath it. They're complementary, not redundant —
  an output-cache miss still hits `HybridCache` before the database.
- **`AddRateLimiter`** — a fixed-window limiter (`Api` policy: 5 requests /
  10 seconds, no queue) applied to the same API group. The natural target
  for abuse protection here is the new anonymous, unauthenticated read
  surface, not the existing authenticated write pages.

## How it's wired

```
Features/Listings/ListingQueries.cs   # HybridCache-backed reads + InvalidateAsync
Endpoints/ListingsApiEndpoints.cs     # GET /api/listings, GET /api/listings/{id}
```

- `Program.cs`: `AddHybridCache()`, `AddOutputCache(... .AddPolicy("Listings", ...))`
  (30s expiry), `AddRateLimiter(... .AddFixedWindowLimiter("Api", ...))`.
  Middleware order matters: `UseRateLimiter()` runs **before**
  `UseOutputCache()` — otherwise a cached response could bypass the limiter
  entirely, defeating its purpose. `MapListingsApiEndpoints()` applies both
  policies to the whole group via `.CacheOutput("Listings")` and
  `.RequireRateLimiting("Api")`.
- **Public, unauthenticated** — same "public to read" story the Blazor pages
  already have (P3.5); there is no write endpoint on this API.
- **Cache invalidation:** `ListingQueries.InvalidateAsync()` (tag-based —
  `HybridCache.RemoveByTagAsync("listings")`) is called from the three
  existing write points: `ListingCreate.razor`, `ListingEdit.razor`, and
  `Listings.razor`'s delete handler. Without this the API would serve stale
  data for up to the 30s output-cache expiry plus `HybridCache`'s own
  (unset — default) expiry.

## Adding a cached read elsewhere

1. Inject `HybridCache` (or a feature-specific wrapper like `ListingQueries`,
   if there's more than one query worth grouping) and call
   `cache.GetOrCreateAsync(key, factory, tags: [...])`.
2. Tag it, and call `RemoveByTagAsync` from every place that mutates the
   underlying data. A cache with no invalidation path is a bug waiting to
   ship stale data, not a shortcut.
3. `OutputCache`/`AddRateLimiter` are per-endpoint concerns (attribute or
   `.CacheOutput()`/`.RequireRateLimiting()` fluent calls) — they don't apply
   to Blazor Interactive Server pages, which aren't discrete cacheable HTTP
   responses the way a minimal API endpoint is.

## Testing

`ListingQueriesTests` (`tests/DotnetAgenticStarterkit.Tests/Features/Listings/`, P2.3
Testcontainers pattern) proves both halves against real Postgres: a second
read is served from the cache — not the database — until `InvalidateAsync`
is called, then reflects the change. **Don't** test `HybridCache`/
`OutputCache`/`AddRateLimiter`'s own internals — that's the framework's job.
The output-cache `Age` header and the rate limiter's `429` were verified
manually (below), not as automated tests, for the same reason P4.1/P4.2
didn't automate their own third-party-library-boundary checks.

## Verified end-to-end (2026-09-02)

Against the real Docker Postgres, a running `dotnet run`, real `curl`:

- `GET /api/listings` returns the real seeded listings as JSON.
- **Output cache:** first request → `Age: 0`; a repeated request one second
  later → `Age: 1` — the `Age` header only appears on a cache hit, so this is
  the framework's own signal that the second request never reached the
  endpoint handler.
- **Rate limiter:** 8 rapid requests against the 5-per-10-second `Api`
  policy → the first 5 return `200`, the remaining 3 return `429` — exactly
  the configured limit, not off by one in either direction.
- `ListingQueriesTests` (2 tests) pass. `dotnet test` → 22/22 (was 20), clean
  build with analyzers, `dotnet format --verify-no-changes` clean.
