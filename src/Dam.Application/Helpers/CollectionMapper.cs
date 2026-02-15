using Dam.Application.Dtos;
using Dam.Application.Services;
using Dam.Domain.Entities;

namespace Dam.Application.Helpers;

/// <summary>
/// Centralised mapping from <see cref="Collection"/> entities to
/// <see cref="CollectionResponseDto"/>, including per-user role resolution.
/// Eliminates duplicated mapping code across endpoint handlers.
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
        CancellationToken ct = default)
    {
        var role = await authService.GetUserRoleAsync(userId, collection.Id, ct);
        var isInherited = role != null
            && await authService.IsRoleInheritedAsync(userId, collection.Id, ct);

        return new CollectionResponseDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            CreatedAt = collection.CreatedAt,
            CreatedByUserId = collection.CreatedByUserId,
            UserRole = role ?? "none",
            IsRoleInherited = isInherited
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
        CancellationToken ct = default)
    {
        var collectionList = collections.ToList();
        if (collectionList.Count == 0) return new();

        // Batch-resolve roles for all collections in one operation
        var roles = await authService.GetUserRolesAsync(userId, collectionList.Select(c => c.Id), ct);

        var results = new List<CollectionResponseDto>(collectionList.Count);
        foreach (var c in collectionList)
        {
            var role = roles.GetValueOrDefault(c.Id);
            var isInherited = role != null
                && await authService.IsRoleInheritedAsync(userId, c.Id, ct);

            results.Add(new CollectionResponseDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                ParentId = c.ParentId,
                CreatedAt = c.CreatedAt,
                CreatedByUserId = c.CreatedByUserId,
                UserRole = role ?? "none",
                IsRoleInherited = isInherited
            });
        }
        return results;
    }
}
