using System.Text;
using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    public async Task<MigrationListResponse> GetMigrationsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
    {
        var result = await migrationService.ListAsync(skip, take, ct);
        return Unwrap(result, "Get migrations");
    }

    public async Task<MigrationResponseDto> GetMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.GetByIdAsync(id, ct);
        return Unwrap(result, "Get migration");
    }

    public async Task<MigrationResponseDto> CreateMigrationAsync(CreateMigrationDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create migration");
        var result = await migrationService.CreateAsync(dto, ct);
        return Unwrap(result, "Create migration");
    }

    public async Task UploadMigrationManifestAsync(Guid id, Stream csvStream, string fileName, CancellationToken ct = default)
    {
        // fileName is part of the legacy multipart form; the service signature
        // is just (id, stream, ct).
        _ = fileName;
        var result = await migrationService.UploadManifestAsync(id, csvStream, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Upload migration manifest");
    }

    public async Task UploadMigrationFilesAsync(Guid id, IEnumerable<(string FileName, Stream Stream, string ContentType)> files, CancellationToken ct = default)
    {
        var result = await migrationService.UploadStagingFilesAsync(id, files, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Upload migration files");
    }

    public async Task StartMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.StartAsync(id, ct);
        EnsureSuccess(result, "Start migration");
    }

    public async Task StartMigrationS3ScanAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.StartS3ScanAsync(id, ct);
        EnsureSuccess(result, "Start S3 scan");
    }

    public async Task CancelMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.CancelAsync(id, ct);
        EnsureSuccess(result, "Cancel migration");
    }

    public async Task RetryFailedMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.RetryFailedAsync(id, ct);
        EnsureSuccess(result, "Retry failed migration items");
    }

    public async Task<MigrationProgressDto> GetMigrationProgressAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.GetProgressAsync(id, ct);
        return Unwrap(result, "Get migration progress");
    }

    public async Task<MigrationItemListResponse> GetMigrationItemsAsync(Guid id, string? statusFilter = null, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await migrationService.GetItemsAsync(id, statusFilter, skip, take, ct);
        return Unwrap(result, "Get migration items");
    }

    public async Task DeleteMigrationAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete migration");
    }

    /// <summary>
    /// Generates the migration outcome CSV in-process. The legacy HTTP endpoint
    /// produced the same shape (header + per-item rows); this mirrors it so
    /// callers receive an identical stream.
    /// </summary>
    public async Task<Stream> DownloadMigrationOutcomeAsync(Guid id, CancellationToken ct = default)
    {
        var result = await migrationService.GetItemsAsync(id, null, 0, 100_000, ct);
        var items = Unwrap(result, "Download migration outcome").Items;

        var csv = new StringBuilder();
        csv.AppendLine("external_id,filename,status,target_asset_id,error_code,error_message");
        foreach (var item in items)
        {
            csv.Append(EscapeCsvField(item.ExternalId ?? "")).Append(',');
            csv.Append(EscapeCsvField(item.FileName)).Append(',');
            csv.Append(EscapeCsvField(item.Status)).Append(',');
            csv.Append(item.AssetId?.ToString() ?? "").Append(',');
            csv.Append(EscapeCsvField(item.ErrorCode ?? "")).Append(',');
            csv.Append(EscapeCsvField(item.ErrorMessage ?? ""));
            csv.AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return new MemoryStream(bytes, writable: false);
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public async Task UnstageMigrationItemAsync(Guid migrationId, Guid itemId, CancellationToken ct = default)
    {
        var result = await migrationService.UnstageMigrationItemAsync(migrationId, itemId, ct);
        EnsureSuccess(result, "Unstage migration item");
    }

    public async Task<int> BulkDeleteMigrationsAsync(string filter, CancellationToken ct = default)
    {
        var result = await migrationService.BulkDeleteAsync(filter, ct);
        return Unwrap(result, "Bulk delete migrations");
    }
}
