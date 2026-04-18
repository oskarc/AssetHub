using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Service for managing bulk import migrations.
/// Handles migration lifecycle, manifest parsing, and item tracking.
/// </summary>
public interface IMigrationService
{
    /// <summary>
    /// Create a new migration job in Draft status.
    /// </summary>
    Task<ServiceResult<MigrationResponseDto>> CreateAsync(CreateMigrationDto dto, CancellationToken ct);

    /// <summary>
    /// Upload and parse a CSV manifest for a draft migration.
    /// Populates migration items from the CSV rows.
    /// </summary>
    Task<ServiceResult<MigrationResponseDto>> UploadManifestAsync(Guid migrationId, Stream csvStream, CancellationToken ct);

    /// <summary>
    /// Start processing a migration (transitions from Draft to Running).
    /// Publishes a StartMigrationCommand via Wolverine.
    /// </summary>
    Task<ServiceResult> StartAsync(Guid migrationId, CancellationToken ct);

    /// <summary>
    /// Cancel a running migration.
    /// </summary>
    Task<ServiceResult> CancelAsync(Guid migrationId, CancellationToken ct);

    /// <summary>
    /// Get a migration by ID with summary counts.
    /// </summary>
    Task<ServiceResult<MigrationResponseDto>> GetByIdAsync(Guid migrationId, CancellationToken ct);

    /// <summary>
    /// List all migrations with pagination.
    /// </summary>
    Task<ServiceResult<MigrationListResponse>> ListAsync(int skip, int take, CancellationToken ct);

    /// <summary>
    /// Get real-time progress for a migration.
    /// </summary>
    Task<ServiceResult<MigrationProgressDto>> GetProgressAsync(Guid migrationId, CancellationToken ct);

    /// <summary>
    /// Get items for a migration with optional status filter and pagination.
    /// </summary>
    Task<ServiceResult<MigrationItemListResponse>> GetItemsAsync(
        Guid migrationId, string? statusFilter, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Delete a migration and all its items (only allowed for Draft/Completed/Failed/Cancelled).
    /// </summary>
    Task<ServiceResult> DeleteAsync(Guid migrationId, CancellationToken ct);

    /// <summary>
    /// Retry failed items in a completed-with-errors migration.
    /// </summary>
    Task<ServiceResult> RetryFailedAsync(Guid migrationId, CancellationToken ct);

    /// <summary>
    /// Upload a file to the migration staging area.
    /// The file can later be matched to a MigrationItem by filename.
    /// </summary>
    Task<ServiceResult> UploadStagingFileAsync(
        Guid migrationId, string fileName, Stream fileStream, string contentType, CancellationToken ct);

    /// <summary>
    /// Upload a batch of files (from a multipart form) to the migration staging area.
    /// </summary>
    Task<ServiceResult<int>> UploadStagingFilesAsync(
        Guid migrationId, IEnumerable<(string FileName, Stream Stream, string ContentType)> files, CancellationToken ct);

    /// <summary>
    /// Remove a staged file from the migration staging area and unmark the item.
    /// </summary>
    Task<ServiceResult> UnstageMigrationItemAsync(Guid migrationId, Guid itemId, CancellationToken ct);

    /// <summary>
    /// Bulk delete migrations by filter: "completed", "draft", or "all".
    /// Returns the number of migrations deleted.
    /// </summary>
    Task<ServiceResult<int>> BulkDeleteAsync(string filter, CancellationToken ct);
}
