---
applyTo: "src/AssetHub.Infrastructure/**"
description: "Use when creating or editing services, repositories, EF Core configuration, or DI registration in the AssetHub.Infrastructure project."
---
# Infrastructure Layer Conventions (AssetHub.Infrastructure)

AssetHub.Infrastructure implements all service interfaces and repository interfaces defined in Application. It references Application + Domain.

## Services

### Class structure
All services are `sealed class` with primary constructors:

```csharp
public sealed class ExampleService(
    IExampleRepository exampleRepo,
    CurrentUser currentUser,
    IOptions<AppSettings> appSettings,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<ExampleService> logger) : IExampleService
{
    private readonly ResiliencePipeline _minioPipeline = pipelineProvider.GetPipeline("minio");
}
```

### Separation of concern
Split large domains into focused services:
- **Commands**: `AssetService` — create, update, delete mutations.
- **Queries**: `AssetQueryService` — search, list, get-by-id reads.
- **Specialized I/O**: `AssetUploadService` — file upload pipeline.

### Polly resilience
Wrap external calls in named pipelines:
```csharp
await _minioPipeline.ExecuteAsync(async ct =>
{
    await _minioAdapter.PutObjectAsync(bucket, key, stream, ct);
}, cancellationToken);
```
Available pipelines: `"minio"` (retry 3×, circuit breaker 30s), `"clamav"` (retry 2×, circuit breaker 60s), `"smtp"` (retry 2×).

### Return values
Always return `ServiceResult<T>` — never throw for business errors:
```csharp
if (asset is null) return ServiceError.NotFound("Asset not found");
if (!_currentUser.IsSystemAdmin) return ServiceError.Forbidden("Not authorized");
return new AssetDto(asset);
```

### Logging
- `Information` for successful operations and summaries.
- `Warning` for non-critical failures (recoverable, per-item errors).
- `Error` for unrecoverable failures.
- Always use structured logging with named arguments: `logger.LogInformation("Processed {AssetId}", id)`.

## Repositories

### Class structure
Primary constructors with `AssetHubDbContext`, `HybridCache`, `ILogger<T>`:

```csharp
public sealed class ExampleRepository(
    AssetHubDbContext dbContext,
    HybridCache cache,
    ILogger<ExampleRepository> logger) : IExampleRepository
```

### Caching
Use `HybridCache` for hot-path lookups with keys from `CacheKeys` (see `caching-patterns.instructions.md` for full conventions):
```csharp
var data = await cache.GetOrCreateAsync(
    CacheKeys.Example(id),
    async ct => await dbContext.Examples.FirstOrDefaultAsync(e => e.Id == id, ct),
    new HybridCacheEntryOptions
    {
        Expiration = CacheKeys.ExampleTtl,
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    },
    tags: [CacheKeys.Tags.Example(id)],
    cancellationToken: ct);
```
Invalidate on mutations: `await cache.RemoveByTagAsync(CacheKeys.Tags.Example(id), ct)`.

### Query patterns
- **Pagination**: `query.Skip(skip).Take(take)` — always count first for total.
- **Tracking**: use `.AsNoTracking()` for read-only queries.
- **Includes**: conditional `if (includeAcls) query = query.Include(c => c.Acls)`.
- **Projections**: `.Select(a => new { ... })` for minimal data transfer.
- **Batch loading**: `.ToDictionary(a => a.Id)` to avoid N+1.

### JSONB queries
```csharp
// Search within JSONB Tags array
query = query.Where(a => a.Tags.Any(t => EF.Functions.ILike(t, $"%{search}%")));
```

## DbContext Configuration

All entity configuration is inline in `OnModelCreating()` — no separate configuration classes.

### JSONB columns
Always include column type, JSON serialization converter, and a **ValueComparer** (critical for change tracking):

```csharp
// List<string> (e.g., Tags)
entity.Property(e => e.Tags)
    .HasConversion(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
    .HasColumnType("jsonb")
    .Metadata.SetValueComparer(new ValueComparer<List<string>>(
        (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        c => c.ToList()));
```

### String enum storage
Enums are stored as strings via extension methods defined in `Domain/Entities/Enums.cs`:
```csharp
entity.Property(e => e.Status)
    .HasConversion(v => v.ToDbString(), v => v.ToAssetStatus())
    .HasMaxLength(50).IsRequired();
```

### Index naming
Convention: `idx_{entity}_{field(s)}` with `_unique` suffix for unique indexes:
```csharp
entity.HasIndex(e => new { e.EventType, e.CreatedAt })
    .HasDatabaseName("idx_audit_event_type_created");
```

### Foreign keys
Always specify `OnDelete` behavior explicitly:
```csharp
entity.HasOne(e => e.Collection)
    .WithMany(e => e.Acls)
    .HasForeignKey(e => e.CollectionId)
    .OnDelete(DeleteBehavior.Cascade);
```

## DI Registration

In `DependencyInjection/InfrastructureServiceExtensions.cs`:
- Repositories: `AddScoped<IRepo, Repo>()`
- Services: `AddScoped<IService, Service>()`
- Wolverine-handled services: register concrete first, then forward interface:
  ```csharp
  services.AddScoped<MediaProcessingService>();
  services.AddScoped<IMediaProcessingService>(sp => sp.GetRequiredService<MediaProcessingService>());
  ```
