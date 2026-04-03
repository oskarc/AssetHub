---
applyTo: "src/AssetHub.Worker/**"
description: "Use when creating or editing Wolverine message handlers or background services in the AssetHub.Worker project."
---
# Worker Conventions (AssetHub.Worker)

AssetHub.Worker is a composition root that runs Wolverine message handlers (via RabbitMQ) and `IHostedService` background services. It shares infrastructure with the API via `AddSharedInfrastructure()`.

## Host Setup
- Uses `Host.CreateDefaultBuilder()` with `.UseWolverine()` (not WebApplicationBuilder — no HTTP pipeline).
- Registers `IHttpContextAccessor` as singleton returning null (no HTTP context in handlers).
- Auto-migrates the database on startup (configurable).

## Message Handlers

Wolverine auto-discovers public `HandleAsync()` methods. Handlers live in `Handlers/`.

```csharp
public sealed class ProcessImageHandler(
    IMediaProcessingService mediaProcessingService,
    ILogger<ProcessImageHandler> logger)
{
    public async Task<object[]> HandleAsync(ProcessImageCommand command, CancellationToken ct)
    {
        // Process image, return events
        return [new AssetProcessingCompletedEvent(command.AssetId)];
    }
}
```

**Key rules:**
- Primary constructor with direct service injection (Wolverine manages scoping).
- Method must be `public async Task HandleAsync(TCommand command, CancellationToken ct)`.
- Return `object[]` to publish response events, or `Task` for void handlers.
- Commands and events are defined in `AssetHub.Application/Messages/`.

### Queues
- **Listens to:** `process-image`, `process-video`, `build-zip`
- **Publishes to:** `asset-processing-completed`, `asset-processing-failed`
- Auto-retry with exponential backoff (1s → 2s → 5s → 10s → 30s).
- Queues are auto-provisioned on startup.

## Background Services (IHostedService)

Recurring maintenance tasks use `BackgroundService` with `PeriodicTimer`. These live in `BackgroundServices/`.

```csharp
public sealed class ExampleCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExampleCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IExampleRepository>();
            // Cleanup logic
        }
    }
}
```

**Key rules:**
- Primary constructor with `IServiceScopeFactory` + `ILogger<T>`.
- Create a scope per iteration to resolve scoped services (DbContext, repos).
- Use `PeriodicTimer` for scheduling — not `Thread.Sleep` or `Task.Delay` loops.
- Never inject scoped services directly — always resolve from the scope.

## Error Handling

### Per-item resilience (batch processing in background services)
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

### Message handlers
Wolverine auto-discovers handlers — no explicit registration needed. Just create a public class with a `HandleAsync` method in the `Handlers/` directory.

### Background services in `Program.cs`
```csharp
services.AddHostedService<ExampleCleanupService>();
```

## Logging Conventions
- `Information`: handler/service start, completion summary.
- `Debug`: per-batch progress, "nothing to do" outcomes.
- `Warning`: per-item failures, cancellation.
- Always include counts: `"Processed {Count} of {Total} items"`.
