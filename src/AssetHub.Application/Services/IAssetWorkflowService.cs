using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Publishing workflow (T3-WF-01). Transitions:
/// <list type="bullet">
/// <item>Draft → InReview (submit) — author, gated by required metadata.</item>
/// <item>InReview → Approved (approve) — Manager+ with optional note.</item>
/// <item>InReview → Rejected (reject) — Manager+, reason required.</item>
/// <item>Rejected → InReview (resubmit) — author, same metadata gate.</item>
/// <item>Approved → Published (publish) — Manager+.</item>
/// <item>Published → Approved (unpublish) — Manager+.</item>
/// </list>
///
/// Each successful transition appends an <c>AssetWorkflowTransition</c>
/// row, emits an <c>asset.workflow_*</c> audit event, and notifies the
/// asset's creator via the <c>workflow_transition</c> notification
/// category (in-app + email per user prefs).
/// </summary>
public interface IAssetWorkflowService
{
    Task<ServiceResult<AssetWorkflowResponseDto>> GetAsync(Guid assetId, CancellationToken ct);

    Task<ServiceResult<AssetWorkflowResponseDto>> SubmitAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct);

    Task<ServiceResult<AssetWorkflowResponseDto>> ApproveAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct);

    Task<ServiceResult<AssetWorkflowResponseDto>> RejectAsync(
        Guid assetId, WorkflowRejectDto dto, CancellationToken ct);

    Task<ServiceResult<AssetWorkflowResponseDto>> PublishAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct);

    Task<ServiceResult<AssetWorkflowResponseDto>> UnpublishAsync(
        Guid assetId, WorkflowActionDto dto, CancellationToken ct);
}

/// <summary>
/// Action tokens the service surfaces in <c>AvailableActions</c> so the UI
/// knows which buttons to render for the current user + state combo.
/// Kept as string constants so they're stable across the JSON wire.
/// </summary>
public static class WorkflowActions
{
    public const string Submit = "submit";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";
}
