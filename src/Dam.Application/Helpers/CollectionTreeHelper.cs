using Dam.Application.Dtos;
using Dam.Domain.Entities;

namespace Dam.Application.Helpers;

/// <summary>
/// Helpers for building and traversing hierarchical collection trees.
/// </summary>
public static class CollectionTreeHelper
{
    /// <summary>
    /// Builds a hierarchical CollectionAccessDto tree from a flat list of collections.
    /// </summary>
    public static CollectionAccessDto BuildAccessTree(
        Collection collection,
        List<Collection> allCollections,
        Dictionary<string, string> userNames,
        Dictionary<string, string>? userEmails = null,
        HashSet<string>? adminUserIds = null)
    {
        var children = allCollections.Where(c => c.ParentId == collection.Id).ToList();

        return new CollectionAccessDto
        {
            Id = collection.Id,
            Name = collection.Name,
            Description = collection.Description,
            ParentId = collection.ParentId,
            Acls = collection.Acls.Select(a => new CollectionAclResponseDto
            {
                Id = a.Id,
                PrincipalType = a.PrincipalType,
                PrincipalId = a.PrincipalId,
                PrincipalName = a.PrincipalType == "user" && userNames.TryGetValue(a.PrincipalId, out var name)
                    ? name
                    : a.PrincipalType == "user" ? $"Deleted User ({a.PrincipalId[..Math.Min(8, a.PrincipalId.Length)]})" : a.PrincipalId,
                PrincipalEmail = a.PrincipalType == "user" && userEmails != null && userEmails.TryGetValue(a.PrincipalId, out var email) ? email : null,
                Role = a.Role,
                IsSystemAdmin = a.PrincipalType == "user" && adminUserIds != null && adminUserIds.Contains(a.PrincipalId)
            }).ToList(),
            Children = children.Select(c => BuildAccessTree(c, allCollections, userNames, userEmails, adminUserIds)).ToList()
        };
    }

    /// <summary>
    /// Flattens a hierarchical CollectionAccessDto tree into a flat list with depth tracking.
    /// </summary>
    public static List<FlatCollection> Flatten(List<CollectionAccessDto>? collections, int depth = 0)
    {
        var result = new List<FlatCollection>();
        if (collections == null) return result;

        foreach (var col in collections)
        {
            result.Add(new FlatCollection { Id = col.Id, Name = col.Name, Depth = depth });
            result.AddRange(Flatten(col.Children, depth + 1));
        }
        return result;
    }

    /// <summary>
    /// Flattens a hierarchical CollectionAccessDto tree as an enumerable (no depth tracking).
    /// </summary>
    public static IEnumerable<CollectionAccessDto> FlattenAll(List<CollectionAccessDto> collections)
    {
        foreach (var collection in collections)
        {
            yield return collection;
            foreach (var child in FlattenAll(collection.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>
    /// Recursively searches a collection tree for a specific collection by ID.
    /// </summary>
    public static CollectionAccessDto? FindById(List<CollectionAccessDto>? collections, Guid id)
    {
        if (collections == null) return null;

        foreach (var col in collections)
        {
            if (col.Id == id) return col;
            var found = FindById(col.Children, id);
            if (found != null) return found;
        }
        return null;
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
