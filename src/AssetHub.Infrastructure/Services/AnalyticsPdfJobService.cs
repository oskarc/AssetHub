using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wolverine;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Lifecycle service for queued analytics PDF exports (T5-ANL-01).
/// Mirrors the ZIP-download lifecycle:
/// <list type="number">
///   <item>API enqueues — creates a Pending row, dispatches <c>BuildAnalyticsPdfCommand</c>.</item>
///   <item>Worker handler renders + uploads to MinIO + flips the row to Ready.</item>
///   <item>API polls — returns a presigned URL once Ready.</item>
/// </list>
/// </summary>
public sealed class AnalyticsPdfJobService(
    IAnalyticsPdfJobRepository jobRepo,
    IMinIOAdapter minio,
    IOptions<MinIOSettings> minioSettings,
    IOptions<AnalyticsSettings> analyticsSettings,
    IMessageBus bus,
    CurrentUser currentUser,
    ILogger<AnalyticsPdfJobService> logger) : IAnalyticsPdfJobService
{
    private readonly string _bucketName = minioSettings.Value.BucketName;
    private readonly AnalyticsSettings _settings = analyticsSettings.Value;

    public async Task<ServiceResult<AnalyticsPdfJobStatusDto>> EnqueueAsync(int windowDays, CancellationToken ct)
    {
        if (windowDays is < 1 or > 365)
            return ServiceError.BadRequest("windowDays must be between 1 and 365.");

        var job = new AnalyticsPdfJob
        {
            Id = Guid.NewGuid(),
            Status = AnalyticsPdfJobStatus.Pending,
            WindowDays = windowDays,
            RequestedByUserId = currentUser.UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(_settings.PdfRetentionHours)
        };
        await jobRepo.AddAsync(job, ct);

        try
        {
            await bus.PublishAsync(new BuildAnalyticsPdfCommand { JobId = job.Id });
        }
        catch (Exception ex)
        {
            // Mark the job failed so the UI surfaces the error rather than
            // polling forever — message-bus outage is operational, not
            // user-facing, but the user-facing job needs a terminal state.
            logger.LogError(ex, "Failed to dispatch BuildAnalyticsPdfCommand for job {JobId}", job.Id);
            job.Status = AnalyticsPdfJobStatus.Failed;
            job.ErrorMessage = "Failed to enqueue PDF build — message bus unavailable.";
            job.CompletedAt = DateTime.UtcNow;
            await jobRepo.UpdateAsync(job, ct);
            return ServiceError.Server(job.ErrorMessage);
        }

        return ToDto(job, downloadUrl: null);
    }

    public async Task<ServiceResult<AnalyticsPdfJobStatusDto>> GetStatusAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobRepo.GetByIdAsync(jobId, ct);
        if (job is null)
            return ServiceError.NotFound("PDF export job not found or expired.");

        // Self-scope: an admin should only see jobs they queued. If we ever
        // grow non-admin requesters this check stays load-bearing; for v1
        // every caller is an admin and the check is a belt-and-braces.
        if (!string.Equals(job.RequestedByUserId, currentUser.UserId, StringComparison.Ordinal)
            && !currentUser.IsSystemAdmin)
        {
            return ServiceError.Forbidden();
        }

        string? downloadUrl = null;
        if (job.Status == AnalyticsPdfJobStatus.Ready && job.ObjectKey is not null)
        {
            downloadUrl = await minio.GetPresignedDownloadUrlAsync(
                _bucketName,
                job.ObjectKey,
                expirySeconds: 3600,
                forceDownload: true,
                downloadFileName: $"analytics-{job.CreatedAt:yyyy-MM-dd}.pdf",
                ct);
        }

        return ToDto(job, downloadUrl);
    }

    private static AnalyticsPdfJobStatusDto ToDto(AnalyticsPdfJob job, string? downloadUrl) => new()
    {
        JobId = job.Id,
        Status = job.Status.ToDbString(),
        WindowDays = job.WindowDays,
        CreatedAt = job.CreatedAt,
        CompletedAt = job.CompletedAt,
        DownloadUrl = downloadUrl,
        ErrorMessage = job.ErrorMessage
    };
}
