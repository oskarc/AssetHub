using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Append-only history for <see cref="AssetWorkflowTransition"/>. Service
/// writes one row per transition; the panel reads the whole list for one
/// asset in chronological order.
/// </summary>
public interface IAssetWorkflowTransitionRepository
{
    Task<List<AssetWorkflowTransition>> ListByAssetAsync(Guid assetId, CancellationToken ct = default);

    Task<AssetWorkflowTransition> CreateAsync(AssetWorkflowTransition entity, CancellationToken ct = default);
}
