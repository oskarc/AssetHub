using System.Security.Cryptography;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class NotificationPreferencesService(
    INotificationPreferencesRepository repo,
    INotificationUnsubscribeTokenService tokens,
    IAuditService audit,
    CurrentUser currentUser,
    ILogger<NotificationPreferencesService> logger) : INotificationPreferencesService
{
    public async Task<ServiceResult<NotificationPreferencesDto>> GetForCurrentUserAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        var prefs = await GetOrCreateAsync(currentUser.UserId, ct);
        return ToDto(prefs);
    }

    public async Task<ServiceResult<NotificationPreferencesDto>> UpdateForCurrentUserAsync(
        UpdateNotificationPreferencesDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return ServiceError.Forbidden("Authentication required.");

        // Validate cadence values up-front so bad input is rejected before any writes.
        foreach (var (category, settings) in dto.Categories)
        {
            if (!NotificationConstants.EmailCadences.IsValid(settings.EmailCadence))
                return ServiceError.Validation(
                    $"Invalid email cadence '{settings.EmailCadence}' for category '{category}'.",
                    new Dictionary<string, string>
                    {
                        [$"categories.{category}.emailCadence"] = "Must be 'instant', 'daily', or 'weekly'."
                    });
        }

        var prefs = await GetOrCreateAsync(currentUser.UserId, ct);

        // Merge: only update categories present in the DTO; leave others untouched.
        var changedKeys = new List<string>();
        foreach (var (category, settings) in dto.Categories)
        {
            var existing = prefs.Categories.TryGetValue(category, out var current) ? current : null;
            if (existing is null
                || existing.InApp != settings.InApp
                || existing.Email != settings.Email
                || existing.EmailCadence != settings.EmailCadence)
            {
                prefs.Categories[category] = new NotificationCategoryPrefs
                {
                    InApp = settings.InApp,
                    Email = settings.Email,
                    EmailCadence = settings.EmailCadence
                };
                changedKeys.Add(category);
            }
        }

        if (changedKeys.Count == 0)
            return ToDto(prefs);

        prefs.UpdatedAt = DateTime.UtcNow;
        await repo.UpdateAsync(prefs, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.PreferencesUpdated,
            Constants.ScopeTypes.UserPreferences,
            targetId: null,
            actorUserId: currentUser.UserId,
            new Dictionary<string, object>
            {
                ["category_changes"] = changedKeys
            },
            ct);

        logger.LogInformation(
            "Notification preferences updated for {UserId}: {CategoryCount} categories changed",
            currentUser.UserId, changedKeys.Count);

        return ToDto(prefs);
    }

    public async Task<NotificationCategoryPrefs> ResolveForUserAsync(
        string userId, string category, CancellationToken ct)
    {
        var prefs = await repo.GetByUserIdAsync(userId, ct);
        if (prefs is null)
            return DefaultPrefs;
        return prefs.Categories.TryGetValue(category, out var categoryPrefs)
            ? categoryPrefs
            : DefaultPrefs;
    }

    public Task<NotificationPreferences?> GetByUserIdAsync(string userId, CancellationToken ct)
        => repo.GetByUserIdAsync(userId, ct);

    public async Task<ServiceResult<UnsubscribeResult>> UnsubscribeFromCategoryAsync(
        string token, CancellationToken ct)
    {
        var payload = tokens.TryParseToken(token);
        if (payload is null)
        {
            logger.LogDebug("Unsubscribe attempted with invalid token");
            return new UnsubscribeResult(Applied: false, Category: null);
        }

        // Reject categories outside the known set so a minted-but-bogus
        // token can't bloat the JSONB Categories map with arbitrary keys.
        // Self-inflicted only (any valid token was minted by our server) but
        // avoids the edge case entirely.
        if (!NotificationConstants.Categories.All.Contains(payload.Category))
        {
            logger.LogDebug(
                "Unsubscribe rejected — unknown category {Category}",
                payload.Category);
            return new UnsubscribeResult(Applied: false, Category: payload.Category);
        }

        var prefs = await repo.GetByUserIdAsync(payload.UserId, ct);
        if (prefs is null || prefs.UnsubscribeTokenHash != payload.Stamp)
        {
            // Stamp mismatch — user rotated their token or the row was never
            // created. Return a neutral response so an attacker can't probe
            // for valid user ids.
            logger.LogDebug(
                "Unsubscribe stamp mismatch for user {UserId} / category {Category}",
                payload.UserId, payload.Category);
            return new UnsubscribeResult(Applied: false, Category: payload.Category);
        }

        var existing = prefs.Categories.TryGetValue(payload.Category, out var current) ? current : null;
        if (existing is not null && !existing.Email)
        {
            // Already unsubscribed — idempotent no-op.
            return new UnsubscribeResult(Applied: false, Category: payload.Category);
        }

        prefs.Categories[payload.Category] = new NotificationCategoryPrefs
        {
            InApp = existing?.InApp ?? DefaultPrefs.InApp,
            Email = false,
            EmailCadence = existing?.EmailCadence ?? DefaultPrefs.EmailCadence
        };
        prefs.UpdatedAt = DateTime.UtcNow;
        await repo.UpdateAsync(prefs, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.UnsubscribedViaEmail,
            Constants.ScopeTypes.UserPreferences,
            targetId: null,
            actorUserId: payload.UserId,
            new Dictionary<string, object>
            {
                ["category"] = payload.Category
            },
            ct);

        logger.LogInformation(
            "User {UserId} unsubscribed from category {Category} via email link",
            payload.UserId, payload.Category);

        return new UnsubscribeResult(Applied: true, Category: payload.Category);
    }

    private async Task<NotificationPreferences> GetOrCreateAsync(string userId, CancellationToken ct)
    {
        var existing = await repo.GetByUserIdAsync(userId, ct);
        if (existing is not null)
            return existing;

        var now = DateTime.UtcNow;
        var prefs = new NotificationPreferences
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Categories = new Dictionary<string, NotificationCategoryPrefs>(),
            UnsubscribeTokenHash = GenerateUnsubscribeTokenHash(),
            CreatedAt = now,
            UpdatedAt = now
        };

        return await repo.CreateAsync(prefs, ct);
    }

    /// <summary>
    /// Generates the per-user rotation stamp stored in
    /// <see cref="NotificationPreferences.UnsubscribeTokenHash"/>. This is NOT
    /// a secret — it is embedded in every Data-Protection-signed unsubscribe
    /// token and validated on redemption. Rotating it (future endpoint)
    /// invalidates every outstanding unsubscribe URL in one shot. 32 bytes
    /// of CSPRNG + SHA-256 gives a uniformly-random 64-char hex string.
    /// </summary>
    private static string GenerateUnsubscribeTokenHash()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static NotificationPreferencesDto ToDto(NotificationPreferences prefs)
    {
        var merged = new Dictionary<string, NotificationCategoryPrefsDto>();
        foreach (var category in NotificationConstants.Categories.All)
        {
            var source = prefs.Categories.TryGetValue(category, out var stored) ? stored : DefaultPrefs;
            merged[category] = new NotificationCategoryPrefsDto
            {
                InApp = source.InApp,
                Email = source.Email,
                EmailCadence = source.EmailCadence
            };
        }
        // Also surface any unknown categories already persisted (forward compat
        // if a future deployment adds a category the current code hasn't yet).
        foreach (var (category, source) in prefs.Categories)
        {
            if (merged.ContainsKey(category)) continue;
            merged[category] = new NotificationCategoryPrefsDto
            {
                InApp = source.InApp,
                Email = source.Email,
                EmailCadence = source.EmailCadence
            };
        }
        return new NotificationPreferencesDto { Categories = merged };
    }

    private static readonly NotificationCategoryPrefs DefaultPrefs = new()
    {
        InApp = true,
        Email = true,
        EmailCadence = NotificationConstants.EmailCadences.Instant
    };
}
