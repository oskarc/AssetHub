using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<DashboardDto?> GetDashboardAsync(CancellationToken ct = default)
    {
        var result = await dashboardService.GetDashboardAsync(ct);
        return Unwrap(result, "Get dashboard");
    }
}
