using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Repository interface for Migration and MigrationItem entities.
/// </summary>
public interface IMigrationRepository
{
    /// <summary>
    /// Get a migration by ID.
    /// </summary>
    Task<Migration?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get a migration by ID with its items.
    /// </summary>
    Task<Migration?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// List all migrations ordered by creation date descending, with pagination.
    /// </summary>
    Task<List<Migration>> ListAsync(int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Get total count of migrations.
    /// </summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Create a new migration.
    /// </summary>
    Task<Migration> CreateAsync(Migration migration, CancellationToken ct = default);

    /// <summary>
    /// Update an existing migration.
    /// </summary>
    Task UpdateAsync(Migration migration, CancellationToken ct = default);

    /// <summary>
    /// Delete a migration and all its items.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Add items to a migration in bulk.
    /// </summary>
    Task AddItemsAsync(IEnumerable<MigrationItem> items, CancellationToken ct = default);

    /// <summary>
    /// Get items for a migration with optional status filter and pagination.
    /// </summary>
    Task<List<MigrationItem>> GetItemsAsync(
        Guid migrationId, string? statusFilter, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Get total count of items for a migration with optional status filter.
    /// </summary>
    Task<int> CountItemsAsync(Guid migrationId, string? statusFilter, CancellationToken ct = default);

    /// <summary>
    /// Get a single migration item by ID.
    /// </summary>
    Task<MigrationItem?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Update a migration item.
    /// </summary>
    Task UpdateItemAsync(MigrationItem item, CancellationToken ct = default);

    /// <summary>
    /// Get all pending items for a migration (for batch processing).
    /// </summary>
    Task<List<MigrationItem>> GetPendingItemsAsync(Guid migrationId, CancellationToken ct = default);

    /// <summary>
    /// Get all failed items for a migration (for retry).
    /// </summary>
    Task<List<MigrationItem>> GetFailedItemsAsync(Guid migrationId, CancellationToken ct = default);

    /// <summary>
    /// Get current item status counts for a migration.
    /// </summary>
    Task<MigrationItemCounts> GetItemCountsAsync(Guid migrationId, CancellationToken ct = default);

    /// <summary>
    /// Remove all items for a migration.
    /// </summary>
    Task RemoveAllItemsAsync(Guid migrationId, CancellationToken ct = default);

    /// <summary>
    /// Mark items as file-staged for the given migration where the filename matches.
    /// Returns the number of items that were marked.
    /// </summary>
    Task<int> MarkItemsStagedAsync(Guid migrationId, IEnumerable<string> fileNames, CancellationToken ct = default);

    /// <summary>
    /// Delete all migrations matching the given statuses. Returns the number deleted.
    /// </summary>
    Task<int> DeleteByStatusAsync(IReadOnlyList<MigrationStatus> statuses, CancellationToken ct = default);
}

/// <summary>
/// Aggregated item status counts for a migration.
/// </summary>
public record MigrationItemCounts(
    int Total,
    int Pending,
    int Processing,
    int Succeeded,
    int Failed,
    int Skipped,
    int Staged,
    int StagedPending);
