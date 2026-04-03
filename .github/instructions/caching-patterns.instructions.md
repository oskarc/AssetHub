---
applyTo: "src/AssetHub.Infrastructure/**"
description: "Use when implementing caching, cache invalidation, or HybridCache usage in AssetHub services or repositories."
---
# Caching Conventions

AssetHub uses **HybridCache** (L1 in-memory + L2 Redis). All cache configuration is centralized in `Application/CacheKeys.cs`.

## Adding a New Cache Key

1. Add a private prefix constant in `CacheKeys`.
2. Add a `public static readonly TimeSpan` TTL field with XML doc.
3. Add a `public static string` builder method.
4. If group invalidation is needed, add a tag in `CacheKeys.Tags`.

```csharp
// In CacheKeys.cs:
private const string ExamplePrefix = "example:";
public static readonly TimeSpan ExampleTtl = TimeSpan.FromMinutes(5);
public static string Example(Guid id) => $"{ExamplePrefix}{id}";

public static class Tags
{
    public static string Example(Guid id) => $"example:{id}";
}
```

### Rules
- **All keys' prefixes** and **all TTLs** must be defined in `CacheKeys` — never use inline strings or durations.
- **Short TTLs for volatile data**: collection counts (1min), dashboard (2min), users list (30s).
- **Longer TTLs for stable data**: user names (10min), collection names (10min).

## Using HybridCache

```csharp
// Read-through pattern
var data = await _cache.GetOrCreateAsync(
    CacheKeys.Example(id),
    async ct => await _repo.GetByIdAsync(id, ct),
    new HybridCacheEntryOptions
    {
        Expiration = CacheKeys.ExampleTtl,
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    },
    tags: [CacheKeys.Tags.Example(id)],
    cancellationToken: ct);
```

## Invalidation

Prefer **tag-based invalidation** over per-key removal:

```csharp
// GOOD: Invalidate by tag (clears all related entries)
await _cache.RemoveByTagAsync(CacheKeys.Tags.Example(id), ct);

// OK: Remove a specific key when tag isn't applicable
await _cache.RemoveAsync(CacheKeys.Example(id), ct);

// BAD: Forgetting to invalidate after mutations
await _repo.UpdateAsync(entity, ct);
// Missing: cache invalidation here
```

Always invalidate after create, update, and delete operations.

## What MUST NOT Be Cached

- **Authorization roles/ACL lookups** — these use request-scoped `Dictionary` in `CollectionAuthorizationService`. Global caching creates stale-permission windows where a user retains access after being removed.
- **Security tokens or passwords** — never store in any cache layer.
- **Presigned URLs** — cached in `MinIOAdapter` with variable TTL (75% of URL expiry). Don't add a separate cache layer on top.
