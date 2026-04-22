using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Real-Postgres tests for <see cref="NotificationPreferencesService"/>. Exercises
/// lazy-creation, merge-update behaviour, cadence validation, and audit emission.
/// </summary>
[Collection("Database")]
public class NotificationPreferencesServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private NotificationPreferencesRepository _repo = null!;
    private Mock<IAuditService> _audit = null!;

    private const string UserId = "user-notif-prefs-001";

    public NotificationPreferencesServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new NotificationPreferencesRepository(_db);
        _audit = new Mock<IAuditService>();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private NotificationPreferencesService ServiceFor(string userId)
        => new(_repo, _audit.Object, new CurrentUser(userId, isSystemAdmin: false),
            NullLogger<NotificationPreferencesService>.Instance);

    [Fact]
    public async Task GetForCurrentUserAsync_Anonymous_ReturnsForbidden()
    {
        var svc = new NotificationPreferencesService(_repo, _audit.Object,
            CurrentUser.Anonymous, NullLogger<NotificationPreferencesService>.Instance);

        var result = await svc.GetForCurrentUserAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetForCurrentUserAsync_FirstCall_LazilyCreatesWithDefaults()
    {
        var svc = ServiceFor(UserId);

        var result = await svc.GetForCurrentUserAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        // Every known category is populated with sensible defaults.
        foreach (var category in NotificationConstants.Categories.All)
        {
            Assert.True(result.Value!.Categories.ContainsKey(category), $"missing {category}");
            var prefs = result.Value.Categories[category];
            Assert.True(prefs.InApp);
            Assert.True(prefs.Email);
            Assert.Equal("instant", prefs.EmailCadence);
        }

        // Row persisted with a unique unsubscribe token hash.
        var stored = await _repo.GetByUserIdAsync(UserId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(64, stored!.UnsubscribeTokenHash.Length);
    }

    [Fact]
    public async Task UpdateForCurrentUserAsync_MergesOnlyProvidedCategoriesAndEmitsAudit()
    {
        var svc = ServiceFor(UserId + "-update");
        await svc.GetForCurrentUserAsync(CancellationToken.None); // force lazy creation

        var dto = new UpdateNotificationPreferencesDto
        {
            Categories = new Dictionary<string, NotificationCategoryPrefsDto>
            {
                [NotificationConstants.Categories.Mention] = new()
                {
                    InApp = true,
                    Email = false,          // opting out of email for mentions
                    EmailCadence = "instant"
                }
            }
        };

        var result = await svc.UpdateForCurrentUserAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Categories[NotificationConstants.Categories.Mention].Email);
        // Unmodified category keeps defaults.
        Assert.True(result.Value.Categories[NotificationConstants.Categories.SavedSearchDigest].Email);

        _audit.Verify(a => a.LogAsync(
            NotificationConstants.AuditEvents.PreferencesUpdated,
            Constants.ScopeTypes.UserPreferences,
            It.IsAny<Guid?>(),
            UserId + "-update",
            It.Is<Dictionary<string, object>?>(d =>
                d != null
                && d.ContainsKey("category_changes")
                && ((List<string>)d["category_changes"]).Contains(NotificationConstants.Categories.Mention)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateForCurrentUserAsync_InvalidCadence_ReturnsValidation()
    {
        var svc = ServiceFor(UserId + "-bad-cadence");

        var dto = new UpdateNotificationPreferencesDto
        {
            Categories = new Dictionary<string, NotificationCategoryPrefsDto>
            {
                [NotificationConstants.Categories.Mention] = new()
                {
                    InApp = true,
                    Email = true,
                    EmailCadence = "hourly"   // not allowed
                }
            }
        };

        var result = await svc.UpdateForCurrentUserAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateForCurrentUserAsync_SameAsStored_SkipsSecondAudit()
    {
        var svc = ServiceFor(UserId + "-no-change");

        // First update: stores explicit mention prefs (one audit).
        var dto = new UpdateNotificationPreferencesDto
        {
            Categories = new Dictionary<string, NotificationCategoryPrefsDto>
            {
                [NotificationConstants.Categories.Mention] = new()
                {
                    InApp = true,
                    Email = true,
                    EmailCadence = "instant"
                }
            }
        };
        await svc.UpdateForCurrentUserAsync(dto, CancellationToken.None);
        _audit.Invocations.Clear();

        // Second update with identical content: no-op, no audit.
        await svc.UpdateForCurrentUserAsync(dto, CancellationToken.None);

        _audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveForUserAsync_UnknownUser_ReturnsDefaults()
    {
        var svc = ServiceFor("any");
        var resolved = await svc.ResolveForUserAsync(
            "never-seen-user", NotificationConstants.Categories.Mention, CancellationToken.None);

        Assert.True(resolved.InApp);
        Assert.True(resolved.Email);
        Assert.Equal("instant", resolved.EmailCadence);
    }

    [Fact]
    public async Task ResolveForUserAsync_AfterUpdate_ReflectsStoredValue()
    {
        var svc = ServiceFor(UserId + "-resolve");
        var dto = new UpdateNotificationPreferencesDto
        {
            Categories = new Dictionary<string, NotificationCategoryPrefsDto>
            {
                [NotificationConstants.Categories.SavedSearchDigest] = new()
                {
                    InApp = false,
                    Email = false,
                    EmailCadence = "weekly"
                }
            }
        };
        await svc.UpdateForCurrentUserAsync(dto, CancellationToken.None);

        var resolved = await svc.ResolveForUserAsync(
            UserId + "-resolve", NotificationConstants.Categories.SavedSearchDigest, CancellationToken.None);

        Assert.False(resolved.InApp);
        Assert.False(resolved.Email);
        Assert.Equal("weekly", resolved.EmailCadence);
    }
}
