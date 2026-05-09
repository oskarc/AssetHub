using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AssetHub.Worker.Handlers;

/// <summary>
/// Renders the queued analytics dashboard as a PDF and uploads it to MinIO
/// (T5-ANL-01).
///
/// QuestPDF Community license note: this handler uses QuestPDF, which is
/// free for organisations with under $1M USD annual revenue (Community
/// Edition). Operators above that threshold must purchase a Professional
/// or Enterprise license, or swap this handler's renderer for a different
/// PDF engine. See <c>docs/operations/THIRD-PARTY-LICENSES.md</c>.
/// </summary>
public sealed class BuildAnalyticsPdfHandler(
    IAnalyticsPdfJobRepository jobRepo,
    IAnalyticsService analytics,
    IMinIOAdapter minio,
    IOptions<MinIOSettings> minioSettings,
    IAuditService auditService,
    ILogger<BuildAnalyticsPdfHandler> logger)
{
    private readonly string _bucket = minioSettings.Value.BucketName;

    public async Task HandleAsync(BuildAnalyticsPdfCommand command, CancellationToken ct)
    {
        var job = await jobRepo.GetByIdAsync(command.JobId, ct);
        if (job is null)
        {
            logger.LogWarning("BuildAnalyticsPdf received for unknown job {JobId}", command.JobId);
            return;
        }

        // Once-per-process license activation. Idempotent — calling Activate
        // multiple times in the same process is a no-op after the first call.
        QuestPDF.Settings.License = LicenseType.Community;

        try
        {
            job.Status = AnalyticsPdfJobStatus.Building;
            await jobRepo.UpdateAsync(job, ct);

            // Pull the same data the dashboard renders for the configured window.
            var window = job.WindowDays;
            var topAssetsResult = await analytics.GetTopDownloadedAssetsAsync(window, take: 20, ct);
            var dailyResult = await analytics.GetDailyDownloadCountsAsync(window, ct);
            var byCollectionResult = await analytics.GetStorageByCollectionAsync(take: 20, ct);
            var byTypeResult = await analytics.GetStorageByAssetTypeAsync(ct);
            var exposureResult = await analytics.GetTopRecipientsAsync(window, take: 20, ct);

            var pdfBytes = RenderPdf(
                job,
                topAssets: topAssetsResult.Value ?? [],
                daily: dailyResult.Value ?? [],
                byCollection: byCollectionResult.Value ?? [],
                byType: byTypeResult.Value ?? [],
                exposure: exposureResult.Value ?? []);

            var objectKey = $"{AnalyticsConstants.PdfObjectKeyPrefix}{job.Id:D}.pdf";
            using var stream = new MemoryStream(pdfBytes);
            await minio.UploadAsync(_bucket, objectKey, stream, "application/pdf", ct);

            job.ObjectKey = objectKey;
            job.SizeBytes = pdfBytes.Length;
            job.Status = AnalyticsPdfJobStatus.Ready;
            job.CompletedAt = DateTime.UtcNow;
            await jobRepo.UpdateAsync(job, ct);

            await auditService.LogAsync(
                eventType: AnalyticsConstants.AuditEvents.Exported,
                targetType: AnalyticsConstants.ScopeTypes.Analytics,
                targetId: Guid.Empty,
                actorUserId: job.RequestedByUserId,
                details: new Dictionary<string, object>
                {
                    ["format"] = "pdf",
                    ["window_days"] = window,
                    ["size_bytes"] = pdfBytes.Length,
                    ["job_id"] = job.Id.ToString("D")
                },
                ct: ct);

            logger.LogInformation(
                "Analytics PDF rendered for job {JobId} ({SizeBytes} bytes, window {Window} d)",
                job.Id, pdfBytes.Length, window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to render analytics PDF for job {JobId}", job.Id);
            job.Status = AnalyticsPdfJobStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            // Truncate to fit the column max — error messages from QuestPDF can be long.
            job.ErrorMessage = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            await jobRepo.UpdateAsync(job, ct);
            // Don't rethrow — Wolverine retry would just re-render the same broken PDF.
        }
    }

    private static byte[] RenderPdf(
        AnalyticsPdfJob job,
        IReadOnlyList<AnalyticsAssetDownloadRowDto> topAssets,
        IReadOnlyList<AnalyticsDailyPointDto> daily,
        IReadOnlyList<AnalyticsStorageByCollectionRowDto> byCollection,
        IReadOnlyList<AnalyticsStorageByAssetTypeRowDto> byType,
        IReadOnlyList<AnalyticsExposureRowDto> exposure)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("AssetHub — Analytics Snapshot")
                        .FontSize(20).SemiBold();
                    col.Item().Text(
                        $"Window: last {job.WindowDays} day(s) · Generated {job.CreatedAt:yyyy-MM-dd HH:mm} UTC")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(16);

                    RenderDownloadsSection(col, topAssets, daily);
                    RenderStorageSection(col, byCollection, byType);
                    RenderExposureSection(col, exposure);
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    t.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    private static void RenderDownloadsSection(
        ColumnDescriptor col,
        IReadOnlyList<AnalyticsAssetDownloadRowDto> topAssets,
        IReadOnlyList<AnalyticsDailyPointDto> daily)
    {
        col.Item().Text("Downloads").FontSize(14).SemiBold();
        col.Item().Text($"Daily totals over the window ({daily.Count} day(s)):")
            .FontSize(9).FontColor(Colors.Grey.Medium);

        if (daily.Count == 0)
        {
            col.Item().Text("No download events in the selected window.").Italic();
        }
        else
        {
            // Inline mini-chart as text bars — QuestPDF community has charts via
            // SkiaSharp but PDF text is sufficient for a board pack and avoids
            // the extra dependency / rendering surface.
            var maxCount = daily.Max(d => d.Count);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(80);
                    c.RelativeColumn();
                    c.ConstantColumn(60);
                });
                foreach (var d in daily.TakeLast(14))
                {
                    table.Cell().Text(d.Date.ToString("yyyy-MM-dd"));
                    table.Cell().Text(BarString(d.Count, maxCount));
                    table.Cell().AlignRight().Text(d.Count.ToString("N0"));
                }
            });
        }

        col.Item().PaddingTop(8).Text($"Top {topAssets.Count} assets by download count:")
            .FontSize(11).SemiBold();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(2);
                c.ConstantColumn(70);
            });
            table.Header(h =>
            {
                h.Cell().Text("Asset").SemiBold();
                h.Cell().Text("Type").SemiBold();
                h.Cell().AlignRight().Text("Downloads").SemiBold();
            });
            foreach (var row in topAssets)
            {
                var title = row.Title ?? row.AssetId.ToString("D");
                if (row.IsDeleted) title += " (deleted)";
                table.Cell().Text(title);
                table.Cell().Text(row.AssetType ?? "-");
                table.Cell().AlignRight().Text(row.DownloadCount.ToString("N0"));
            }
        });
    }

    private static void RenderStorageSection(
        ColumnDescriptor col,
        IReadOnlyList<AnalyticsStorageByCollectionRowDto> byCollection,
        IReadOnlyList<AnalyticsStorageByAssetTypeRowDto> byType)
    {
        col.Item().PaddingTop(8).Text("Storage").FontSize(14).SemiBold();

        col.Item().Text($"Top {byCollection.Count} collections by size:")
            .FontSize(11).SemiBold();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(5);
                c.ConstantColumn(60);
                c.ConstantColumn(80);
            });
            table.Header(h =>
            {
                h.Cell().Text("Collection").SemiBold();
                h.Cell().AlignRight().Text("Assets").SemiBold();
                h.Cell().AlignRight().Text("Size").SemiBold();
            });
            foreach (var row in byCollection)
            {
                table.Cell().Text(row.CollectionName ?? row.CollectionId.ToString("D"));
                table.Cell().AlignRight().Text(row.AssetCount.ToString("N0"));
                table.Cell().AlignRight().Text(FormatBytes(row.Bytes));
            }
        });

        col.Item().PaddingTop(8).Text("By asset type:").FontSize(11).SemiBold();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.ConstantColumn(60);
                c.ConstantColumn(80);
            });
            table.Header(h =>
            {
                h.Cell().Text("Type").SemiBold();
                h.Cell().AlignRight().Text("Assets").SemiBold();
                h.Cell().AlignRight().Text("Size").SemiBold();
            });
            foreach (var row in byType)
            {
                table.Cell().Text(row.AssetType);
                table.Cell().AlignRight().Text(row.AssetCount.ToString("N0"));
                table.Cell().AlignRight().Text(FormatBytes(row.Bytes));
            }
        });
    }

    private static void RenderExposureSection(
        ColumnDescriptor col,
        IReadOnlyList<AnalyticsExposureRowDto> exposure)
    {
        col.Item().PaddingTop(8).Text("Forensic exposure").FontSize(14).SemiBold();
        col.Item().Text(
                "Recipients are shown as opaque hashes; reveal the plaintext via the admin dashboard.")
            .FontSize(9).FontColor(Colors.Grey.Medium);

        if (exposure.Count == 0)
        {
            col.Item().Text("No watermarked downloads in the selected window.").Italic();
            return;
        }

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(6);
                c.ConstantColumn(60);
                c.ConstantColumn(80);
                c.ConstantColumn(80);
            });
            table.Header(h =>
            {
                h.Cell().Text("Recipient hash").SemiBold();
                h.Cell().Text("Kind").SemiBold();
                h.Cell().AlignRight().Text("Downloads").SemiBold();
                h.Cell().AlignRight().Text("Distinct assets").SemiBold();
            });
            foreach (var row in exposure)
            {
                // Truncate the hash for readability; full hash lives in the audit log.
                var shortHash = row.RecipientHash.Length > 24
                    ? row.RecipientHash[..24] + "…"
                    : row.RecipientHash;
                table.Cell().Text(shortHash);
                table.Cell().Text(row.RecipientKind);
                table.Cell().AlignRight().Text(row.DownloadCount.ToString("N0"));
                table.Cell().AlignRight().Text(row.DistinctAssetCount.ToString("N0"));
            }
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }

    private static string BarString(long count, long max)
    {
        if (max <= 0) return "";
        var len = (int)Math.Round((double)count / max * 30);
        return new string('█', Math.Max(1, len));
    }
}
