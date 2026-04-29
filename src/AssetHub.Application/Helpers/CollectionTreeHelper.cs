using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Helpers for mapping collections to DTOs.
/// </summary>
public static class CollectionTreeHelper
{
    /// <summary>
    /// Maps a Collection entity to a CollectionAccessDto.
    /// </summary>
    public static CollectionAccessDto ToAccessDto(
        Collection collection,
        Dictionary<string, string> userNames,
        Dictionary<string, string>? userEmails = null,
        HashSet<string>? adminUserIds = null)
    {
        return new CollectionAccessDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            BrandId = collection.BrandId,
            Acls = collection.Acls.Select(a =>
            {
                var principalName = a.PrincipalId;
                if (a.PrincipalType == PrincipalType.User)
                {
                    principalName = userNames.TryGetValue(a.PrincipalId, out var name)
                        ? name
                        : $"Deleted User ({a.PrincipalId[..Math.Min(8, a.PrincipalId.Length)]})";
                }

                return new CollectionAclResponseDto
                {
                    Id = a.Id,
                    PrincipalType = a.PrincipalType.ToDbString(),
                    PrincipalId = a.PrincipalId,
                    PrincipalName = principalName,
                    PrincipalEmail = a.PrincipalType == PrincipalType.User && userEmails is not null && userEmails.TryGetValue(a.PrincipalId, out var email) ? email : null,
                    Role = a.Role.ToDbString(),
                    IsSystemAdmin = a.PrincipalType == PrincipalType.User && adminUserIds is not null && adminUserIds.Contains(a.PrincipalId)
                };
            }).ToList()
        };
    }

    /// <summary>
    /// Converts a list of CollectionAccessDto to FlatCollection list for display.
    /// </summary>
    public static List<FlatCollection> Flatten(List<CollectionAccessDto>? collections)
    {
        if (collections is null) return new();
        return collections.Select(col => new FlatCollection { Id = col.Id, Name = col.Name, Depth = 0 }).ToList();
    }

    /// <summary>
    /// Returns the collections as-is (no flattening needed with flat structure).
    /// </summary>
    public static IEnumerable<CollectionAccessDto> FlattenAll(List<CollectionAccessDto> collections)
    {
        return collections;
    }

    /// <summary>
    /// Searches a collection list for a specific collection by ID.
    /// </summary>
    public static CollectionAccessDto? FindById(List<CollectionAccessDto>? collections, Guid id)
    {
        return collections?.FirstOrDefault(c => c.Id == id);
    }
}

/// <summary>
/// A flattened collection entry with depth for indented display in select lists.
/// </summary>
public class FlatCollection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Depth { get; set; }
}
