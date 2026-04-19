using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Query operations for metadata schemas.
/// </summary>
public interface IMetadataSchemaQueryService
{
    /// <summary>Gets all metadata schemas.</summary>
    Task<ServiceResult<List<MetadataSchemaDto>>> GetAllAsync(CancellationToken ct);

    /// <summary>Gets a metadata schema by ID.</summary>
    Task<ServiceResult<MetadataSchemaDto>> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Gets schemas applicable to an asset (by type and collection).</summary>
    Task<ServiceResult<List<MetadataSchemaDto>>> GetApplicableAsync(string? assetType, Guid? collectionId, CancellationToken ct);
}
