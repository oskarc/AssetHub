using AssetHub.Application;
using AssetHub.Domain.Entities;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Factory methods for building test entities with sensible defaults.
/// Use With* methods to override specific properties.
/// </summary>
public static class TestData
{
    private static readonly string DefaultUserId = "test-user-001";

    public static Asset CreateAsset(
        Guid? id = null,
        string title = "Test Asset",
        AssetType assetType = AssetType.Image,
        AssetStatus status = AssetStatus.Ready,
        string? description = null,
        List<string>? tags = null,
        long sizeBytes = 1024,
        string contentType = "image/jpeg",
        string? createdByUserId = null)
    {
        return new Asset
        {
            Id = id ?? Guid.NewGuid(),
            Title = title,
            AssetType = assetType,
            Status = status,
            Description = description,
            Tags = tags ?? new List<string> { "test" },
            MetadataJson = new Dictionary<string, object> { ["source"] = "test" },
            ContentType = contentType,
            SizeBytes = sizeBytes,
            OriginalObjectKey = $"originals/{Guid.NewGuid()}.jpg",
            ThumbObjectKey = status == AssetStatus.Ready ? $"thumbs/{Guid.NewGuid()}.jpg" : null,
            MediumObjectKey = status == AssetStatus.Ready ? $"medium/{Guid.NewGuid()}.jpg" : null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId ?? DefaultUserId,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Collection CreateCollection(
        Guid? id = null,
        string name = "Test Collection",
        string? description = null,
        string? createdByUserId = null)
    {
        return new Collection
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId ?? DefaultUserId
        };
    }

    public static CollectionAcl CreateAcl(
        Guid collectionId,
        string principalId,
        AclRole role = AclRole.Viewer,
        PrincipalType principalType = PrincipalType.User)
    {
        return new CollectionAcl
        {
            Id = Guid.NewGuid(),
            CollectionId = collectionId,
            PrincipalType = principalType,
            PrincipalId = principalId,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static AssetCollection CreateAssetCollection(
        Guid assetId,
        Guid collectionId,
        string? addedByUserId = null)
    {
        return new AssetCollection
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            CollectionId = collectionId,
            AddedAt = DateTime.UtcNow,
            AddedByUserId = addedByUserId ?? DefaultUserId
        };
    }

    public static Share CreateShare(
        Guid? id = null,
        ShareScopeType scopeType = ShareScopeType.Asset,
        Guid? scopeId = null,
        string? tokenHash = null,
        DateTime? expiresAt = null,
        string? passwordHash = null,
        string? createdByUserId = null,
        bool revoked = false)
    {
        return new Share
        {
            Id = id ?? Guid.NewGuid(),
            TokenHash = tokenHash ?? $"hash_{Guid.NewGuid():N}",
            TokenEncrypted = $"enc_{Guid.NewGuid():N}",
            ScopeType = scopeType,
            ScopeId = scopeId ?? Guid.NewGuid(),
            PermissionsJson = new Dictionary<string, bool> { ["download"] = true, ["preview"] = true },
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            RevokedAt = revoked ? DateTime.UtcNow : null,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId ?? DefaultUserId,
            AccessCount = 0
        };
    }

    public static AuditEvent CreateAuditEvent(
        string eventType = "test.event",
        string targetType = Constants.ScopeTypes.Asset,
        Guid? targetId = null,
        string? actorUserId = null)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            TargetType = targetType,
            TargetId = targetId ?? Guid.NewGuid(),
            ActorUserId = actorUserId ?? DefaultUserId,
            CreatedAt = DateTime.UtcNow,
            DetailsJson = new Dictionary<string, object> { ["test"] = true }
        };
    }
}
