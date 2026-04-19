using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

public sealed class MetadataSchemaQueryService(
    IMetadataSchemaRepository repo) : IMetadataSchemaQueryService
{
    public async Task<ServiceResult<List<MetadataSchemaDto>>> GetAllAsync(CancellationToken ct)
    {
        var schemas = await repo.GetAllAsync(ct);
        return schemas.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<MetadataSchemaDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var schema = await repo.GetByIdAsync(id, ct);
        if (schema is null) return ServiceError.NotFound("Metadata schema not found");
        return ToDto(schema);
    }

    public async Task<ServiceResult<List<MetadataSchemaDto>>> GetApplicableAsync(string? assetType, Guid? collectionId, CancellationToken ct)
    {
        AssetType? parsedType = null;
        if (!string.IsNullOrEmpty(assetType))
        {
            if (!DomainEnumExtensions.IsValidAssetType(assetType))
                return ServiceError.BadRequest($"Unknown asset type: {assetType}");
            parsedType = assetType.ToAssetType();
        }

        var schemas = await repo.GetApplicableAsync(parsedType, collectionId, ct);
        return schemas.Select(ToDto).ToList();
    }

    internal static MetadataSchemaDto ToDto(MetadataSchema s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        Scope = s.Scope.ToDbString(),
        AssetType = s.AssetType?.ToDbString(),
        CollectionId = s.CollectionId,
        Version = s.Version,
        CreatedAt = s.CreatedAt,
        CreatedByUserId = s.CreatedByUserId,
        Fields = s.Fields.OrderBy(f => f.SortOrder).Select(ToFieldDto).ToList()
    };

    internal static MetadataFieldDto ToFieldDto(MetadataField f) => new()
    {
        Id = f.Id,
        Key = f.Key,
        Label = f.Label,
        LabelSv = f.LabelSv,
        Type = f.Type.ToDbString(),
        Required = f.Required,
        Searchable = f.Searchable,
        Facetable = f.Facetable,
        PatternRegex = f.PatternRegex,
        MaxLength = f.MaxLength,
        NumericMin = f.NumericMin,
        NumericMax = f.NumericMax,
        SelectOptions = f.SelectOptions.Count > 0 ? f.SelectOptions : null,
        TaxonomyId = f.TaxonomyId,
        SortOrder = f.SortOrder
    };
}
