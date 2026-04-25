using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for the workflow flow: asset/transition/metadata repos + schema query + collection auth + notifications + webhook publisher + audit + UnitOfWork + scoped CurrentUser + logger. Bundling them obscures intent; UnitOfWork was added to wrap action+audit atomically (A-4).")]
public sealed class AssetWorkflowService(
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    IAssetWorkflowTransitionRepository transitionRepo,
    IAssetMetadataRepository metadataRepo,
    IMetadataSchemaQueryService schemaQuery,
    ICollectionAuthorizationService authService,
    INotificationService notifications,
    IWebhookEventPublisher webhooks,
    IAuditService audit,
    IUnitOfWork uow,
    CurrentUser currentUser,
    ILogger<AssetWorkflowService> logger) : IAssetWorkflowService
{
    public async Task<ServiceResult<AssetWorkflowResponseDto>> GetAsync(Guid assetId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found.");

        if (!await CanAccessAssetAsync(assetId, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var history = await transitionRepo.ListByAssetAsync(assetId, ct);
        var available = await ComputeAvailableActionsAsync(asset, ct);
        return BuildResponse(asset, history, available);
    }

    public Task<ServiceResult<AssetWorkflowResponseDto>> SubmitAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct)
        => TransitionAsync(assetId, WorkflowActions.Submit, dto.Reason, ct);

    public Task<ServiceResult<AssetWorkflowResponseDto>> ApproveAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct)
        => TransitionAsync(assetId, WorkflowActions.Approve, dto.Reason, ct);

    public Task<ServiceResult<AssetWorkflowResponseDto>> RejectAsync(
        Guid assetId, WorkflowRejectDto dto, CancellationToken ct)
        => TransitionAsync(assetId, WorkflowActions.Reject, dto.Reason, ct);

    public Task<ServiceResult<AssetWorkflowResponseDto>> PublishAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct)
        => TransitionAsync(assetId, WorkflowActions.Publish, dto.Reason, ct);

    public Task<ServiceResult<AssetWorkflowResponseDto>> UnpublishAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct)
        => TransitionAsync(assetId, WorkflowActions.Unpublish, dto.Reason, ct);

    // ── Core transition engine ──────────────────────────────────────────

    private async Task<ServiceResult<AssetWorkflowResponseDto>> TransitionAsync(
        Guid assetId, string action, string? reason, CancellationToken ct)
    {
        var preflight = await PreflightAsync(assetId, action, ct);
        if (!preflight.IsSuccess) return preflight.Error!;
        var (asset, plan) = preflight.Value!;

        var gateError = await CheckTransitionGatesAsync(asset, plan, ct);
        if (gateError is not null) return gateError;

        var now = DateTime.UtcNow;
        var from = asset.WorkflowState;
        var to = plan.ToState;

        // State change + transition row + audit run in one transaction
        // (A-4) — a torn write would otherwise leave the asset in the new
        // state with no transition record, or vice-versa. Webhook publish
        // and author notification are deliberately outside the transaction
        // (external side-effects can't be rolled back).
        await uow.ExecuteAsync(async tct =>
        {
            await ApplyStateChangeAsync(asset, to, now, tct);
            await RecordTransitionAsync(assetId, from, to, reason, now, tct);
            await audit.LogAsync(
                plan.AuditEvent,
                Constants.ScopeTypes.Asset,
                assetId,
                currentUser.UserId,
                BuildAuditDetails(from, to, reason),
                tct);
        }, ct);

        await PublishWorkflowEventAsync(assetId, asset.Title, from, to, reason, now, ct);
        await NotifyAuthorAsync(asset, assetId, from, to, reason, ct);

        logger.LogInformation(
            "Asset {AssetId} workflow {From} → {To} by {UserId}",
            assetId, from, to, currentUser.UserId);

        var history = await transitionRepo.ListByAssetAsync(assetId, ct);
        var available = await ComputeAvailableActionsAsync(asset, ct);
        return BuildResponse(asset, history, available);
    }

    private async Task<ServiceResult<(Asset Asset, TransitionPlan Plan)>> PreflightAsync(
        Guid assetId, string action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return ServiceError.Forbidden();

        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found.");

        if (!await CanAccessAssetAsync(assetId, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        // Resolve target state + actor-role requirement from (current state, action).
        var plan = TryPlan(asset, action);
        if (plan is null)
            return ServiceError.Conflict(
                $"Cannot '{action}' from state '{asset.WorkflowState.ToDbString()}'.");

        return (asset, plan.Value);
    }

    private async Task<ServiceError?> CheckTransitionGatesAsync(
        Asset asset, TransitionPlan plan, CancellationToken ct)
    {
        // Role gate. Submit / Resubmit is author-bound (creator can always act).
        // Approve / Reject / Publish / Unpublish need Manager+ on a containing
        // collection, OR system admin.
        if (plan.RequiresAuthor && asset.CreatedByUserId != currentUser.UserId && !currentUser.IsSystemAdmin)
            return ServiceError.Forbidden(
                "Only the asset's owner can submit or resubmit it for review.");

        if (plan.RequiresRole is { } requiredRole
            && !await CanAccessAssetAsync(asset.Id, requiredRole, ct))
            return ServiceError.Forbidden($"Requires {requiredRole}+ on a containing collection.");

        // Required-metadata gate on submit / resubmit.
        if (plan.CheckRequiredMetadata)
        {
            var missing = await FindMissingRequiredFieldsAsync(asset, ct);
            if (missing.Count > 0)
                return ServiceError.Validation(
                    "Required metadata is missing for review submission.",
                    missing.ToDictionary(k => $"metadata.{k.Key}", k => $"{k.Label} is required."));
        }

        return null;
    }

    private async Task ApplyStateChangeAsync(Asset asset, AssetWorkflowState to, DateTime now, CancellationToken ct)
    {
        asset.WorkflowState = to;
        asset.WorkflowStateUpdatedAt = now;
        asset.UpdatedAt = now;
        await assetRepo.UpdateAsync(asset, ct);
    }

    private Task RecordTransitionAsync(
        Guid assetId, AssetWorkflowState from, AssetWorkflowState to,
        string? reason, DateTime now, CancellationToken ct)
        => transitionRepo.CreateAsync(new AssetWorkflowTransition
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            FromState = from,
            ToState = to,
            ActorUserId = currentUser.UserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            CreatedAt = now
        }, ct);

    private static Dictionary<string, object> BuildAuditDetails(
        AssetWorkflowState from, AssetWorkflowState to, string? reason)
        => new()
        {
            ["from_state"] = from.ToDbString(),
            ["to_state"] = to.ToDbString(),
            ["reason"] = reason ?? string.Empty
        };

    private Task PublishWorkflowEventAsync(
        Guid assetId, string assetTitle, AssetWorkflowState from, AssetWorkflowState to,
        string? reason, DateTime now, CancellationToken ct)
        => webhooks.PublishAsync(WebhookEvents.WorkflowStateChanged, new
        {
            assetId,
            assetTitle,
            fromState = from.ToDbString(),
            toState = to.ToDbString(),
            actorUserId = currentUser.UserId,
            reason,
            transitionedAt = now
        }, ct);

    private async Task NotifyAuthorAsync(
        Asset asset, Guid assetId, AssetWorkflowState from, AssetWorkflowState to,
        string? reason, CancellationToken ct)
    {
        // Notify the asset's author unless they're also the actor — no point
        // pinging yourself about your own approval click.
        if (string.IsNullOrEmpty(asset.CreatedByUserId) || asset.CreatedByUserId == currentUser.UserId)
            return;

        try
        {
            await notifications.CreateAsync(
                userId: asset.CreatedByUserId,
                category: NotificationConstants.Categories.WorkflowTransition,
                title: $"'{asset.Title}' is now {to.ToDbString()}",
                body: string.IsNullOrWhiteSpace(reason)
                    ? $"State changed from {from.ToDbString()} to {to.ToDbString()}."
                    : $"State changed from {from.ToDbString()} to {to.ToDbString()}. Reason: {reason}",
                url: $"/assets/{assetId}",
                data: new Dictionary<string, object>
                {
                    ["asset_id"] = assetId,
                    ["from_state"] = from.ToDbString(),
                    ["to_state"] = to.ToDbString(),
                    ["actor_user_id"] = currentUser.UserId
                },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Broken notification shouldn't undo the workflow change.
            logger.LogWarning(ex,
                "Failed to notify author {UserId} about workflow transition on asset {AssetId}",
                asset.CreatedByUserId, assetId);
        }
    }

    // ── Transition table ────────────────────────────────────────────────

    private record struct TransitionPlan(
        AssetWorkflowState ToState,
        string AuditEvent,
        bool RequiresAuthor,
        string? RequiresRole,
        bool CheckRequiredMetadata);

    private static TransitionPlan? TryPlan(Asset asset, string action)
    {
        var from = asset.WorkflowState;
        return (from, action) switch
        {
            // Submit / resubmit: author-bound, needs required metadata filled.
            (AssetWorkflowState.Draft, WorkflowActions.Submit)
                => new TransitionPlan(AssetWorkflowState.InReview,
                    NotificationConstants.AuditEvents.WorkflowSubmitted,
                    RequiresAuthor: true, RequiresRole: null,
                    CheckRequiredMetadata: true),
            (AssetWorkflowState.Rejected, WorkflowActions.Submit)
                => new TransitionPlan(AssetWorkflowState.InReview,
                    NotificationConstants.AuditEvents.WorkflowSubmitted,
                    RequiresAuthor: true, RequiresRole: null,
                    CheckRequiredMetadata: true),

            // Reviewer actions from InReview.
            (AssetWorkflowState.InReview, WorkflowActions.Approve)
                => new TransitionPlan(AssetWorkflowState.Approved,
                    NotificationConstants.AuditEvents.WorkflowApproved,
                    RequiresAuthor: false, RequiresRole: RoleHierarchy.Roles.Manager,
                    CheckRequiredMetadata: false),
            (AssetWorkflowState.InReview, WorkflowActions.Reject)
                => new TransitionPlan(AssetWorkflowState.Rejected,
                    NotificationConstants.AuditEvents.WorkflowRejected,
                    RequiresAuthor: false, RequiresRole: RoleHierarchy.Roles.Manager,
                    CheckRequiredMetadata: false),

            // Publish / unpublish toggle.
            (AssetWorkflowState.Approved, WorkflowActions.Publish)
                => new TransitionPlan(AssetWorkflowState.Published,
                    NotificationConstants.AuditEvents.WorkflowPublished,
                    RequiresAuthor: false, RequiresRole: RoleHierarchy.Roles.Manager,
                    CheckRequiredMetadata: false),
            (AssetWorkflowState.Published, WorkflowActions.Unpublish)
                => new TransitionPlan(AssetWorkflowState.Approved,
                    NotificationConstants.AuditEvents.WorkflowUnpublished,
                    RequiresAuthor: false, RequiresRole: RoleHierarchy.Roles.Manager,
                    CheckRequiredMetadata: false),

            _ => null
        };
    }

    private async Task<List<string>> ComputeAvailableActionsAsync(Asset asset, CancellationToken ct)
    {
        var actions = new List<string>();
        var candidates = new[]
        {
            WorkflowActions.Submit,
            WorkflowActions.Approve,
            WorkflowActions.Reject,
            WorkflowActions.Publish,
            WorkflowActions.Unpublish
        };

        foreach (var action in candidates)
        {
            var plan = TryPlan(asset, action);
            if (plan is null) continue;

            // Role / author check.
            if (plan.Value.RequiresAuthor
                && asset.CreatedByUserId != currentUser.UserId
                && !currentUser.IsSystemAdmin)
                continue;
            if (plan.Value.RequiresRole is { } requiredRole
                && !await CanAccessAssetAsync(asset.Id, requiredRole, ct))
                continue;

            actions.Add(action);
        }
        return actions;
    }

    // ── Required-metadata gate ──────────────────────────────────────────

    private record struct MissingField(string Key, string Label);

    private async Task<List<MissingField>> FindMissingRequiredFieldsAsync(Asset asset, CancellationToken ct)
    {
        var schemas = await CollectApplicableSchemasAsync(asset, ct);
        if (schemas.Count == 0) return new List<MissingField>();

        var filledFieldIds = await GetFilledFieldIdsAsync(asset.Id, ct);

        return schemas.Values
            .SelectMany(s => s.Fields)
            .Where(f => f.Required && !filledFieldIds.Contains(f.Id))
            .Select(f => new MissingField(f.Key, f.Label))
            .ToList();
    }

    private async Task<Dictionary<Guid, MetadataSchemaDto>> CollectApplicableSchemasAsync(
        Asset asset, CancellationToken ct)
    {
        // Global + asset-type + every containing collection — the per-collection
        // call already filters out non-applicable schemas. De-dup on schema id.
        var schemas = new Dictionary<Guid, MetadataSchemaDto>();
        var assetTypeStr = asset.AssetType.ToDbString();

        var globalResult = await schemaQuery.GetApplicableAsync(assetTypeStr, collectionId: null, ct);
        AddSchemasIfSuccess(schemas, globalResult);

        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(asset.Id, ct);
        foreach (var cid in collectionIds)
        {
            var perColl = await schemaQuery.GetApplicableAsync(assetTypeStr, cid, ct);
            AddSchemasIfSuccess(schemas, perColl);
        }

        return schemas;
    }

    private static void AddSchemasIfSuccess(
        Dictionary<Guid, MetadataSchemaDto> sink,
        ServiceResult<List<MetadataSchemaDto>> result)
    {
        if (!result.IsSuccess || result.Value is null) return;
        foreach (var s in result.Value) sink[s.Id] = s;
    }

    private async Task<HashSet<Guid>> GetFilledFieldIdsAsync(Guid assetId, CancellationToken ct)
    {
        var values = await metadataRepo.GetByAssetIdAsync(assetId, ct);
        return values
            .Where(HasValue)
            .Select(v => v.MetadataFieldId)
            .ToHashSet();
    }

    private static bool HasValue(AssetMetadataValue v)
        => !string.IsNullOrWhiteSpace(v.ValueText)
        || v.ValueNumeric is not null
        || v.ValueDate is not null
        || v.ValueTaxonomyTermId is not null;

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<bool> CanAccessAssetAsync(Guid assetId, string requiredRole, CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin) return true;
        var collections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var accessible = await authService.FilterAccessibleAsync(
            currentUser.UserId, collections, requiredRole, ct);
        return accessible.Count > 0;
    }

    private static AssetWorkflowResponseDto BuildResponse(
        Asset asset,
        List<AssetWorkflowTransition> history,
        List<string> available)
        => new()
        {
            AssetId = asset.Id,
            CurrentState = asset.WorkflowState.ToDbString(),
            StateUpdatedAt = asset.WorkflowStateUpdatedAt,
            AvailableActions = available,
            History = history.Select(t => new AssetWorkflowTransitionResponseDto
            {
                Id = t.Id,
                AssetId = t.AssetId,
                FromState = t.FromState.ToDbString(),
                ToState = t.ToState.ToDbString(),
                ActorUserId = t.ActorUserId,
                Reason = t.Reason,
                CreatedAt = t.CreatedAt
            }).ToList()
        };
}
