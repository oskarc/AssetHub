using AssetHub.Application;
using AssetHub.Application.Helpers;
using AssetHub.Domain.Entities;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// Factory methods for building test entities with sensible defaults.
/// Use With* methods to override specific properties.
/// </summary>
public static class TestData
{
    private static readonly string DefaultUserId = "test-user-001";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Test factory with optional defaults — callers only specify what they need")]
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Test factory with optional defaults — callers only specify what they need")]
    public static Share CreateShare(
        Guid? id = null,
        ShareScopeType scopeType = ShareScopeType.Asset,
        Guid? scopeId = null,
        string? tokenHash = null,
        string? token = null,
        DateTime? expiresAt = null,
        string? passwordHash = null,
        string? createdByUserId = null,
        bool revoked = false)
    {
        // If a plaintext token is provided, compute its hash
        // If tokenHash is provided, use it directly
        // Otherwise, generate a random hash for backward compatibility
        var computedHash = token != null
            ? ShareHelpers.ComputeTokenHash(token)
            : tokenHash ?? $"hash_{Guid.NewGuid():N}";

        return new Share
        {
            Id = id ?? Guid.NewGuid(),
            TokenHash = computedHash,
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

    /// <summary>
    /// Creates a share with a real token for testing. Returns both the share entity and the plaintext token.
    /// </summary>
    public static (Share Share, string Token) CreateShareWithToken(
        Guid? id = null,
        ShareScopeType scopeType = ShareScopeType.Asset,
        Guid? scopeId = null,
        DateTime? expiresAt = null,
        string? passwordHash = null,
        string? createdByUserId = null,
        bool revoked = false)
    {
        var token = ShareHelpers.GenerateToken();
        var share = CreateShare(
            id: id,
            scopeType: scopeType,
            scopeId: scopeId,
            token: token,
            expiresAt: expiresAt,
            passwordHash: passwordHash,
            createdByUserId: createdByUserId,
            revoked: revoked);
        return (share, token);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Test factory with optional defaults — callers only specify what they need")]
    public static MetadataSchema CreateMetadataSchema(
        Guid? id = null,
        string name = "Test Schema",
        string? description = null,
        MetadataSchemaScope scope = MetadataSchemaScope.Global,
        AssetType? assetType = null,
        Guid? collectionId = null,
        int version = 1,
        string? createdByUserId = null,
        List<MetadataField>? fields = null)
    {
        var schemaId = id ?? Guid.NewGuid();
        var resolvedFields = fields ?? new List<MetadataField>();
        foreach (var f in resolvedFields)
            f.MetadataSchemaId = schemaId;

        return new MetadataSchema
        {
            Id = schemaId,
            Name = name,
            Description = description,
            Scope = scope,
            AssetType = assetType,
            CollectionId = collectionId,
            Version = version,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId ?? DefaultUserId,
            Fields = resolvedFields
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Test factory with optional defaults")]
    public static MetadataField CreateMetadataField(
        Guid? id = null,
        Guid? schemaId = null,
        string key = "test_field",
        string label = "Test Field",
        MetadataFieldType type = MetadataFieldType.Text,
        bool required = false,
        int? maxLength = null,
        string? patternRegex = null,
        decimal? numericMin = null,
        decimal? numericMax = null,
        List<string>? selectOptions = null,
        Guid? taxonomyId = null,
        int sortOrder = 0)
    {
        return new MetadataField
        {
            Id = id ?? Guid.NewGuid(),
            MetadataSchemaId = schemaId ?? Guid.NewGuid(),
            Key = key,
            Label = label,
            Type = type,
            Required = required,
            Searchable = true,
            Facetable = false,
            MaxLength = maxLength,
            PatternRegex = patternRegex,
            NumericMin = numericMin,
            NumericMax = numericMax,
            SelectOptions = selectOptions ?? new(),
            TaxonomyId = taxonomyId,
            SortOrder = sortOrder
        };
    }

    public static Taxonomy CreateTaxonomy(
        Guid? id = null,
        string name = "Test Taxonomy",
        string? description = null,
        string? createdByUserId = null,
        List<TaxonomyTerm>? terms = null)
    {
        var taxonomyId = id ?? Guid.NewGuid();
        var resolvedTerms = terms ?? new List<TaxonomyTerm>();
        foreach (var t in resolvedTerms)
            t.TaxonomyId = taxonomyId;

        return new Taxonomy
        {
            Id = taxonomyId,
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId ?? DefaultUserId,
            Terms = resolvedTerms
        };
    }

    public static TaxonomyTerm CreateTaxonomyTerm(
        Guid? id = null,
        Guid? taxonomyId = null,
        Guid? parentTermId = null,
        string label = "Test Term",
        string? slug = null,
        int sortOrder = 0)
    {
        return new TaxonomyTerm
        {
            Id = id ?? Guid.NewGuid(),
            TaxonomyId = taxonomyId ?? Guid.NewGuid(),
            ParentTermId = parentTermId,
            Label = label,
            Slug = slug ?? label.ToLowerInvariant().Replace(' ', '-'),
            SortOrder = sortOrder
        };
    }

    public static AssetMetadataValue CreateAssetMetadataValue(
        Guid? id = null,
        Guid? assetId = null,
        Guid? fieldId = null,
        string? valueText = null,
        decimal? valueNumeric = null,
        DateTime? valueDate = null,
        Guid? valueTaxonomyTermId = null)
    {
        return new AssetMetadataValue
        {
            Id = id ?? Guid.NewGuid(),
            AssetId = assetId ?? Guid.NewGuid(),
            MetadataFieldId = fieldId ?? Guid.NewGuid(),
            ValueText = valueText,
            ValueNumeric = valueNumeric,
            ValueDate = valueDate,
            ValueTaxonomyTermId = valueTaxonomyTermId
        };
    }

    public static ExportPreset CreateExportPreset(
        Guid? id = null,
        string name = "Web Large",
        Action<ExportPreset>? configure = null)
    {
        var preset = new ExportPreset
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Width = 1920,
            Height = 1080,
            FitMode = ExportPresetFitMode.Contain,
            Format = ExportPresetFormat.Jpeg,
            Quality = 85,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = DefaultUserId
        };
        configure?.Invoke(preset);
        return preset;
    }
}
