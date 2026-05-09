using System.Globalization;
using System.Text;
using AssetHub.Api.Authentication;
using AssetHub.Api.Extensions;
using AssetHub.Api.Filters;
using AssetHub.Api.OpenApi;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetHub.Api.Endpoints;

/// <summary>
/// Analytics dashboard surface (T5-ANL-01). Admin-only across the board.
///
/// Three GET endpoints serve the in-app dashboard JSON; one CSV export per
/// panel; one POST + one GET drive the async PDF export lifecycle; one POST
/// reveals a watermark recipient hash to plaintext.
/// </summary>
public static class AnalyticsEndpoints
{
    private const string GroupTag = "Analytics";
    private const string AdminScope = "admin";

    public static void MapAnalyticsEndpoints(this WebApplication app)
    {
        MapDashboardQueries(app);
        MapCsvExports(app);
        MapPdfExport(app);
        MapExposureReveal(app);
    }

    // ── Dashboard JSON queries ─────────────────────────────────────────────

    private static void MapDashboardQueries(WebApplication app)
    {
        var group = NewAdminGroup(app);
        var scope = new RequireScopeFilter(AdminScope);

        group.MapGet("/downloads", async (
                [FromQuery] int? window,
                [FromQuery] int? take,
                [FromServices] IAnalyticsService svc,
                CancellationToken ct) =>
            (await svc.GetTopDownloadedAssetsAsync(window ?? 90, take ?? 20, ct)).ToHttpResult())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Top downloaded assets in the time window.");

        group.MapGet("/downloads/daily", async (
                [FromQuery] int? window,
                [FromServices] IAnalyticsService svc,
                CancellationToken ct) =>
            (await svc.GetDailyDownloadCountsAsync(window ?? 90, ct)).ToHttpResult())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Daily total download counts for the trend chart.");

        group.MapGet("/storage/by-collection", async (
                [FromQuery] int? take,
                [FromServices] IAnalyticsService svc,
                CancellationToken ct) =>
            (await svc.GetStorageByCollectionAsync(take ?? 20, ct)).ToHttpResult())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Storage usage broken down by collection.");

        group.MapGet("/storage/by-asset-type", async (
                [FromServices] IAnalyticsService svc,
                CancellationToken ct) =>
            (await svc.GetStorageByAssetTypeAsync(ct)).ToHttpResult())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Storage usage broken down by asset type.");

        group.MapGet("/exposure", async (
                [FromQuery] int? window,
                [FromQuery] int? take,
                [FromServices] IAnalyticsService svc,
                CancellationToken ct) =>
            (await svc.GetTopRecipientsAsync(window ?? 90, take ?? 20, ct)).ToHttpResult())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Top forensic-watermark recipients (hash-grouped). Reveal via /exposure/reveal.");
    }

    // ── CSV exports ────────────────────────────────────────────────────────

    private static void MapCsvExports(WebApplication app)
    {
        var group = NewAdminGroup(app);
        var scope = new RequireScopeFilter(AdminScope);

        group.MapGet("/downloads/export.csv", async (
                [FromQuery] int? window,
                [FromQuery] int? take,
                [FromServices] IAnalyticsService svc,
                [FromServices] IAuditService audit,
                CancellationToken ct) =>
            {
                var result = await svc.GetTopDownloadedAssetsAsync(window ?? 90, take ?? 20, ct);
                if (!result.IsSuccess) return result.ToHttpResult();
                await AuditExportAsync(audit, "downloads", "csv", window ?? 90, result.Value!.Count, ct);
                var csv = BuildCsv(result.Value!,
                    new[] { "asset_id", "title", "asset_type", "download_count", "is_deleted" },
                    r => new[]
                    {
                        r.AssetId.ToString("D"),
                        r.Title ?? string.Empty,
                        r.AssetType ?? string.Empty,
                        r.DownloadCount.ToString(CultureInfo.InvariantCulture),
                        r.IsDeleted ? "true" : "false"
                    });
                return CsvResult(csv, $"analytics-downloads-{DateTime.UtcNow:yyyyMMdd}.csv");
            })
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("CSV export of the top-downloads panel.");

        group.MapGet("/storage/export.csv", async (
                [FromQuery] int? take,
                [FromServices] IAnalyticsService svc,
                [FromServices] IAuditService audit,
                CancellationToken ct) =>
            {
                var byColl = await svc.GetStorageByCollectionAsync(take ?? 20, ct);
                if (!byColl.IsSuccess) return byColl.ToHttpResult();
                var byType = await svc.GetStorageByAssetTypeAsync(ct);
                if (!byType.IsSuccess) return byType.ToHttpResult();

                var totalRows = (byColl.Value!.Count) + (byType.Value!.Count);
                await AuditExportAsync(audit, "storage", "csv", 0, totalRows, ct);

                // Two stacked sections in one CSV — header rows separate them.
                var sb = new StringBuilder();
                sb.AppendLine("section,collection_id,collection_name,asset_type,bytes,asset_count");
                foreach (var r in byColl.Value!)
                {
                    sb.Append("by_collection,").Append(r.CollectionId.ToString("D")).Append(',');
                    sb.Append(EscapeCsv(r.CollectionName)).Append(',').Append(',');
                    sb.Append(r.Bytes.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.AppendLine(r.AssetCount.ToString(CultureInfo.InvariantCulture));
                }
                foreach (var r in byType.Value!)
                {
                    sb.Append("by_asset_type,,,").Append(EscapeCsv(r.AssetType)).Append(',');
                    sb.Append(r.Bytes.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.AppendLine(r.AssetCount.ToString(CultureInfo.InvariantCulture));
                }
                return CsvResult(sb.ToString(), $"analytics-storage-{DateTime.UtcNow:yyyyMMdd}.csv");
            })
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("CSV export of the storage panel (both sections).");

        group.MapGet("/exposure/export.csv", async (
                [FromQuery] int? window,
                [FromQuery] int? take,
                [FromServices] IAnalyticsService svc,
                [FromServices] IAuditService audit,
                CancellationToken ct) =>
            {
                var result = await svc.GetTopRecipientsAsync(window ?? 90, take ?? 20, ct);
                if (!result.IsSuccess) return result.ToHttpResult();
                await AuditExportAsync(audit, "exposure", "csv", window ?? 90, result.Value!.Count, ct);
                var csv = BuildCsv(result.Value!,
                    new[] { "recipient_hash", "recipient_kind", "download_count", "distinct_asset_count" },
                    r => new[]
                    {
                        r.RecipientHash,
                        r.RecipientKind,
                        r.DownloadCount.ToString(CultureInfo.InvariantCulture),
                        r.DistinctAssetCount.ToString(CultureInfo.InvariantCulture)
                    });
                return CsvResult(csv, $"analytics-exposure-{DateTime.UtcNow:yyyyMMdd}.csv");
            })
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("CSV export of the exposure panel (hash-grouped, no PII).");
    }

    // ── Async PDF export (job-queue lifecycle) ─────────────────────────────

    private static void MapPdfExport(WebApplication app)
    {
        var group = NewAdminGroup(app);
        var scope = new RequireScopeFilter(AdminScope);

        group.MapPost("/export-pdf", async (
                [FromQuery] int? window,
                [FromServices] IAnalyticsPdfJobService jobService,
                CancellationToken ct) =>
            (await jobService.EnqueueAsync(window ?? 90, ct))
                .ToHttpResult(value => Results.Accepted(
                    uri: $"/api/v1/admin/analytics/export-pdf/{value.JobId}",
                    value: value)))
            .DisableAntiforgery()
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Queue a PDF export of the dashboard. Returns 202 + job id.");

        group.MapGet("/export-pdf/{jobId:guid}", async (
                Guid jobId,
                [FromServices] IAnalyticsPdfJobService jobService,
                CancellationToken ct) =>
            (await jobService.GetStatusAsync(jobId, ct)).ToHttpResult())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Poll PDF export status. Returns presigned download URL when ready.");
    }

    // ── Recipient reveal ───────────────────────────────────────────────────

    private static void MapExposureReveal(WebApplication app)
    {
        var group = NewAdminGroup(app);
        var scope = new RequireScopeFilter(AdminScope);

        group.MapPost("/exposure/reveal", async (
                RevealRecipientRequestDto request,
                [FromServices] IAnalyticsService svc,
                CancellationToken ct) =>
            (await svc.RevealRecipientAsync(request, ct)).ToHttpResult())
            .DisableAntiforgery()
            .AddEndpointFilter(new ValidationFilter<RevealRecipientRequestDto>())
            .AddEndpointFilter(scope)
            .MarkAsPublicApi()
            .WithSummary("Decrypt a recipient hash to plaintext. Audited (no PII in the audit row).");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static RouteGroupBuilder NewAdminGroup(WebApplication app) =>
        app.MapGroup("/api/v1/admin/analytics")
           .RequireAuthorization("RequireAdmin")
           .RequireAntiforgeryUnlessBearer()
           .WithTags(GroupTag);

    private static IResult CsvResult(string content, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Results.File(bytes, contentType: "text/csv", fileDownloadName: fileName);
    }

    private static string BuildCsv<T>(
        IEnumerable<T> rows,
        IReadOnlyList<string> headers,
        Func<T, IReadOnlyList<string>> rowSelector)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', rowSelector(row).Select(EscapeCsv)));
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsEscape = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsEscape) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static async Task AuditExportAsync(
        IAuditService audit, string panel, string format, int windowDays, int rowCount, CancellationToken ct)
    {
        await audit.LogAsync(
            eventType: AssetHub.Application.AnalyticsConstants.AuditEvents.Exported,
            targetType: AssetHub.Application.AnalyticsConstants.ScopeTypes.Analytics,
            targetId: Guid.Empty,
            actorUserId: null,
            details: new Dictionary<string, object>
            {
                ["panel"] = panel,
                ["format"] = format,
                ["window_days"] = windowDays,
                ["row_count"] = rowCount
            },
            ct: ct);
    }
}
