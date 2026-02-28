using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Centralised mapping from <see cref="Collection"/> entities to
/// <see cref="CollectionResponseDto"/>, including per-user role resolution.
/// </summary>
public static class CollectionMapper
{
    /// <summary>
    /// Maps a single collection entity to a response DTO with role information.
    /// </summary>
    public static async Task<CollectionResponseDto> ToDtoAsync(
        Collection collection,
        string userId,
        ICollectionAuthorizationService authService,
        int assetCount = 0,
        CancellationToken ct = default)
    {
        var role = await authService.GetUserRoleAsync(userId, collection.Id, ct);

        return new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            CreatedAt = collection.CreatedAt,
            CreatedByUserId = collection.CreatedByUserId,
            UserRole = role ?? "none",
            AssetCount = assetCount
        };
    }

    /// <summary>
    /// Maps a batch of collection entities to response DTOs.
    /// Uses batch role resolution to avoid N+1 database queries.
    /// </summary>
    public static async Task<List<CollectionResponseDto>> ToDtoListAsync(
        IEnumerable<Collection> collections,
        string userId,
        ICollectionAuthorizationService authService,
        Dictionary<Guid, int>? assetCounts = null,
        CancellationToken ct = default)
    {
        var collectionList = collections.ToList();
        if (collectionList.Count == 0) return new();

        var collectionIds = collectionList.Select(c => c.Id).ToList();

        // Batch-resolve roles for all collections in one operation
        var roles = await authService.GetUserRolesAsync(userId, collectionIds, ct);

        var results = new List<CollectionResponseDto>(collectionList.Count);
        foreach (var c in collectionList)
        {
            var role = roles.GetValueOrDefault(c.Id);
            var count = assetCounts?.GetValueOrDefault(c.Id) ?? 0;

            results.Add(new CollectionResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                CreatedAt = c.CreatedAt,
                CreatedByUserId = c.CreatedByUserId,
                UserRole = role ?? "none",
                AssetCount = count
            });
        }
        return results;
    }
}
