---
applyTo: "src/AssetHub.Worker/**"
description: "Use when creating or editing Hangfire background jobs in the AssetHub.Worker project."
---
# Worker Conventions (AssetHub.Worker)

AssetHub.Worker is a composition root that runs Hangfire background jobs. It shares infrastructure with the API via `AddSharedInfrastructure()`.

## Host Setup
- Uses `Host.CreateDefaultBuilder()` (not WebApplicationBuilder — no HTTP pipeline).
- Registers `IHttpContextAccessor` as singleton returning null (no HTTP context in jobs).
- Auto-migrates the database on startup (separate from API migration).

## Job Class Structure

All jobs follow this pattern:

```csharp
public class ExampleJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ExampleJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExampleRepository>();

        // Job logic here
    }
}
```

**Key rules:**
- Primary constructor with `IServiceScopeFactory` + `ILogger<T>`.
- Create a scope in `ExecuteAsync()` to resolve scoped services (DbContext, repos).
- Method must be `public async Task ExecuteAsync(CancellationToken ct = default)`.
- Never inject scoped services directly — always resolve from the scope.

## Error Handling

### Per-item resilience (batch processing)
```csharp
foreach (var item in items)
{
    try
    {
        await ProcessItemAsync(item, ct);
        processed++;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to process {ItemId}", item.Id);
        // Continue — one failure doesn't stop the batch
    }
}
```

### Long-running loops with cancellation
```csharp
int deleted;
do
{
    ct.ThrowIfCancellationRequested();
    deleted = await repo.DeleteBatchAsync(cutoff, BatchSize, ct);
    totalDeleted += deleted;
} while (deleted >= BatchSize);
```

### Catch `OperationCanceledException` at top level
```csharp
catch (OperationCanceledException)
{
    logger.LogWarning("Job cancelled after processing {Count} items", totalDeleted);
}
```

## Registration

### 1. Register job class as scoped in `Program.cs`
```csharp
services.AddScoped<ExampleJob>();
```

### 2. Register recurring schedule after host build
```csharp
var recurringJobs = host.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<ExampleJob>(
    "example-job",                              // Unique job ID (kebab-case)
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.Daily(3, 0),                           // UTC schedule
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
```

**Rules:**
- Job ID must be globally unique, use kebab-case.
- Always set `TimeZone = TimeZoneInfo.Utc`.
- Pass `CancellationToken.None` in the lambda (Hangfire provides its own).
- `AddOrUpdate` is idempotent — safe to call on every startup.

## Logging Conventions
- `Information`: job start, job completion summary.
- `Debug`: per-batch progress, "nothing to do" outcomes.
- `Warning`: per-item failures, cancellation.
- Always include counts: `"Processed {Count} of {Total} items"`.

## Hangfire Queues
Two queues configured: `"default"` and `"media-processing"`.
- Cleanup/retention jobs use the default queue.
- Media processing jobs use `"media-processing"` queue.
