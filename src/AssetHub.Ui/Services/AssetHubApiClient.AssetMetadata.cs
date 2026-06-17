using AssetHub.Application;
using AssetHub.Application.Dtos;

namespace AssetHub.Ui.Services;

public sealed partial class AssetHubApiClient
{
    // ─── Asset ↔ Collection membership (multi-collection) ──────────────────

    public async Task<List<AssetCollectionDto>> GetAssetCollectionsAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetAssetCollectionsAsync(assetId, ct);
        return Unwrap(result, "Get asset collections").ToList();
    }

    public async Task<List<AssetDerivativeDto>> GetAssetDerivativesAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetQueryService.GetDerivativesAsync(assetId, ct);
        return Unwrap(result, "Get asset derivatives");
    }

    public async Task AddAssetToCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await assetService.AddToCollectionAsync(assetId, collectionId, ct);
        EnsureSuccess(new ServiceResult { Error = result.Error }, "Add asset to collection");
    }

    public async Task RemoveAssetFromCollectionAsync(Guid assetId, Guid collectionId, CancellationToken ct = default)
    {
        var result = await assetService.RemoveFromCollectionAsync(assetId, collectionId, ct);
        EnsureSuccess(result, "Remove asset from collection");
    }

    // ─── Metadata schemas ──────────────────────────────────────────────────

    public async Task<List<MetadataSchemaDto>> GetMetadataSchemasAsync(CancellationToken ct = default)
    {
        var result = await metadataSchemaQueryService.GetAllAsync(ct);
        return Unwrap(result, "Get metadata schemas");
    }

    public async Task<MetadataSchemaDto?> GetMetadataSchemaAsync(Guid id, CancellationToken ct = default)
    {
        var result = await metadataSchemaQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get metadata schema", 404);
    }

    public async Task<List<MetadataSchemaDto>> GetApplicableMetadataSchemasAsync(string? assetType = null, Guid? collectionId = null, CancellationToken ct = default)
    {
        var result = await metadataSchemaQueryService.GetApplicableAsync(assetType, collectionId, ct);
        return Unwrap(result, "Get applicable metadata schemas");
    }

    public async Task<MetadataSchemaDto> CreateMetadataSchemaAsync(CreateMetadataSchemaDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create metadata schema");
        var result = await metadataSchemaService.CreateAsync(dto, ct);
        return Unwrap(result, "Create metadata schema");
    }

    public async Task<MetadataSchemaDto> UpdateMetadataSchemaAsync(Guid id, UpdateMetadataSchemaDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update metadata schema");
        var result = await metadataSchemaService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update metadata schema");
    }

    public async Task DeleteMetadataSchemaAsync(Guid id, bool force = false, CancellationToken ct = default)
    {
        var result = await metadataSchemaService.DeleteAsync(id, force, ct);
        EnsureSuccess(result, "Delete metadata schema");
    }

    // ─── Taxonomies ────────────────────────────────────────────────────────

    public async Task<List<TaxonomySummaryDto>> GetTaxonomiesAsync(CancellationToken ct = default)
    {
        var result = await taxonomyQueryService.GetAllAsync(ct);
        return Unwrap(result, "Get taxonomies");
    }

    public async Task<TaxonomyDto?> GetTaxonomyAsync(Guid id, CancellationToken ct = default)
    {
        var result = await taxonomyQueryService.GetByIdAsync(id, ct);
        return UnwrapOrNullOn(result, "Get taxonomy", 404);
    }

    public async Task<TaxonomyDto> CreateTaxonomyAsync(CreateTaxonomyDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Create taxonomy");
        var result = await taxonomyService.CreateAsync(dto, ct);
        return Unwrap(result, "Create taxonomy");
    }

    public async Task<TaxonomyDto> UpdateTaxonomyAsync(Guid id, UpdateTaxonomyDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Update taxonomy");
        var result = await taxonomyService.UpdateAsync(id, dto, ct);
        return Unwrap(result, "Update taxonomy");
    }

    public async Task<TaxonomyDto> ReplaceTaxonomyTermsAsync(Guid id, List<UpsertTaxonomyTermDto> terms, CancellationToken ct = default)
    {
        var result = await taxonomyService.ReplaceTermsAsync(id, terms, ct);
        return Unwrap(result, "Replace taxonomy terms");
    }

    public async Task DeleteTaxonomyAsync(Guid id, CancellationToken ct = default)
    {
        var result = await taxonomyService.DeleteAsync(id, ct);
        EnsureSuccess(result, "Delete taxonomy");
    }

    // ─── Asset metadata values ─────────────────────────────────────────────

    public async Task<List<AssetMetadataValueDto>> GetAssetMetadataAsync(Guid assetId, CancellationToken ct = default)
    {
        var result = await assetMetadataService.GetByAssetIdAsync(assetId, ct);
        return Unwrap(result, "Get asset metadata");
    }

    public async Task<List<AssetMetadataValueDto>> SetAssetMetadataAsync(Guid assetId, SetAssetMetadataDto dto, CancellationToken ct = default)
    {
        Validate(dto, "Set asset metadata");
        var result = await assetMetadataService.SetAsync(assetId, dto, ct);
        return Unwrap(result, "Set asset metadata");
    }
}
