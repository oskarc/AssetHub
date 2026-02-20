using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

public interface IDashboardService
{
    /// <summary>
    /// Returns aggregated dashboard data scoped to the requesting user's role.
    /// Admin sees global stats + all shares + all activity.
    /// Manager sees own shares + own activity.
    /// Contributor/Viewer sees recent assets + collections.
    /// </summary>
    Task<ServiceResult<DashboardDto>> GetDashboardAsync(CancellationToken ct = default);
}
