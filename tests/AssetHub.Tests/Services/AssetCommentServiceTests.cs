using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class AssetCommentServiceTests
{
    private readonly Mock<IAssetCommentRepository> _repo = new();
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<ICollectionAuthorizationService> _authService = new();
    private readonly Mock<IUserLookupService> _userLookup = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IWebhookEventPublisher> _webhooks = new();
    private readonly Mock<IAuditService> _audit = new();

    private const string AuthorId = "user-alice";
    private const string OtherUserId = "user-bob";
    private const string AdminId = "user-admin";

    private AssetCommentService CreateService(string userId = AuthorId, bool isAdmin = false)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new AssetCommentService(
            _repo.Object,
            _assetRepo.Object,
            _assetCollectionRepo.Object,
            _authService.Object,
            _userLookup.Object,
            _notifications.Object,
            _webhooks.Object,
            _audit.Object,
            currentUser,
            NullLogger<AssetCommentService>.Instance);
    }

    private void SetupAccess(Guid assetId, string role)
    {
        var cid = Guid.NewGuid();
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
        _authService.Setup(a => a.FilterAccessibleAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), role, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
    }

    private void SetupNoAccess(Guid assetId)
    {
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });
        _authService.Setup(a => a.FilterAccessibleAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private static Asset MakeAsset(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Title = "test-asset",
        OriginalObjectKey = "originals/test.jpg",
        AssetType = AssetType.Image,
        ContentType = "image/jpeg",
        SizeBytes = 1,
        Sha256 = "h",
        Status = AssetStatus.Ready,
        CreatedAt = DateTime.UtcNow,
        CreatedByUserId = AuthorId
    };

    private static AssetComment MakeComment(
        Guid assetId, string authorId = AuthorId, Guid? parent = null, string body = "hello")
        => new()
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            AuthorUserId = authorId,
            Body = body,
            MentionedUserIds = new List<string>(),
            ParentCommentId = parent,
            CreatedAt = DateTime.UtcNow
        };

    // ── ListForAssetAsync ───────────────────────────────────────────

    [Fact]
    public async Task ListForAssetAsync_Anonymous_ReturnsForbidden()
    {
        var svc = new AssetCommentService(
            _repo.Object, _assetRepo.Object, _assetCollectionRepo.Object, _authService.Object,
            _userLookup.Object, _notifications.Object, _webhooks.Object, _audit.Object,
            CurrentUser.Anonymous, NullLogger<AssetCommentService>.Instance);

        var result = await svc.ListForAssetAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ListForAssetAsync_NoAccessToAsset_ReturnsForbidden()
    {
        var assetId = Guid.NewGuid();
        SetupNoAccess(assetId);
        var svc = CreateService();

        var result = await svc.ListForAssetAsync(assetId, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ListForAssetAsync_WithAccess_ReturnsComments()
    {
        var assetId = Guid.NewGuid();
        SetupAccess(assetId, RoleHierarchy.Roles.Viewer);
        _repo.Setup(r => r.ListByAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetComment> { MakeComment(assetId), MakeComment(assetId) });
        var svc = CreateService();

        var result = await svc.ListForAssetAsync(assetId, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutContributor_ReturnsForbidden()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));
        // Viewer-only access → cannot comment.
        SetupAccess(assetId, RoleHierarchy.Roles.Viewer);
        _authService.Setup(a => a.FilterAccessibleAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), RoleHierarchy.Roles.Contributor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        var svc = CreateService();

        var result = await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "hi" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
        _repo.Verify(r => r.CreateAsync(It.IsAny<AssetComment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_AssetNotFound_ReturnsNotFound()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);
        var svc = CreateService();

        var result = await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "hi" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_HappyPath_PersistsAuditsAndNotifiesMentions()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));
        SetupAccess(assetId, RoleHierarchy.Roles.Contributor);

        _userLookup.Setup(u => u.GetUserIdByUsernameAsync("bob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherUserId);

        AssetComment? saved = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<AssetComment>(), It.IsAny<CancellationToken>()))
            .Callback<AssetComment, CancellationToken>((c, _) => saved = c)
            .ReturnsAsync((AssetComment c, CancellationToken _) => c);

        var svc = CreateService();
        var result = await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "Hey @bob please review" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Contains(OtherUserId, saved!.MentionedUserIds);

        _notifications.Verify(n => n.CreateAsync(
                OtherUserId,
                NotificationConstants.Categories.Mention,
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.CommentCreated,
                Constants.ScopeTypes.Comment, It.IsAny<Guid?>(), AuthorId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.CommentMentionDelivered,
                Constants.ScopeTypes.Comment, It.IsAny<Guid?>(), AuthorId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_MentionsSelf_DoesNotNotifySelf()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));
        SetupAccess(assetId, RoleHierarchy.Roles.Contributor);
        _userLookup.Setup(u => u.GetUserIdByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorId);

        AssetComment? saved = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<AssetComment>(), It.IsAny<CancellationToken>()))
            .Callback<AssetComment, CancellationToken>((c, _) => saved = c)
            .ReturnsAsync((AssetComment c, CancellationToken _) => c);

        var svc = CreateService();
        await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "ping @alice" }, CancellationToken.None);

        Assert.DoesNotContain(AuthorId, saved!.MentionedUserIds);
        _notifications.Verify(n => n.CreateAsync(
                AuthorId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownMentionedUser_IsDroppedSilently()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));
        SetupAccess(assetId, RoleHierarchy.Roles.Contributor);
        _userLookup.Setup(u => u.GetUserIdByUsernameAsync("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        AssetComment? saved = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<AssetComment>(), It.IsAny<CancellationToken>()))
            .Callback<AssetComment, CancellationToken>((c, _) => saved = c)
            .ReturnsAsync((AssetComment c, CancellationToken _) => c);

        var svc = CreateService();
        var result = await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "hey @ghost" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(saved!.MentionedUserIds);
        _notifications.Verify(n => n.CreateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ReplyToMissingParent_ReturnsBadRequest()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));
        SetupAccess(assetId, RoleHierarchy.Roles.Contributor);
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AssetComment?)null);

        var svc = CreateService();
        var result = await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "hi", ParentCommentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_ReplyToReply_ReturnsBadRequest()
    {
        var assetId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));
        SetupAccess(assetId, RoleHierarchy.Roles.Contributor);
        var grandparentId = Guid.NewGuid();
        var parent = MakeComment(assetId, parent: grandparentId);
        _repo.Setup(r => r.GetByIdAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(parent);

        var svc = CreateService();
        var result = await svc.CreateAsync(assetId,
            new CreateAssetCommentDto { Body = "hi", ParentCommentId = parent.Id },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NonAuthor_ReturnsForbidden()
    {
        var assetId = Guid.NewGuid();
        var existing = MakeComment(assetId, authorId: OtherUserId);
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var svc = CreateService(AuthorId);

        var result = await svc.UpdateAsync(existing.Id,
            new UpdateAssetCommentDto { Body = "new" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_AddsNewMention_NotifiesOnlyNewlyAdded()
    {
        var assetId = Guid.NewGuid();
        var existing = MakeComment(assetId, body: "original");
        existing.MentionedUserIds = new List<string> { OtherUserId };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _assetRepo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(MakeAsset(assetId));

        _userLookup.Setup(u => u.GetUserIdByUsernameAsync("bob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OtherUserId);
        _userLookup.Setup(u => u.GetUserIdByUsernameAsync("carol", It.IsAny<CancellationToken>()))
            .ReturnsAsync("user-carol");

        var svc = CreateService(AuthorId);
        await svc.UpdateAsync(existing.Id,
            new UpdateAssetCommentDto { Body = "updated with @bob and @carol" },
            CancellationToken.None);

        // bob was already mentioned — no duplicate notification.
        _notifications.Verify(n => n.CreateAsync(
                OtherUserId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        // carol is new — notify once.
        _notifications.Verify(n => n.CreateAsync(
                "user-carol", NotificationConstants.Categories.Mention,
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Author_CanDelete()
    {
        var assetId = Guid.NewGuid();
        var existing = MakeComment(assetId, authorId: AuthorId);
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repo.Setup(r => r.DeleteAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var svc = CreateService(AuthorId);
        var result = await svc.DeleteAsync(existing.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.DeleteAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonAuthorNonAdmin_Forbidden()
    {
        var assetId = Guid.NewGuid();
        var existing = MakeComment(assetId, authorId: OtherUserId);
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var svc = CreateService(AuthorId);
        var result = await svc.DeleteAsync(existing.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_SystemAdmin_CanDeleteOthersComment()
    {
        var assetId = Guid.NewGuid();
        var existing = MakeComment(assetId, authorId: OtherUserId);
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repo.Setup(r => r.DeleteAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var svc = CreateService(AdminId, isAdmin: true);
        var result = await svc.DeleteAsync(existing.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.CommentDeleted,
                Constants.ScopeTypes.Comment, It.IsAny<Guid?>(), AdminId,
                It.Is<Dictionary<string, object>>(d => (bool)d["was_admin_delete"]),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
