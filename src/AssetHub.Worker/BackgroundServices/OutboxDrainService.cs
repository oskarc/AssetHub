using System.Text.Json;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Drains the OutboxMessages table to RabbitMQ. Producers enqueue rows in
/// the same SQL transaction as their source mutation; this service picks up
/// undispatched rows oldest-first, deserializes them, and calls
/// <see cref="IMessageBus.PublishAsync(object, DeliveryOptions?)"/> (D-2).
///
/// Failures bump AttemptCount + LastError so a poison message eventually
/// drops out of the work-set instead of blocking healthy traffic.
///
/// Multi-pod note: this implementation assumes a single Worker pod. The
/// Where/OrderBy/Take query has no SKIP LOCKED so two pods could read the
/// same row and double-publish; Wolverine handlers are already idempotent so
/// the failure mode is wasted work, not data corruption. Multi-pod
/// work-stealing is tracked as D-8.
/// </summary>
public sealed class OutboxDrainService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDrainService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;
    private const int MaxAttempts = 10;
    private const int MaxErrorLength = 1000;

    /// <summary>
    /// Allowlist guard for type resolution. The drainer only resolves message
    /// types from the AssetHub message namespace — even though the
    /// MessageType column is written by our own code, defense-in-depth means
    /// a hypothetical attacker with DB write access can't deserialize an
    /// arbitrary CLR type.
    /// </summary>
    private const string MessageNamespacePrefix = "AssetHub.Application.Messages.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox drain started. Interval: {Seconds}s, batch: {Batch}, max attempts: {MaxAttempts}",
            Interval.TotalSeconds, BatchSize, MaxAttempts);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await DrainOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox drain iteration failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DrainOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<DbContextProvider>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        List<OutboxMessage> batch;
        await using (var lease = await provider.AcquireAsync(ct))
        {
            batch = await lease.Db.OutboxMessages
                .Where(o => o.DispatchedAt == null && o.AttemptCount < MaxAttempts)
                .OrderBy(o => o.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        if (batch.Count == 0)
        {
            logger.LogDebug("Outbox drain: queue empty");
            return;
        }

        var dispatched = 0;
        var failed = 0;
        foreach (var row in batch)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryDispatchAsync(provider, bus, row, ct))
                dispatched++;
            else
                failed++;
        }

        logger.LogInformation(
            "Outbox drain: {Dispatched} dispatched, {Failed} failed (batch {BatchCount})",
            dispatched, failed, batch.Count);
    }

    private async Task<bool> TryDispatchAsync(
        DbContextProvider provider, IMessageBus bus, OutboxMessage row, CancellationToken ct)
    {
        try
        {
            var message = Deserialize(row);
            await bus.PublishAsync(message);
            await MarkDispatchedAsync(provider, row.Id, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(provider, row.Id, ex.Message, ct);
            logger.LogWarning(ex,
                "Outbox dispatch failed for {MessageType} (id {Id}, attempt {Attempt})",
                row.MessageType, row.Id, row.AttemptCount + 1);
            return false;
        }
    }

    private static object Deserialize(OutboxMessage row)
    {
        if (!row.MessageType.StartsWith(MessageNamespacePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Refusing to resolve outbox message type outside the AssetHub message namespace: {row.MessageType}");

        var type = Type.GetType(row.MessageType, throwOnError: false)
            ?? throw new InvalidOperationException($"Outbox message type not loadable: {row.MessageType}");

        var deserialized = JsonSerializer.Deserialize(row.PayloadJson, type, JsonOptions)
            ?? throw new InvalidOperationException($"Outbox message body deserialized to null: {row.MessageType}");

        return deserialized;
    }

    private static async Task MarkDispatchedAsync(DbContextProvider provider, Guid id, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var now = DateTime.UtcNow;
        await lease.Db.OutboxMessages
            .Where(o => o.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(o => o.DispatchedAt, now)
                .SetProperty(o => o.LastAttemptAt, now), ct);
    }

    private static async Task RecordFailureAsync(DbContextProvider provider, Guid id, string error, CancellationToken ct)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var truncated = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        var now = DateTime.UtcNow;
        await lease.Db.OutboxMessages
            .Where(o => o.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(o => o.AttemptCount, o => o.AttemptCount + 1)
                .SetProperty(o => o.LastAttemptAt, now)
                .SetProperty(o => o.LastError, truncated), ct);
    }
}
