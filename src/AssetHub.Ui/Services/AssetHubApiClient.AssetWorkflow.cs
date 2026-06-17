using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<AssetWorkflowResponseDto> GetAssetWorkflowAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.GetAsync(assetId, ct);
        return Unwrap(result, "Get asset workflow");
    }

    public async Task<AssetWorkflowResponseDto> SubmitAssetForReviewAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.SubmitAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "submit asset workflow");
    }

    public async Task<AssetWorkflowResponseDto> ApproveAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.ApproveAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "approve asset workflow");
    }

    public async Task<AssetWorkflowResponseDto> RejectAssetAsync(Guid assetId, string reason, CancellationToken ct = default)
    {
        var dto = new WorkflowRejectDto { Reason = reason };
        Validate(dto, "Reject asset");
        var result = await assetWorkflowService.RejectAsync(assetId, dto, ct);
        return Unwrap(result, "reject asset workflow");
    }

    public async Task<AssetWorkflowResponseDto> PublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.PublishAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "publish asset workflow");
    }

    public async Task<AssetWorkflowResponseDto> UnpublishAssetAsync(Guid assetId, string? reason, CancellationToken ct = default)
    {
        var result = await assetWorkflowService.UnpublishAsync(assetId, new WorkflowActionDto { Reason = reason }, ct);
        return Unwrap(result, "unpublish asset workflow");
    }
}
