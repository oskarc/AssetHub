namespace Dam.Ui.Tests.Helpers;

/// <summary>
/// Factory methods for creating test DTOs with sensible defaults.
/// </summary>
public static class TestData
{
    private static int _counter = 0;

    public static AssetResponseDto CreateAsset(
        Guid? id = null,
        string title = "Test Asset",
        string assetType = "image",
        string status = "ready",
        string contentType = "image/jpeg",
        long sizeBytes = 1024000,
        string? description = null,
        List<string>? tags = null,
        string? thumbObjectKey = null,
        string userRole = "viewer")
    {
        var counter = Interlocked.Increment(ref _counter);
        return new AssetResponseDto
        {
            Id = id ?? Guid.NewGuid(),
            Title = $"{title}-{counter}",
            AssetType = assetType,
            Status = status,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Description = description,
            Tags = tags ?? [],
            ThumbObjectKey = thumbObjectKey,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = "user-1",
            UpdatedAt = DateTime.UtcNow,
            UserRole = userRole,
            MetadataJson = new Dictionary<string, object>()
        };
    }

    public static List<AssetResponseDto> CreateAssets(int count, string assetType = "image", string userRole = "viewer")
    {
        return Enumerable.Range(0, count)
            .Select(i => CreateAsset(
                title: $"Asset {i}",
                assetType: assetType,
                userRole: userRole))
            .ToList();
    }

    public static AssetListResponse CreateAssetListResponse(
        Guid? collectionId = null,
        int count = 5,
        int total = -1,
        string userRole = "viewer")
    {
        var items = CreateAssets(count, userRole: userRole);
        return new AssetListResponse
        {
            CollectionId = collectionId ?? Guid.NewGuid(),
            Total = total >= 0 ? total : count,
            Items = items
        };
    }

    public static CollectionResponseDto CreateCollection(
        Guid? id = null,
        string name = "Test Collection",
        string? description = null,
        Guid? parentId = null,
        string userRole = "manager",
        bool isRoleInherited = false,
        int childCount = 0,
        int assetCount = 0)
    {
        var counter = Interlocked.Increment(ref _counter);
        return new CollectionResponseDto
        {
            Id = id ?? Guid.NewGuid(),
            Name = $"{name}-{counter}",
            Description = description,
            ParentId = parentId,
            UserRole = userRole,
            IsRoleInherited = isRoleInherited,
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            CreatedByUserId = "user-1",
            ChildCount = childCount,
            AssetCount = assetCount
        };
    }

    public static List<CollectionResponseDto> CreateCollections(int count, string userRole = "manager")
    {
        return Enumerable.Range(0, count)
            .Select(i => CreateCollection(name: $"Collection {i}", userRole: userRole))
            .ToList();
    }

    public static CollectionAclResponseDto CreateAclEntry(
        Guid? id = null,
        string principalId = "user-1",
        string? principalName = "testuser",
        string role = "viewer",
        string principalType = "user")
    {
        return new CollectionAclResponseDto
        {
            Id = id ?? Guid.NewGuid(),
            PrincipalType = principalType,
            PrincipalId = principalId,
            PrincipalName = principalName,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ShareResponseDto CreateShareResponse(
        Guid? id = null,
        string scopeType = "asset",
        Guid? scopeId = null,
        string? password = "TestPass123!")
    {
        return new ShareResponseDto
        {
            Id = id ?? Guid.NewGuid(),
            ScopeType = scopeType,
            ScopeId = scopeId ?? Guid.NewGuid(),
            Token = "test-token-abc",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            PermissionsJson = new Dictionary<string, bool> { { "download", true } },
            ShareUrl = "https://example.com/share/test-token-abc",
            Password = password
        };
    }

    public static UserSearchResultDto CreateUserSearchResult(
        string? id = null,
        string username = "searchuser",
        string? email = null)
    {
        var counter = Interlocked.Increment(ref _counter);
        return new UserSearchResultDto
        {
            Id = id ?? $"user-{counter}",
            Username = $"{username}-{counter}",
            Email = email ?? $"{username}-{counter}@example.com"
        };
    }

    public static AssetCollectionDto CreateAssetCollection(
        Guid? id = null,
        string name = "Asset Collection")
    {
        return new AssetCollectionDto
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = null
        };
    }
}
