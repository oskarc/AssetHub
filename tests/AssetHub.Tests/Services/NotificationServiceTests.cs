using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wolverine;

namespace AssetHub.Tests.Services;

/// <summary>
/// Real-Postgres tests for <see cref="NotificationService"/>. Preferences are
/// mocked so each test can declare exactly what the user's in-app setting
/// resolves to for the target category.
/// </summary>
[Collection("Database")]
public class NotificationServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private NotificationRepository _repo = null!;
    private Mock<INotificationPreferencesService> _prefs = null!;
    private Mock<IMessageBus> _bus = null!;

    private const string UserId = "user-notif-001";
    private const string Category = NotificationConstants.Categories.Mention;

    public NotificationServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new NotificationRepository(_db);
        _prefs = new Mock<INotificationPreferencesService>();
        _bus = new Mock<IMessageBus>();
        // Default: in-app enabled, email enabled, instant — the typical shape.
        _prefs.Setup(p => p.ResolveForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCategoryPrefs { InApp = true, Email = true, EmailCadence = "instant" });
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private NotificationService ServiceFor(string userId)
        => new(_repo, _prefs.Object,
            new CurrentUser(userId, isSystemAdmin: false),
            TestCacheHelper.CreateHybridCache(),
            _bus.Object,
            NullLogger<NotificationService>.Instance);

    [Fact]
    public async Task CreateAsync_InAppEnabled_PersistsAndReturnsDto()
    {
        var svc = ServiceFor(UserId);

        var result = await svc.CreateAsync(UserId, Category, "Hello", body: "World",
            url: "/assets/abc", data: new Dictionary<string, object> { ["asset_id"] = "abc" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Hello", result.Value!.Title);
        Assert.Equal("World", result.Value.Body);
        Assert.Equal("/assets/abc", result.Value.Url);
        Assert.Equal("abc", result.Value.Data["asset_id"].ToString());
        Assert.False(result.Value.IsRead);
    }

    [Fact]
    public async Task CreateAsync_InAppDisabled_ReturnsNullAndPersistsNothing()
    {
        _prefs.Setup(p => p.ResolveForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCategoryPrefs { InApp = false, Email = false, EmailCadence = "instant" });

        var svc = ServiceFor(UserId + "-suppressed");

        var result = await svc.CreateAsync(UserId + "-suppressed", Category, "ignored", ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        // Nothing persisted for that user.
        Assert.Equal(0, await _repo.CountAsync(UserId + "-suppressed", unreadOnly: false, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_InstantEmailCadence_PublishesSendNotificationEmailCommand()
    {
        _prefs.Setup(p => p.ResolveForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCategoryPrefs { InApp = true, Email = true, EmailCadence = "instant" });
        var svc = ServiceFor(UserId + "-instant-email");

        var result = await svc.CreateAsync(UserId + "-instant-email", Category, "t",
            ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        _bus.Verify(b => b.PublishAsync(
                It.Is<SendNotificationEmailCommand>(c => c.NotificationId == result.Value!.Id),
                It.IsAny<DeliveryOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DailyEmailCadence_DoesNotPublishEmailCommand()
    {
        _prefs.Setup(p => p.ResolveForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCategoryPrefs { InApp = true, Email = true, EmailCadence = "daily" });
        var svc = ServiceFor(UserId + "-daily");

        await svc.CreateAsync(UserId + "-daily", Category, "t", ct: CancellationToken.None);

        _bus.Verify(b => b.PublishAsync(
                It.IsAny<SendNotificationEmailCommand>(),
                It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_EmailDisabled_DoesNotPublishEmailCommand()
    {
        _prefs.Setup(p => p.ResolveForUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCategoryPrefs { InApp = true, Email = false, EmailCadence = "instant" });
        var svc = ServiceFor(UserId + "-no-email");

        await svc.CreateAsync(UserId + "-no-email", Category, "t", ct: CancellationToken.None);

        _bus.Verify(b => b.PublishAsync(
                It.IsAny<SendNotificationEmailCommand>(),
                It.IsAny<DeliveryOptions>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_TruncatesOverlongTitleAndBody()
    {
        var svc = ServiceFor(UserId + "-truncate");

        var longTitle = new string('t', NotificationConstants.Limits.MaxTitleLength + 10);
        var longBody = new string('b', NotificationConstants.Limits.MaxBodyLength + 10);

        var result = await svc.CreateAsync(UserId + "-truncate", Category, longTitle, longBody,
            ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NotificationConstants.Limits.MaxTitleLength, result.Value!.Title.Length);
        Assert.Equal(NotificationConstants.Limits.MaxBodyLength, result.Value.Body!.Length);
    }

    [Fact]
    public async Task CreateAsync_MissingFields_ReturnsBadRequest()
    {
        var svc = ServiceFor(UserId);

        Assert.False((await svc.CreateAsync("", Category, "x", ct: CancellationToken.None)).IsSuccess);
        Assert.False((await svc.CreateAsync(UserId, "", "x", ct: CancellationToken.None)).IsSuccess);
        Assert.False((await svc.CreateAsync(UserId, Category, "", ct: CancellationToken.None)).IsSuccess);
    }

    [Theory]
    [InlineData("https://evil.com/phish")]
    [InlineData("http://other-origin")]
    [InlineData("javascript:alert(1)")]
    [InlineData("assets/abc")]
    public async Task CreateAsync_AbsoluteOrNonRootedUrl_ReturnsBadRequest(string url)
    {
        var svc = ServiceFor(UserId);

        var result = await svc.CreateAsync(UserId, Category, "t", url: url,
            ct: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Theory]
    [InlineData("/assets/abc")]
    [InlineData("/search")]
    [InlineData(null)]
    public async Task CreateAsync_RelativeOrNullUrl_Succeeds(string? url)
    {
        var svc = ServiceFor(UserId + "-url-" + (url is null ? "null" : url.Length.ToString()));

        var result = await svc.CreateAsync(UserId + "-url-" + (url is null ? "null" : url.Length.ToString()),
            Category, "t", url: url, ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ListForCurrentUserAsync_FiltersUnreadAndOrdersNewestFirst()
    {
        var userId = UserId + "-list";
        var svc = ServiceFor(userId);

        // Create 3 notifications: read, unread-old, unread-new.
        await svc.CreateAsync(userId, Category, "one", ct: CancellationToken.None);
        await Task.Delay(15);
        await svc.CreateAsync(userId, Category, "two", ct: CancellationToken.None);
        await Task.Delay(15);
        await svc.CreateAsync(userId, Category, "three", ct: CancellationToken.None);

        // Mark "two" as read.
        var all = await svc.ListForCurrentUserAsync(unreadOnly: false, skip: 0, take: 50, CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(3, all.Value!.TotalCount);
        var two = all.Value.Items.First(n => n.Title == "two");
        await svc.MarkReadAsync(two.Id, CancellationToken.None);

        // Unread-only returns "one" and "three", newest first.
        var unread = await svc.ListForCurrentUserAsync(unreadOnly: true, skip: 0, take: 50, CancellationToken.None);
        Assert.True(unread.IsSuccess);
        Assert.Equal(2, unread.Value!.TotalCount);
        Assert.Equal(2, unread.Value.UnreadCount);
        Assert.Equal("three", unread.Value.Items[0].Title);
        Assert.Equal("one", unread.Value.Items[1].Title);
    }

    [Fact]
    public async Task GetUnreadCountForCurrentUserAsync_ReflectsCreatesAndReads()
    {
        var userId = UserId + "-unread";
        var svc = ServiceFor(userId);

        var initial = await svc.GetUnreadCountForCurrentUserAsync(CancellationToken.None);
        Assert.True(initial.IsSuccess);
        Assert.Equal(0, initial.Value!.Count);

        await svc.CreateAsync(userId, Category, "x", ct: CancellationToken.None);
        await svc.CreateAsync(userId, Category, "y", ct: CancellationToken.None);

        var afterCreate = await svc.GetUnreadCountForCurrentUserAsync(CancellationToken.None);
        Assert.Equal(2, afterCreate.Value!.Count);

        await svc.MarkAllReadForCurrentUserAsync(CancellationToken.None);

        var afterRead = await svc.GetUnreadCountForCurrentUserAsync(CancellationToken.None);
        Assert.Equal(0, afterRead.Value!.Count);
    }

    [Fact]
    public async Task MarkReadAsync_OtherUsersNotification_ReturnsNotFound()
    {
        var owner = UserId + "-owner";
        var intruder = UserId + "-intruder";

        var svcOwner = ServiceFor(owner);
        var created = await svcOwner.CreateAsync(owner, Category, "private", ct: CancellationToken.None);
        Assert.NotNull(created.Value);

        var svcIntruder = ServiceFor(intruder);
        var result = await svcIntruder.MarkReadAsync(created.Value!.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_OwnNotification_RemovesRow()
    {
        var userId = UserId + "-delete";
        var svc = ServiceFor(userId);
        var created = await svc.CreateAsync(userId, Category, "doomed", ct: CancellationToken.None);

        var result = await svc.DeleteAsync(created.Value!.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await _repo.GetForOwnerAsync(created.Value.Id, userId, CancellationToken.None));
    }

    [Fact]
    public async Task Anonymous_AllListCallsReturnForbidden()
    {
        var svc = new NotificationService(_repo, _prefs.Object,
            CurrentUser.Anonymous, TestCacheHelper.CreateHybridCache(),
            _bus.Object,
            NullLogger<NotificationService>.Instance);

        Assert.False((await svc.ListForCurrentUserAsync(false, 0, 50, CancellationToken.None)).IsSuccess);
        Assert.False((await svc.GetUnreadCountForCurrentUserAsync(CancellationToken.None)).IsSuccess);
        Assert.False((await svc.MarkReadAsync(Guid.NewGuid(), CancellationToken.None)).IsSuccess);
        Assert.False((await svc.MarkAllReadForCurrentUserAsync(CancellationToken.None)).IsSuccess);
        Assert.False((await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None)).IsSuccess);
    }
}
