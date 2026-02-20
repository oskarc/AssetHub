using AssetHub.Application.Services;

namespace AssetHub.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard")
            .RequireAuthorization()
            .WithTags("Dashboard");

        group.MapGet("", GetDashboard).WithName("GetDashboard");
    }

    private static async Task<IResult> GetDashboard(
        IDashboardService dashboardService,
        CancellationToken ct)
    {
        var result = await dashboardService.GetDashboardAsync(ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.StatusCode(500);
    }
}
