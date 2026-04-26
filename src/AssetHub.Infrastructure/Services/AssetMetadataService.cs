using System.Text.RegularExpressions;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for asset-metadata commands: 5 repos + auth + scoped CurrentUser + logger. Collapsing to a holder relocates the parameter count without changing intent.")]
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
            .Where(s => SchemaApplies(s, assetType, collectionSet))
            .SelectMany(s => s.Fields)
            .Select(f => f.Id)
            .ToHashSet();
    }

    private static bool SchemaApplies(MetadataSchema s, AssetType assetType, HashSet<Guid> collectionSet)
    {
        return s.Scope switch
        {
            MetadataSchemaScope.Global => true,
            MetadataSchemaScope.AssetType => s.AssetType == assetType,
            MetadataSchemaScope.Collection => s.CollectionId is { } cid && collectionSet.Contains(cid),
            _ => false
        };
    }

    private async Task<ServiceResult> ValidateValuesAsync(
        Dictionary<Guid, MetadataField> applicableFields,
        List<SetMetadataValueDto> values,
        CancellationToken ct)
    {
        var error = CheckDuplicates(values)
            ?? CheckUnknownFields(values, applicableFields)
            ?? CheckValueShapes(values, applicableFields)
            ?? CheckRequiredFields(values, applicableFields)
            ?? await CheckTaxonomyOwnershipAsync(values, applicableFields, ct);
        return error is null ? ServiceResult.Success : error;
    }

    private static ServiceError? CheckDuplicates(List<SetMetadataValueDto> values)
    {
        var duplicate = values.GroupBy(v => v.MetadataFieldId)
            .FirstOrDefault(g => g.Count() > 1)?.Key;
        return duplicate is null
            ? null
            : ServiceError.BadRequest($"Field {duplicate} appears more than once");
    }

    private static ServiceError? CheckUnknownFields(
        List<SetMetadataValueDto> values, Dictionary<Guid, MetadataField> applicableFields)
    {
        var unknown = values.FirstOrDefault(v => !applicableFields.ContainsKey(v.MetadataFieldId));
        return unknown is null
            ? null
            : ServiceError.BadRequest($"Metadata field {unknown.MetadataFieldId} does not apply to this asset");
    }

    private static ServiceError? CheckValueShapes(
        List<SetMetadataValueDto> values, Dictionary<Guid, MetadataField> applicableFields)
    {
        foreach (var v in values)
        {
            var err = ValidateValueShape(applicableFields[v.MetadataFieldId], v);
            if (err is not null) return err;
        }
        return null;
    }

    private static ServiceError? CheckRequiredFields(
        List<SetMetadataValueDto> values, Dictionary<Guid, MetadataField> applicableFields)
    {
        var valuedFields = values.ToDictionary(v => v.MetadataFieldId);
        foreach (var field in applicableFields.Values.Where(f => f.Required))
        {
            if (!valuedFields.TryGetValue(field.Id, out var v) || !HasValue(field, v))
                return ServiceError.BadRequest($"Required field '{field.Key}' is missing");
        }
        return null;
    }

    private async Task<ServiceError?> CheckTaxonomyOwnershipAsync(
        List<SetMetadataValueDto> values,
        Dictionary<Guid, MetadataField> applicableFields,
        CancellationToken ct)
    {
        var termIds = values
            .Where(v => v.ValueTaxonomyTermId.HasValue)
            .Select(v => v.ValueTaxonomyTermId!.Value)
            .Distinct()
            .ToList();
        if (termIds.Count == 0) return null;

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
        return null;
    }

    private static ServiceError? ValidateValueShape(MetadataField field, SetMetadataValueDto v)
    {
        // Null value is allowed (represents clearing the field) — required-ness is checked separately.
        if (!HasValue(field, v)) return null;

        return field.Type switch
        {
            MetadataFieldType.Text or MetadataFieldType.LongText => ValidateText(field, v),
            MetadataFieldType.Url => ValidateUrl(field, v),
            MetadataFieldType.Select => ValidateSelect(field, v),
            MetadataFieldType.MultiSelect => ValidateMultiSelect(field, v),
            MetadataFieldType.Boolean => ValidateBoolean(field, v),
            MetadataFieldType.Number or MetadataFieldType.Decimal => ValidateNumeric(field, v),
            MetadataFieldType.Date or MetadataFieldType.DateTime => ValidateDate(field, v),
            MetadataFieldType.Taxonomy => ValidateTaxonomy(field, v),
            _ => ServiceError.BadRequest($"Field '{field.Key}' has an unsupported type")
        };
    }

    private static ServiceError? ValidateText(MetadataField field, SetMetadataValueDto v)
    {
        if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects text");
        if (field.MaxLength.HasValue && v.ValueText.Length > field.MaxLength.Value)
            return ServiceError.BadRequest($"Field '{field.Key}' exceeds max length {field.MaxLength}");
        if (string.IsNullOrEmpty(field.PatternRegex)) return null;
        return ValidatePattern(field, v.ValueText);
    }

    private static ServiceError? ValidatePattern(MetadataField field, string value)
    {
        // Admin-authored patterns are trusted-ish but still run against user-supplied values.
        // A catastrophic-backtracking pattern + crafted input would otherwise hang the request
        // thread (Sonar S6444 / ReDoS). A 100 ms ceiling is generous for metadata validation
        // and short enough that an attacker can't amplify into a DoS.
        try
        {
            return Regex.IsMatch(value, field.PatternRegex!, RegexOptions.None, TimeSpan.FromMilliseconds(100))
                ? null
                : ServiceError.BadRequest($"Field '{field.Key}' does not match required pattern");
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

    private static ServiceError? ValidateUrl(MetadataField field, SetMetadataValueDto v)
    {
        if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects a URL");
        if (!Uri.TryCreate(v.ValueText, UriKind.Absolute, out _))
            return ServiceError.BadRequest($"Field '{field.Key}' must be a valid absolute URL");
        return null;
    }

    private static ServiceError? ValidateSelect(MetadataField field, SetMetadataValueDto v)
    {
        if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects a value");
        if (field.SelectOptions.Count > 0 && !field.SelectOptions.Contains(v.ValueText))
            return ServiceError.BadRequest($"Field '{field.Key}' value '{v.ValueText}' is not a valid option");
        return null;
    }

    private static ServiceError? ValidateMultiSelect(MetadataField field, SetMetadataValueDto v)
    {
        if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' expects values");
        if (field.SelectOptions.Count == 0) return null;

        var parts = v.ValueText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var unknown = parts.FirstOrDefault(p => !field.SelectOptions.Contains(p));
        return unknown is null
            ? null
            : ServiceError.BadRequest($"Field '{field.Key}' value '{unknown}' is not a valid option");
    }

    private static ServiceError? ValidateBoolean(MetadataField field, SetMetadataValueDto v)
    {
        if (v.ValueText is null) return ServiceError.BadRequest($"Field '{field.Key}' must be 'true' or 'false'");
        var isBool = v.ValueText.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.ValueText.Equals("false", StringComparison.OrdinalIgnoreCase);
        return isBool ? null : ServiceError.BadRequest($"Field '{field.Key}' must be 'true' or 'false'");
    }

    private static ServiceError? ValidateNumeric(MetadataField field, SetMetadataValueDto v)
    {
        if (!v.ValueNumeric.HasValue) return ServiceError.BadRequest($"Field '{field.Key}' expects a number");
        if (field.NumericMin.HasValue && v.ValueNumeric.Value < field.NumericMin.Value)
            return ServiceError.BadRequest($"Field '{field.Key}' must be >= {field.NumericMin}");
        if (field.NumericMax.HasValue && v.ValueNumeric.Value > field.NumericMax.Value)
            return ServiceError.BadRequest($"Field '{field.Key}' must be <= {field.NumericMax}");
        return null;
    }

    private static ServiceError? ValidateDate(MetadataField field, SetMetadataValueDto v)
        => v.ValueDate.HasValue
            ? null
            : ServiceError.BadRequest($"Field '{field.Key}' expects a date");

    private static ServiceError? ValidateTaxonomy(MetadataField field, SetMetadataValueDto v)
        => v.ValueTaxonomyTermId.HasValue
            ? null
            : ServiceError.BadRequest($"Field '{field.Key}' expects a taxonomy term");

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
