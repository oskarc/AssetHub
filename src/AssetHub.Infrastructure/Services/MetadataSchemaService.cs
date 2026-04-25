using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class MetadataSchemaService(
    IMetadataSchemaRepository repo,
    IAssetMetadataRepository metadataRepo,
    ICollectionRepository collectionRepo,
    CurrentUser currentUser,
    ILogger<MetadataSchemaService> logger) : IMetadataSchemaService
{
    public async Task<ServiceResult<MetadataSchemaDto>> CreateAsync(CreateMetadataSchemaDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can manage metadata schemas");

        if (!DomainEnumExtensions.IsValidMetadataSchemaScope(dto.Scope))
            return ServiceError.BadRequest($"Unknown schema scope: {dto.Scope}");

        var scope = dto.Scope.ToMetadataSchemaScope();

        if (scope == MetadataSchemaScope.AssetType && string.IsNullOrEmpty(dto.AssetType))
            return ServiceError.BadRequest("AssetType is required when scope is 'asset_type'");

        if (scope == MetadataSchemaScope.Collection && dto.CollectionId is null)
            return ServiceError.BadRequest("CollectionId is required when scope is 'collection'");

        if (scope == MetadataSchemaScope.Collection && dto.CollectionId is { } cid
            && !await collectionRepo.ExistsAsync(cid, ct))
            return ServiceError.BadRequest($"Collection {cid} does not exist");

        if (await repo.ExistsByNameAsync(dto.Name, ct: ct))
            return ServiceError.Conflict($"A metadata schema named '{dto.Name}' already exists");

        AssetType? assetType = null;
        if (!string.IsNullOrEmpty(dto.AssetType))
        {
            if (!DomainEnumExtensions.IsValidAssetType(dto.AssetType))
                return ServiceError.BadRequest($"Unknown asset type: {dto.AssetType}");
            assetType = dto.AssetType.ToAssetType();
        }

        var fieldValidation = ValidateFields(dto.Fields.Select(f => (f.Key, f.Type, f.TaxonomyId)).ToList());
        if (!fieldValidation.IsSuccess)
            return fieldValidation.Error!;

        var schema = new MetadataSchema
        {
            Name = dto.Name,
            Description = dto.Description,
            Scope = scope,
            AssetType = assetType,
            CollectionId = dto.CollectionId,
            CreatedByUserId = currentUser.UserId,
            Fields = dto.Fields.Select((f, i) => new MetadataField
            {
                Key = f.Key,
                Label = f.Label,
                LabelSv = f.LabelSv,
                Type = f.Type.ToMetadataFieldType(),
                Required = f.Required,
                Searchable = f.Searchable,
                Facetable = f.Facetable,
                PatternRegex = f.PatternRegex,
                MaxLength = f.MaxLength,
                NumericMin = f.NumericMin,
                NumericMax = f.NumericMax,
                SelectOptions = f.SelectOptions ?? [],
                TaxonomyId = f.TaxonomyId,
                SortOrder = f.SortOrder != 0 ? f.SortOrder : i
            }).ToList()
        };

        var created = await repo.CreateAsync(schema, ct);
        logger.LogInformation("Admin {UserId} created metadata schema {SchemaId} '{SchemaName}'", currentUser.UserId, created.Id, created.Name);
        return MetadataSchemaQueryService.ToDto(created);
    }

    public async Task<ServiceResult<MetadataSchemaDto>> UpdateAsync(Guid id, UpdateMetadataSchemaDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can manage metadata schemas");

        var schema = await repo.GetByIdForUpdateAsync(id, ct);
        if (schema is null) return ServiceError.NotFound("Metadata schema not found");

        var nameError = await ApplyNameUpdateAsync(schema, dto, id, ct);
        if (nameError is not null) return nameError;

        if (dto.Description is not null)
            schema.Description = dto.Description;

        if (dto.Fields is not null)
        {
            var fieldsError = ApplyFieldsUpdate(schema, dto.Fields);
            if (fieldsError is not null) return fieldsError;
        }

        var updated = await repo.UpdateAsync(schema, ct);
        logger.LogInformation("Admin {UserId} updated metadata schema {SchemaId}", currentUser.UserId, id);
        return MetadataSchemaQueryService.ToDto(updated);
    }

    private async Task<ServiceError?> ApplyNameUpdateAsync(
        MetadataSchema schema, UpdateMetadataSchemaDto dto, Guid id, CancellationToken ct)
    {
        if (dto.Name is null) return null;
        if (await repo.ExistsByNameAsync(dto.Name, excludeId: id, ct: ct))
            return ServiceError.Conflict($"A metadata schema named '{dto.Name}' already exists");
        schema.Name = dto.Name;
        return null;
    }

    private static ServiceError? ApplyFieldsUpdate(MetadataSchema schema, List<UpdateMetadataFieldDto> fields)
    {
        var fieldValidation = ValidateFields(fields.Select(f => (f.Key, f.Type, f.TaxonomyId)).ToList());
        if (!fieldValidation.IsSuccess) return fieldValidation.Error!;

        // Replace fields: remove old, add new preserving IDs where provided.
        var existingFieldsById = schema.Fields.ToDictionary(f => f.Id);

        schema.Fields = fields.Select((f, i) => MapField(f, i, schema.Id, existingFieldsById)).ToList();
        schema.Version++;
        return null;
    }

    private static MetadataField MapField(
        UpdateMetadataFieldDto f, int index, Guid schemaId,
        Dictionary<Guid, MetadataField> existingFieldsById)
    {
        var field = (f.Id.HasValue && existingFieldsById.TryGetValue(f.Id.Value, out var existing))
            ? existing
            : new MetadataField { Id = Guid.NewGuid(), MetadataSchemaId = schemaId };

        field.Key = f.Key;
        field.Label = f.Label;
        field.LabelSv = f.LabelSv;
        field.Type = f.Type.ToMetadataFieldType();
        field.Required = f.Required;
        field.Searchable = f.Searchable;
        field.Facetable = f.Facetable;
        field.PatternRegex = f.PatternRegex;
        field.MaxLength = f.MaxLength;
        field.NumericMin = f.NumericMin;
        field.NumericMax = f.NumericMax;
        field.SelectOptions = f.SelectOptions ?? [];
        field.TaxonomyId = f.TaxonomyId;
        field.SortOrder = f.SortOrder != 0 ? f.SortOrder : index;
        return field;
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, bool force, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can manage metadata schemas");

        var schema = await repo.GetByIdAsync(id, ct);
        if (schema is null) return ServiceError.NotFound("Metadata schema not found");

        if (!force && await repo.HasMetadataValuesAsync(id, ct))
            return ServiceError.Conflict("Schema has existing asset metadata values. Use force=true to delete anyway.");

        if (force)
            await metadataRepo.DeleteBySchemaIdAsync(id, ct);

        await repo.DeleteAsync(id, ct);
        logger.LogInformation("Admin {UserId} deleted metadata schema {SchemaId}", currentUser.UserId, id);
        return ServiceResult.Success;
    }

    private static ServiceResult ValidateFields(List<(string Key, string Type, Guid? TaxonomyId)> fields)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, type, taxonomyId) in fields)
        {
            if (!DomainEnumExtensions.IsValidMetadataFieldType(type))
                return ServiceError.BadRequest($"Unknown field type: {type}");

            if (!keys.Add(key))
                return ServiceError.BadRequest($"Duplicate field key: {key}");

            if (type == "taxonomy" && taxonomyId is null)
                return ServiceError.BadRequest($"TaxonomyId is required for field '{key}' of type 'taxonomy'");
        }
        return ServiceResult.Success;
    }
}
