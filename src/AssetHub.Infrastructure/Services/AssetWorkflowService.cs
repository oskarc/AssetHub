using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

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

        // Role gate. Submit / Resubmit is author-bound (creator can always act).
        // Approve / Reject / Publish / Unpublish need Manager+ on a containing
        // collection, OR system admin.
        if (plan.Value.RequiresAuthor && asset.CreatedByUserId != currentUser.UserId && !currentUser.IsSystemAdmin)
            return ServiceError.Forbidden(
                "Only the asset's owner can submit or resubmit it for review.");

        if (plan.Value.RequiresRole is { } requiredRole
            && !await CanAccessAssetAsync(assetId, requiredRole, ct))
            return ServiceError.Forbidden($"Requires {requiredRole}+ on a containing collection.");

        // Required-metadata gate on submit / resubmit.
        if (plan.Value.CheckRequiredMetadata)
        {
            var missing = await FindMissingRequiredFieldsAsync(asset, ct);
            if (missing.Count > 0)
                return ServiceError.Validation(
                    "Required metadata is missing for review submission.",
                    missing.ToDictionary(k => $"metadata.{k.Key}", k => $"{k.Label} is required."));
        }

        var from = asset.WorkflowState;
        var to = plan.Value.ToState;
        var now = DateTime.UtcNow;

        asset.WorkflowState = to;
        asset.WorkflowStateUpdatedAt = now;
        asset.UpdatedAt = now;
        await assetRepo.UpdateAsync(asset, ct);

        await transitionRepo.CreateAsync(new AssetWorkflowTransition
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            FromState = from,
            ToState = to,
            ActorUserId = currentUser.UserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            CreatedAt = now
        }, ct);

        await audit.LogAsync(
            plan.Value.AuditEvent,
            Constants.ScopeTypes.Asset,
            assetId,
            currentUser.UserId,
            new Dictionary<string, object>
            {
                ["from_state"] = from.ToDbString(),
                ["to_state"] = to.ToDbString(),
                ["reason"] = reason ?? string.Empty
            },
            ct);

        await webhooks.PublishAsync(WebhookEvents.WorkflowStateChanged, new
        {
            assetId,
            assetTitle = asset.Title,
            fromState = from.ToDbString(),
            toState = to.ToDbString(),
            actorUserId = currentUser.UserId,
            reason,
            transitionedAt = now
        }, ct);

        // Notify the asset's author unless they're also the actor — no point
        // pinging yourself about your own approval click.
        if (!string.IsNullOrEmpty(asset.CreatedByUserId) && asset.CreatedByUserId != currentUser.UserId)
        {
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

        logger.LogInformation(
            "Asset {AssetId} workflow {From} → {To} by {UserId}",
            assetId, from, to, currentUser.UserId);

        var history = await transitionRepo.ListByAssetAsync(assetId, ct);
        var available = await ComputeAvailableActionsAsync(asset, ct);
        return BuildResponse(asset, history, available);
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
        var missing = new List<MissingField>();

        // Gather collection ids for schema resolution.
        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(asset.Id, ct);

        // Global + asset-type + every containing collection — the per-collection
        // call already filters out non-applicable schemas. De-dup on schema id.
        var schemas = new Dictionary<Guid, MetadataSchemaDto>();

        var globalResult = await schemaQuery.GetApplicableAsync(asset.AssetType.ToDbString(), collectionId: null, ct);
        if (globalResult.IsSuccess && globalResult.Value is not null)
            foreach (var s in globalResult.Value) schemas[s.Id] = s;

        foreach (var cid in collectionIds)
        {
            var perColl = await schemaQuery.GetApplicableAsync(asset.AssetType.ToDbString(), cid, ct);
            if (perColl.IsSuccess && perColl.Value is not null)
                foreach (var s in perColl.Value) schemas[s.Id] = s;
        }

        if (schemas.Count == 0) return missing;

        // What values has the asset already filled in?
        var values = await metadataRepo.GetByAssetIdAsync(asset.Id, ct);
        var filledFieldIds = values
            .Where(v => HasValue(v))
            .Select(v => v.MetadataFieldId)
            .ToHashSet();

        foreach (var schema in schemas.Values)
        {
            foreach (var field in schema.Fields)
            {
                if (field.Required && !filledFieldIds.Contains(field.Id))
                    missing.Add(new MissingField(field.Key, field.Label));
            }
        }

        return missing;
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
