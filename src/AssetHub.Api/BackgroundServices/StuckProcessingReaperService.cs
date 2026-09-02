using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Api.BackgroundServices;

/// <summary>
/// Re-enqueues assets left stuck in <see cref="AssetStatus.Processing"/>.
/// </summary>
/// <remarks>
/// Introduced with contract-026, which replaced RabbitMQ with an in-process
/// channel. RabbitMQ redelivered a message whose handler crashed mid-flight; an
/// in-memory channel does not, so a media asset could sit in Processing forever.
/// This reaper is the recovery: any asset in Processing past a grace window is
/// re-scheduled through the same outbox path the upload used, and its UpdatedAt is
/// bumped so it is not re-reaped until the window passes again (backoff = the
/// grace window). It is provider-agnostic — it recovers a stuck asset whatever the
/// cause, not only a lost channel message.
/// </remarks>
public sealed class StuckProcessingReaperService(
    IServiceScopeFactory scopeFactory,
    ILogger<StuckProcessingReaperService> logger) : BackgroundService
{
    // A healthy image processes in well under a minute; five minutes in Processing
    // means the message was almost certainly lost, not merely slow.
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await ReapAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Stuck-processing reaper failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReapAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<DbContextProvider>();
        var media = scope.ServiceProvider.GetRequiredService<IMediaProcessingService>();

        var cutoff = DateTime.UtcNow - Grace;
        List<Asset> stuck;
        await using (var lease = await provider.AcquireAsync(ct))
        {
            stuck = await lease.Db.Assets
                .Where(a => a.Status == AssetStatus.Processing && a.UpdatedAt < cutoff && a.DeletedAt == null)
                .Take(50)
                .ToListAsync(ct);
        }

        if (stuck.Count == 0) return;

        logger.LogWarning("Reaping {Count} asset(s) stuck in Processing past {Grace}", stuck.Count, Grace);

        foreach (var asset in stuck)
        {
            // Bump UpdatedAt first so a failed re-run backs off by the grace window
            // instead of re-enqueueing every cycle.
            await using (var lease = await provider.AcquireAsync(ct))
            {
                await lease.Db.Assets
                    .Where(a => a.Id == asset.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
            }

            await media.ScheduleProcessingAsync(
                asset.Id, asset.AssetType.ToDbString(), asset.OriginalObjectKey, ct);
            logger.LogInformation("Re-enqueued stuck asset {AssetId} for processing", asset.Id);
        }
    }
}
