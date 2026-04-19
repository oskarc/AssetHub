using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IMetadataSchemaRepository
{
    /// <summary>Gets a schema by ID with its fields (cached, no tracking).</summary>
    Task<MetadataSchema?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets a schema by ID with fields (tracked, for update).</summary>
    Task<MetadataSchema?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Gets all schemas (cached).</summary>
    Task<List<MetadataSchema>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Gets schemas applicable to a given asset type and optional collection.</summary>
    Task<List<MetadataSchema>> GetApplicableAsync(AssetType? assetType, Guid? collectionId, CancellationToken ct = default);

    /// <summary>Checks if a schema with the given name already exists.</summary>
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>Checks if the schema has any asset metadata values.</summary>
    Task<bool> HasMetadataValuesAsync(Guid schemaId, CancellationToken ct = default);

    /// <summary>Creates a new schema with its fields.</summary>
    Task<MetadataSchema> CreateAsync(MetadataSchema schema, CancellationToken ct = default);

    /// <summary>Updates an existing schema (must be tracked).</summary>
    Task<MetadataSchema> UpdateAsync(MetadataSchema schema, CancellationToken ct = default);

    /// <summary>Deletes a schema and its fields.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
