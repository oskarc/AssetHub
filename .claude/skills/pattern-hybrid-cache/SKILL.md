---
name: pattern-hybrid-cache
description: A two-tier cache (in-process L1 + distributed L2) fronted by one centralized registry of keys, TTLs, and invalidation tags — so every cached read is declared in one place and invalidated by tag after writes. Use when adding a cached lookup, designing invalidation, or deciding whether something may be cached at all.
---

# Two-tier cache with a central key registry

## Principle (why)

Caching fails in two predictable ways: stale reads (a write happened but a cache somewhere didn't hear about it) and scattered policy (every call site invents its own key string and TTL, so nothing is auditable and invalidation misses entries). Both are solved by making the cache *declarative and centralized*: every cacheable read has a named entry — key shape, time-to-live, and invalidation tag — defined in one registry, and writes invalidate by tag rather than by hand-reconstructing keys.

The two tiers exist because the two failure modes of a *distributed* cache differ from an *in-process* one: L1 (in-memory) is fast but per-instance; L2 (distributed) is shared but slower. A hybrid cache gives each read L1 speed with L2 coherence, and the registry is what keeps the two from drifting.

## Pattern (what)

**One registry owns all cache policy.** A single static class holds, per cacheable concern:
1. a private key-prefix constant,
2. a TTL value (typed, documented with *why* that duration),
3. a key-builder method (`Key(id) => prefix + id`),
4. an invalidation tag (when group-invalidation is needed).

No call site writes a raw key string or a bare TTL — they call the registry.

**Reads go through get-or-create with the registry's policy:**
```csharp
var data = await cache.GetOrCreateAsync(
    Keys.Example(id),
    async ct => await repo.GetByIdAsync(id, ct),
    new HybridCacheEntryOptions { Expiration = Keys.ExampleTtl, LocalCacheExpiration = <short> },
    tags: [Keys.Tags.Example(id)],
    cancellationToken: ct);
```

**Writes invalidate by tag, every time:** after any create/update/delete, `RemoveByTagAsync(Keys.Tags.Example(id))`. Tag-based invalidation clears every entry in a group without reconstructing individual keys — the registry's tags are the unit of invalidation.

**A short L1 expiration under a longer L2** keeps per-instance reads fast while bounding cross-instance staleness to the L1 window.

## What must NOT be cached
- **Authorization data** (roles, ACLs, permissions). A cached permission is a stale-privilege window — use request-scoped memoization instead, which lives and dies with the request. (This is the single most dangerous thing to cache and the most tempting, because auth checks are hot.)
- **Secrets** (tokens, passwords, keys).
- **Values that already carry their own freshness contract** elsewhere (e.g. presigned URLs that are minted with an expiry by the layer that issues them) — caching them again double-bounds the lifetime and invites serving an expired one.

## Boundaries

- TTL is a *correctness* decision, not just performance: it's the maximum staleness the data tolerates. Document the reasoning at the TTL, not just the number.
- Tag invalidation is the default; reach for key-specific removal only when a write genuinely affects exactly one entry and no group.
