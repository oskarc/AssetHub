using System.Text.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Polls every <c>NotificationConstants.DigestSchedule.PollIntervalMinutes</c>
/// and, for each <see cref="SavedSearch"/> whose cadence is due, re-runs the
/// owner's saved search, notifies them about new matches via
/// <see cref="INotificationService"/>, and stamps <c>LastRunAt</c> /
/// <c>LastHighestSeenAssetId</c>.
///
/// Notifications created here ride the existing instant-email pipeline, so a
/// user with <c>EmailCadence = instant</c> for <c>saved_search_digest</c>
/// gets a separate email per digest. Batching into a true "weekly digest"
/// email is deferred (FOLLOW-UPS.md).
/// </summary>
public sealed class SavedSearchDigestBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SavedSearchDigestBackgroundService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(NotificationConstants.DigestSchedule.PollIntervalMinutes);

        logger.LogInformation(
            "Saved-search digest worker started. Interval: {Minutes} min",
            NotificationConstants.DigestSchedule.PollIntervalMinutes);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Saved-search digest tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISavedSearchRepository>();

        var saved = await repo.GetWithNotificationsEnabledAsync(ct);
        if (saved.Count == 0)
        {
            logger.LogDebug("Saved-search digest: nothing enrolled");
            return;
        }

        int processed = 0;
        int notified = 0;
        var now = DateTime.UtcNow;

        foreach (var search in saved)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsDue(search, now))
                continue;

            try
            {
                var didNotify = await ProcessOneAsync(scope.ServiceProvider, search, now, ct);
                processed++;
                if (didNotify) notified++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Saved-search digest failed for search {Id} (owner {Owner})",
                    search.Id, search.OwnerUserId);
            }
        }

        if (processed > 0)
            logger.LogInformation(
                "Saved-search digest tick completed: {Processed} processed, {Notified} notified",
                processed, notified);
        else
            logger.LogDebug("Saved-search digest: no searches due");
    }

    private static bool IsDue(SavedSearch search, DateTime now)
    {
        if (search.LastRunAt is null) return true;

        var age = now - search.LastRunAt.Value;
        return search.Notify switch
        {
            SavedSearchNotifyCadence.OnNewMatch => true,
            SavedSearchNotifyCadence.Daily => age.TotalHours >= NotificationConstants.DigestSchedule.DailyCooldownHours,
            SavedSearchNotifyCadence.Weekly => age.TotalDays >= NotificationConstants.DigestSchedule.WeeklyCooldownDays,
            _ => false
        };
    }

    private async Task<bool> ProcessOneAsync(
        IServiceProvider provider, SavedSearch search, DateTime runAt, CancellationToken ct)
    {
        var request = DeserializeRequest(search);
        if (request is null)
        {
            logger.LogWarning(
                "Saved-search {Id} has invalid RequestJson; skipping",
                search.Id);
            return false;
        }

        // Force created_desc so the first page is the newest matches and we
        // can cap scanning early once we cross LastRunAt.
        request.Sort = Constants.SortBy.CreatedDesc;
        request.Skip = 0;
        request.Take = Math.Max(request.Take, NotificationConstants.Limits.SavedSearchDigestMaxMatches + 1);
        request.Facets = null;

        var search_ = BuildOwnerSearch(provider, search.OwnerUserId);
        var result = await search_.SearchAsync(request, ct);
        if (!result.IsSuccess || result.Value is null)
        {
            logger.LogDebug(
                "Saved-search {Id} search returned {Error}",
                search.Id, result.Error?.Message ?? "no value");
            await StampRunOnlyAsync(provider, search.Id, runAt, ct);
            return false;
        }

        var watermark = search.LastRunAt ?? DateTime.MinValue;
        var newMatches = result.Value.Items
            .Where(a => a.CreatedAt > watermark)
            .ToList();

        if (newMatches.Count == 0)
        {
            await StampRunOnlyAsync(provider, search.Id, runAt, ct);
            return false;
        }

        var notifications = provider.GetRequiredService<INotificationService>();
        var audit = provider.GetRequiredService<IAuditService>();

        var preview = newMatches.Take(NotificationConstants.Limits.SavedSearchDigestMaxMatches)
            .Select(a => a.Title)
            .ToList();
        var extra = newMatches.Count - preview.Count;

        var title = newMatches.Count == 1
            ? $"New match for '{search.Name}': {newMatches[0].Title}"
            : $"{newMatches.Count} new matches for '{search.Name}'";

        var body = extra > 0
            ? string.Join("\n", preview) + $"\n+ {extra} more"
            : string.Join("\n", preview);

        await notifications.CreateAsync(
            userId: search.OwnerUserId,
            category: NotificationConstants.Categories.SavedSearchDigest,
            title: title,
            body: body,
            url: "/search",
            data: new Dictionary<string, object>
            {
                ["saved_search_id"] = search.Id,
                ["match_count"] = newMatches.Count
            },
            ct);

        var repo = provider.GetRequiredService<ISavedSearchRepository>();
        await repo.MarkRunAsync(search.Id, runAt, newMatches[0].Id, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.SavedSearchDigestSent,
            "saved_search",
            targetId: search.Id,
            actorUserId: null,
            new Dictionary<string, object>
            {
                ["cadence"] = search.Notify.ToDbString(),
                ["matches_delivered"] = newMatches.Count,
                ["owner_user_id"] = search.OwnerUserId
            },
            ct);

        return true;
    }

    private static async Task StampRunOnlyAsync(
        IServiceProvider provider, Guid savedSearchId, DateTime runAt, CancellationToken ct)
    {
        var repo = provider.GetRequiredService<ISavedSearchRepository>();
        await repo.MarkRunAsync(savedSearchId, runAt, highestSeenAssetId: null, ct);
    }

    private static AssetSearchService BuildOwnerSearch(IServiceProvider provider, string ownerUserId)
    {
        // AssetSearchService takes CurrentUser in its constructor and reads it
        // synchronously. Since the digest runs outside any HTTP scope, build
        // it by hand so we can impersonate the owner without mutating the
        // scope's CurrentUser registration.
        var db = provider.GetRequiredService<AssetHubDbContext>();
        var collectionRepo = provider.GetRequiredService<ICollectionRepository>();
        var logger = provider.GetRequiredService<ILogger<AssetSearchService>>();
        return new AssetSearchService(db, collectionRepo, new CurrentUser(ownerUserId, isSystemAdmin: false), logger);
    }

    private static AssetSearchRequest? DeserializeRequest(SavedSearch search)
    {
        if (string.IsNullOrWhiteSpace(search.RequestJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<AssetSearchRequest>(search.RequestJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
