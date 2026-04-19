using System.Text.RegularExpressions;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class AssetMetadataService(
    IAssetMetadataRepository metadataRepo,
    IMetadataSchemaRepository schemaRepo,
    ITaxonomyRepository taxonomyRepo,
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionAuthorizationService authService,
    CurrentUser currentUser,
    ILogger<AssetMetadataService> logger) : IAssetMetadataService
{
    public async Task<ServiceResult<List<AssetMetadataValueDto>>> GetByAssetIdAsync(Guid assetId, CancellationToken ct)
    {
        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");

        var values = await metadataRepo.GetByAssetIdAsync(assetId, ct);
        return values.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<List<AssetMetadataValueDto>>> SetAsync(Guid assetId, SetAssetMetadataDto dto, CancellationToken ct)
    {
        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return ServiceError.NotFound("Asset not found");

        if (!await CanEditAssetAsync(assetId, ct))
            return ServiceError.Forbidden("You do not have permission to edit this asset's metadata");

        var applicableFields = await GetApplicableFieldsAsync(asset, assetId, ct);
        var validation = await ValidateValuesAsync(applicableFields, dto.Values, ct);
        if (!validation.IsSuccess) return validation.Error!;

        var domainValues = dto.Values.Select(v => new AssetMetadataValue
        {
            AssetId = assetId,
            MetadataFieldId = v.MetadataFieldId,
            ValueText = v.ValueText,
            ValueNumeric = v.ValueNumeric,
            ValueDate = v.ValueDate,
            ValueTaxonomyTermId = v.ValueTaxonomyTermId
        }).ToList();

        await metadataRepo.ReplaceForAssetAsync(assetId, domainValues, ct);
        logger.LogInformation("User {UserId} set {Count} metadata values on asset {AssetId}", currentUser.UserId, domainValues.Count, assetId);

        var saved = await metadataRepo.GetByAssetIdAsync(assetId, ct);
        return saved.Select(ToDto).ToList();
    }

    public async Task<ServiceResult> BulkSetAsync(BulkSetAssetMetadataDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Only administrators can bulk-set metadata");

        // Hoist the schema lookup once for the whole batch.
        var allSchemas = await schemaRepo.GetAllAsync(ct);
        var allFieldsById = allSchemas
            .SelectMany(s => s.Fields)
            .ToDictionary(f => f.Id);

        // Validate everything up front so we don't write a partial batch before discovering a bad input.
        var batch = new List<(Guid AssetId, List<AssetMetadataValue> Values)>(dto.Assets.Count);

        foreach (var entry in dto.Assets)
        {
            var asset = await assetRepo.GetByIdAsync(entry.AssetId, ct);
            if (asset is null)
                return ServiceError.NotFound($"Asset {entry.AssetId} not found");

            var applicableFieldIds = await GetApplicableFieldIdsAsync(asset, entry.AssetId, allSchemas, ct);
            var applicableFields = applicableFieldIds
                .Select(id => allFieldsById[id])
                .ToDictionary(f => f.Id);

            var validation = await ValidateValuesAsync(applicableFields, entry.Values, ct);
            if (!validation.IsSuccess) return validation.Error!;

            batch.Add((entry.AssetId, entry.Values.Select(v => new AssetMetadataValue
            {
                AssetId = entry.AssetId,
                MetadataFieldId = v.MetadataFieldId,
                ValueText = v.ValueText,
                ValueNumeric = v.ValueNumeric,
                ValueDate = v.ValueDate,
                ValueTaxonomyTermId = v.ValueTaxonomyTermId
            }).ToList()));
        }

        await metadataRepo.ReplaceForAssetsAsync(batch, ct);
        logger.LogInformation("Admin {UserId} bulk-set metadata for {Count} assets", currentUser.UserId, batch.Count);
        return ServiceResult.Success;
    }

    private async Task<bool> CanEditAssetAsync(Guid assetId, CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin) return true;

        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        if (collectionIds.Count == 0) return false;

        var accessible = await authService.FilterAccessibleAsync(
            currentUser.UserId, collectionIds, RoleHierarchy.Roles.Contributor, ct);
        return accessible.Count > 0;
    }

    private async Task<Dictionary<Guid, MetadataField>> GetApplicableFieldsAsync(
        Asset asset, Guid assetId, CancellationToken ct)
    {
        var schemas = await schemaRepo.GetAllAsync(ct);
        var fieldIds = await GetApplicableFieldIdsAsync(asset, assetId, schemas, ct);
        return schemas
            .SelectMany(s => s.Fields)
            .Where(f => fieldIds.Contains(f.Id))
            .ToDictionary(f => f.Id);
    }

    private async Task<HashSet<Guid>> GetApplicableFieldIdsAsync(
        Asset asset, Guid assetId, List<MetadataSchema> allSchemas, CancellationToken ct)
    {
        var collectionIds = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var collectionSet = collectionIds.ToHashSet();
        var assetType = asset.AssetType;

        return allSchemas
            .Where(s =>
                s.Scope == MetadataSchemaScope.Global
                || (s.Scope == MetadataSchemaScope.AssetType && s.AssetType == assetType)
                || (s.Scope == MetadataSchemaScope.Collection && s.CollectionId is { } cid && collectionSet.Contains(cid)))
            .SelectMany(s => s.Fields)
            .Select(f => f.Id)
            .ToHashSet();
    }

    private async Task<ServiceResult> ValidateValuesAsync(
        Dictionary<Guid, MetadataField> applicableFields,
        List<SetMetadataValueDto> values,
        CancellationToken ct)
    {
        // Duplicate field check — same field can't appear twice in a single set.
        var seenFieldIds = new HashSet<Guid>();
        foreach (var v in values)
        {
            if (!seenFieldIds.Add(v.MetadataFieldId))
                return ServiceError.BadRequest($"Field {v.MetadataFieldId} appears more than once");
        }

        // All supplied fields must apply to this asset.
        foreach (var v in values)
        {
            if (!applicableFields.ContainsKey(v.MetadataFieldId))
                return ServiceError.BadRequest($"Metadata field {v.MetadataFieldId} does not apply to this asset");
        }

        // Per-value shape and constraint checks.
        foreach (var v in values)
        {
            var field = applicableFields[v.MetadataFieldId];
            var err = ValidateValueShape(field, v);
            if (err is not null) return err;
        }

        // Required-field check — every required field in an applicable schema must have a value.
        var valuedFields = values.ToDictionary(v => v.MetadataFieldId);
        foreach (var field in applicableFields.Values.Where(f => f.Required))
        {
            if (!valuedFields.TryGetValue(field.Id, out var v) || !HasValue(field, v))
                return ServiceError.BadRequest($"Required field '{field.Key}' is missing");
        }

        // Taxonomy-term ownership check — term must belong to the field's taxonomy.
        var termIds = values
            .Where(v => v.ValueTaxonomyTermId.HasValue)
            .Select(v => v.ValueTaxonomyTermId!.Value)
            .Distinct()
            .ToList();
        if (termIds.Count > 0)
        {
            var terms = (await taxonomyRepo.GetTermsByIdsAsync(termIds, ct))
                .ToDictionary(t => t.Id);

            foreach (var v in values.Where(v => v.ValueTaxonomyTermId.HasValue))
            {
                var field = applicableFields[v.MetadataFieldId];
                if (field.Type != MetadataFieldType.Taxonomy) continue;

                if (!terms.TryGetValue(v.ValueTaxonomyTermId!.Value, out var term))
                    return ServiceError.BadRequest($"Taxonomy term {v.ValueTaxonomyTermId} does not exist");

                if (field.TaxonomyId is null || term.TaxonomyId != field.TaxonomyId)
                    return ServiceError.BadRequest($"Term {term.Id} does not belong to taxonomy {field.TaxonomyId} required by field '{field.Key}'");
            }
        }

        return ServiceResult.Success;
    }

    private static ServiceError? ValidateValueShape(MetadataField field, SetMetadataValueDto v)
    {
        // Null value is allowed (represents clearing the field) — required-ness is checked separately.
        if (!HasValue(field, v)) return null;

        switch (field.Type)
        {
            case MetadataFieldType.Text:
            case MetadataFieldType.LongText:
                if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects text");
                if (field.MaxLength.HasValue && v.ValueText.Length > field.MaxLength.Value)
                    return ServiceError.BadRequest($"Field '{field.Key}' exceeds max length {field.MaxLength}");
                if (!string.IsNullOrEmpty(field.PatternRegex))
                {
                    // Admin-authored patterns are trusted-ish but still run against user-supplied values.
                    // A catastrophic-backtracking pattern + crafted input would otherwise hang the request
                    // thread (Sonar S6444 / ReDoS). A 100 ms ceiling is generous for metadata validation
                    // and short enough that an attacker can't amplify into a DoS.
                    try
                    {
                        if (!Regex.IsMatch(v.ValueText, field.PatternRegex, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                            return ServiceError.BadRequest($"Field '{field.Key}' does not match required pattern");
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return ServiceError.BadRequest($"Field '{field.Key}' pattern evaluation timed out");
                    }
                    catch (ArgumentException)
                    {
                        // Schema admin saved a malformed pattern — surface it cleanly instead of 500ing.
                        return ServiceError.BadRequest($"Field '{field.Key}' has an invalid pattern configured");
                    }
                }
                return null;

            case MetadataFieldType.Url:
                if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects a URL");
                if (!Uri.TryCreate(v.ValueText, UriKind.Absolute, out _))
                    return ServiceError.BadRequest($"Field '{field.Key}' must be a valid absolute URL");
                return null;

            case MetadataFieldType.Select:
                if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects a value");
                if (field.SelectOptions.Count > 0 && !field.SelectOptions.Contains(v.ValueText))
                    return ServiceError.BadRequest($"Field '{field.Key}' value '{v.ValueText}' is not a valid option");
                return null;

            case MetadataFieldType.MultiSelect:
                if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects values");
                if (field.SelectOptions.Count > 0)
                {
                    var parts = v.ValueText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (!field.SelectOptions.Contains(part))
                            return ServiceError.BadRequest($"Field '{field.Key}' value '{part}' is not a valid option");
                    }
                }
                return null;

            case MetadataFieldType.Boolean:
                if (v.ValueText is null || !(v.ValueText.Equals("true", StringComparison.OrdinalIgnoreCase) || v.ValueText.Equals("false", StringComparison.OrdinalIgnoreCase)))
                    return ServiceError.BadRequest($"Field '{field.Key}' must be 'true' or 'false'");
                return null;

            case MetadataFieldType.Number:
            case MetadataFieldType.Decimal:
                if (!v.ValueNumeric.HasValue) return ServiceError.BadRequest($"Field '{field.Key}' expects a number");
                if (field.NumericMin.HasValue && v.ValueNumeric.Value < field.NumericMin.Value)
                    return ServiceError.BadRequest($"Field '{field.Key}' must be >= {field.NumericMin}");
                if (field.NumericMax.HasValue && v.ValueNumeric.Value > field.NumericMax.Value)
                    return ServiceError.BadRequest($"Field '{field.Key}' must be <= {field.NumericMax}");
                return null;

            case MetadataFieldType.Date:
            case MetadataFieldType.DateTime:
                if (!v.ValueDate.HasValue) return ServiceError.BadRequest($"Field '{field.Key}' expects a date");
                return null;

            case MetadataFieldType.Taxonomy:
                if (!v.ValueTaxonomyTermId.HasValue) return ServiceError.BadRequest($"Field '{field.Key}' expects a taxonomy term");
                return null;

            default:
                return ServiceError.BadRequest($"Field '{field.Key}' has an unsupported type");
        }
    }

    private static bool HasValue(MetadataField field, SetMetadataValueDto v) => field.Type switch
    {
        MetadataFieldType.Number or MetadataFieldType.Decimal => v.ValueNumeric.HasValue,
        MetadataFieldType.Date or MetadataFieldType.DateTime => v.ValueDate.HasValue,
        MetadataFieldType.Taxonomy => v.ValueTaxonomyTermId.HasValue,
        _ => !string.IsNullOrEmpty(v.ValueText)
    };

    private static AssetMetadataValueDto ToDto(AssetMetadataValue v) => new()
    {
        MetadataFieldId = v.MetadataFieldId,
        FieldKey = v.MetadataField?.Key ?? string.Empty,
        FieldLabel = v.MetadataField?.Label ?? string.Empty,
        FieldType = v.MetadataField?.Type.ToDbString() ?? string.Empty,
        ValueText = v.ValueText,
        ValueNumeric = v.ValueNumeric,
        ValueDate = v.ValueDate,
        ValueTaxonomyTermId = v.ValueTaxonomyTermId,
        ValueTaxonomyTermLabel = v.ValueTaxonomyTerm?.Label
    };
}
