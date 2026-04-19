using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Command operations for metadata schemas (admin only).
/// </summary>
public interface IMetadataSchemaService
{
    /// <summary>Creates a new metadata schema with fields.</summary>
    Task<ServiceResult<MetadataSchemaDto>> CreateAsync(CreateMetadataSchemaDto dto, CancellationToken ct);

    /// <summary>Updates a metadata schema and optionally its fields.</summary>
    Task<ServiceResult<MetadataSchemaDto>> UpdateAsync(Guid id, UpdateMetadataSchemaDto dto, CancellationToken ct);

    /// <summary>Deletes a metadata schema. Use force=true to also delete associated values.</summary>
    Task<ServiceResult> DeleteAsync(Guid id, bool force, CancellationToken ct);
}
