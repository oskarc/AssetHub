using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class AssetWorkflowServiceTests
{
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<IAssetWorkflowTransitionRepository> _transitionRepo = new();
    private readonly Mock<IAssetMetadataRepository> _metadataRepo = new();
    private readonly Mock<IMetadataSchemaQueryService> _schemaQuery = new();
    private readonly Mock<ICollectionAuthorizationService> _authService = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IAuditService> _audit = new();

    private const string AuthorId = "user-author";
    private const string ManagerId = "user-manager";
    private const string OtherUserId = "user-other";

    public AssetWorkflowServiceTests()
    {
        // Schema query returns empty by default — no required metadata to fill.
        _schemaQuery.Setup(s => s.GetApplicableAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchemaDto>());
        _metadataRepo.Setup(r => r.GetByAssetIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetMetadataValue>());
        _transitionRepo.Setup(r => r.ListByAssetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetWorkflowTransition>());
        _transitionRepo.Setup(r => r.CreateAsync(It.IsAny<AssetWorkflowTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AssetWorkflowTransition t, CancellationToken _) => t);
        // Default fallback: any role that isn't explicitly set up returns
        // empty (not accessible). Each test calls SetupAccess to override.
        _authService.Setup(a => a.FilterAccessibleAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private AssetWorkflowService CreateService(string userId = AuthorId, bool isAdmin = false)
        => new(_assetRepo.Object, _assetCollectionRepo.Object, _transitionRepo.Object,
               _metadataRepo.Object, _schemaQuery.Object, _authService.Object,
               _notifications.Object, _audit.Object,
               new CurrentUser(userId, isAdmin),
               NullLogger<AssetWorkflowService>.Instance);

    private static Asset MakeAsset(AssetWorkflowState state, string creator = AuthorId, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Title = "wf-asset",
            OriginalObjectKey = "k",
            AssetType = AssetType.Image,
            ContentType = "image/jpeg",
            SizeBytes = 1,
            Status = AssetStatus.Ready,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = creator,
            WorkflowState = state
        };

    private void SetupAccess(Guid assetId, params (string Role, bool Accessible)[] roles)
    {
        var cid = Guid.NewGuid();
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
        foreach (var (role, accessible) in roles)
        {
            _authService.Setup(a => a.FilterAccessibleAsync(
                    It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), role, It.IsAny<CancellationToken>()))
                .ReturnsAsync(accessible ? new List<Guid> { cid } : new List<Guid>());
        }
    }

    // ── Transition table coverage ───────────────────────────────────────

    [Fact]
    public async Task Submit_FromDraft_ByAuthor_Succeeds()
    {
        var asset = MakeAsset(AssetWorkflowState.Draft);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id, (RoleHierarchy.Roles.Viewer, true));

        var svc = CreateService(AuthorId);
        var result = await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("in_review", result.Value!.CurrentState);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WorkflowSubmitted,
                Constants.ScopeTypes.Asset, asset.Id, AuthorId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_FromDraft_ByNonAuthor_Forbidden()
    {
        var asset = MakeAsset(AssetWorkflowState.Draft, creator: AuthorId);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id, (RoleHierarchy.Roles.Viewer, true));

        var svc = CreateService(OtherUserId);
        var result = await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Submit_FromApproved_Returns409()
    {
        // Approved isn't a valid source state for submit — "Cannot 'submit'
        // from state 'approved'" conflict.
        var asset = MakeAsset(AssetWorkflowState.Approved);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id, (RoleHierarchy.Roles.Viewer, true));

        var svc = CreateService(AuthorId);
        var result = await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Submit_WithMissingRequiredMetadata_Returns400()
    {
        var asset = MakeAsset(AssetWorkflowState.Draft);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id, (RoleHierarchy.Roles.Viewer, true));

        var requiredField = new MetadataFieldDto
        {
            Id = Guid.NewGuid(), Key = "rights", Label = "Rights", Type = "text",
            Required = true, Searchable = false, Facetable = false, SortOrder = 0
        };
        _schemaQuery.Setup(s => s.GetApplicableAsync(It.IsAny<string?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchemaDto>
            {
                new() { Id = Guid.NewGuid(), Name = "core", Scope = "global", Version = 1,
                        CreatedAt = DateTime.UtcNow, CreatedByUserId = "sys",
                        Fields = new List<MetadataFieldDto> { requiredField } }
            });

        var svc = CreateService(AuthorId);
        var result = await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("metadata", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resubmit_FromRejected_ByAuthor_Succeeds()
    {
        var asset = MakeAsset(AssetWorkflowState.Rejected);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id, (RoleHierarchy.Roles.Viewer, true));

        var svc = CreateService(AuthorId);
        var result = await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("in_review", result.Value!.CurrentState);
    }

    [Fact]
    public async Task Approve_FromInReview_ByManager_Succeeds_NotifiesAuthor()
    {
        var asset = MakeAsset(AssetWorkflowState.InReview);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id,
            (RoleHierarchy.Roles.Viewer, true),
            (RoleHierarchy.Roles.Manager, true));

        var svc = CreateService(ManagerId);
        var result = await svc.ApproveAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("approved", result.Value!.CurrentState);

        _notifications.Verify(n => n.CreateAsync(
                AuthorId,
                NotificationConstants.Categories.WorkflowTransition,
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WorkflowApproved,
                Constants.ScopeTypes.Asset, asset.Id, ManagerId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Approve_FromInReview_ByNonManager_Forbidden()
    {
        var asset = MakeAsset(AssetWorkflowState.InReview);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id,
            (RoleHierarchy.Roles.Viewer, true),
            (RoleHierarchy.Roles.Manager, false));

        var svc = CreateService(OtherUserId);
        var result = await svc.ApproveAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Reject_FromInReview_ByManager_SucceedsAndCapturesReason()
    {
        var asset = MakeAsset(AssetWorkflowState.InReview);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id,
            (RoleHierarchy.Roles.Viewer, true),
            (RoleHierarchy.Roles.Manager, true));

        AssetWorkflowTransition? captured = null;
        _transitionRepo.Setup(r => r.CreateAsync(It.IsAny<AssetWorkflowTransition>(), It.IsAny<CancellationToken>()))
            .Callback<AssetWorkflowTransition, CancellationToken>((t, _) => captured = t)
            .ReturnsAsync((AssetWorkflowTransition t, CancellationToken _) => t);

        var svc = CreateService(ManagerId);
        var result = await svc.RejectAsync(asset.Id,
            new WorkflowRejectDto { Reason = "Resolution too low" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("rejected", result.Value!.CurrentState);
        Assert.Equal("Resolution too low", captured!.Reason);
    }

    [Fact]
    public async Task Publish_FromApproved_ByManager_Succeeds()
    {
        var asset = MakeAsset(AssetWorkflowState.Approved);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id,
            (RoleHierarchy.Roles.Viewer, true),
            (RoleHierarchy.Roles.Manager, true));

        var svc = CreateService(ManagerId);
        var result = await svc.PublishAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("published", result.Value!.CurrentState);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WorkflowPublished,
                Constants.ScopeTypes.Asset, asset.Id, ManagerId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Unpublish_FromPublished_ByManager_Succeeds()
    {
        var asset = MakeAsset(AssetWorkflowState.Published);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id,
            (RoleHierarchy.Roles.Viewer, true),
            (RoleHierarchy.Roles.Manager, true));

        var svc = CreateService(ManagerId);
        var result = await svc.UnpublishAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("approved", result.Value!.CurrentState);
    }

    [Fact]
    public async Task Anonymous_AllActionsReturnForbidden()
    {
        var asset = MakeAsset(AssetWorkflowState.Draft);
        var svc = new AssetWorkflowService(
            _assetRepo.Object, _assetCollectionRepo.Object, _transitionRepo.Object,
            _metadataRepo.Object, _schemaQuery.Object, _authService.Object,
            _notifications.Object, _audit.Object,
            CurrentUser.Anonymous,
            NullLogger<AssetWorkflowService>.Instance);

        Assert.False((await svc.GetAsync(asset.Id, CancellationToken.None)).IsSuccess);
        Assert.False((await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None)).IsSuccess);
        Assert.False((await svc.ApproveAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None)).IsSuccess);
        Assert.False((await svc.RejectAsync(asset.Id, new WorkflowRejectDto { Reason = "x" }, CancellationToken.None)).IsSuccess);
        Assert.False((await svc.PublishAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None)).IsSuccess);
        Assert.False((await svc.UnpublishAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Get_SurfacesAvailableActions_ForCurrentStateAndRole()
    {
        // Author on a Rejected asset: Submit (resubmit) is available.
        var asset = MakeAsset(AssetWorkflowState.Rejected);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id,
            (RoleHierarchy.Roles.Viewer, true),
            (RoleHierarchy.Roles.Manager, false));

        var svc = CreateService(AuthorId);
        var result = await svc.GetAsync(asset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(WorkflowActions.Submit, result.Value!.AvailableActions);
        Assert.DoesNotContain(WorkflowActions.Approve, result.Value.AvailableActions);
    }

    [Fact]
    public async Task ActorEqualsAuthor_DoesNotNotifySelf()
    {
        // Author is also the actor (e.g. resubmitting their own asset).
        var asset = MakeAsset(AssetWorkflowState.Rejected, creator: AuthorId);
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        SetupAccess(asset.Id, (RoleHierarchy.Roles.Viewer, true));

        var svc = CreateService(AuthorId);
        await svc.SubmitAsync(asset.Id, new WorkflowActionDto(), CancellationToken.None);

        _notifications.Verify(n => n.CreateAsync(
                AuthorId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
