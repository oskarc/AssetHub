using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class MetadataSchemaRepository(
    AssetHubDbContext db,
    HybridCache cache,
    ILogger<MetadataSchemaRepository> logger) : IMetadataSchemaRepository
{
    public async Task<MetadataSchema?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.MetadataSchema(id),
            async ct => await db.MetadataSchemas
                .AsNoTracking()
                .Include(s => s.Fields.OrderBy(f => f.SortOrder))
                .FirstOrDefaultAsync(s => s.Id == id, ct),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.MetadataSchemaTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: [CacheKeys.Tags.MetadataSchemas],
            cancellationToken: ct);
    }

    public async Task<MetadataSchema?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        return await db.MetadataSchemas
            .Include(s => s.Fields.OrderBy(f => f.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<List<MetadataSchema>> GetAllAsync(CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.MetadataSchemasAll(),
            async ct => await db.MetadataSchemas
                .AsNoTracking()
                .Include(s => s.Fields.OrderBy(f => f.SortOrder))
                .OrderBy(s => s.Name)
                .ToListAsync(ct),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.MetadataSchemaTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: [CacheKeys.Tags.MetadataSchemas],
            cancellationToken: ct) ?? [];
    }

    public async Task<List<MetadataSchema>> GetApplicableAsync(AssetType? assetType, Guid? collectionId, CancellationToken ct = default)
    {
        var query = db.MetadataSchemas
            .AsNoTracking()
            .Include(s => s.Fields.OrderBy(f => f.SortOrder))
            .Where(s =>
                s.Scope == MetadataSchemaScope.Global
                || (s.Scope == MetadataSchemaScope.AssetType && assetType != null && s.AssetType == assetType)
                || (s.Scope == MetadataSchemaScope.Collection && collectionId != null && s.CollectionId == collectionId));

        return await query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = db.MetadataSchemas.Where(s => s.Name == name);
        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task<bool> HasMetadataValuesAsync(Guid schemaId, CancellationToken ct = default)
    {
        return await db.AssetMetadataValues
            .AnyAsync(v => v.MetadataField!.MetadataSchemaId == schemaId, ct);
    }

    public async Task<MetadataSchema> CreateAsync(MetadataSchema schema, CancellationToken ct = default)
    {
        if (schema.Id == Guid.Empty)
            schema.Id = Guid.NewGuid();
        if (schema.CreatedAt == default)
            schema.CreatedAt = DateTime.UtcNow;

        foreach (var field in schema.Fields)
        {
            if (field.Id == Guid.Empty)
                field.Id = Guid.NewGuid();
            field.MetadataSchemaId = schema.Id;
        }

        db.MetadataSchemas.Add(schema);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.MetadataSchemas, ct);
        logger.LogInformation("Created metadata schema {SchemaId} '{SchemaName}'", schema.Id, schema.Name);
        return schema;
    }

    public async Task<MetadataSchema> UpdateAsync(MetadataSchema schema, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.MetadataSchemas, ct);
        logger.LogInformation("Updated metadata schema {SchemaId} '{SchemaName}'", schema.Id, schema.Name);
        return schema;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await db.MetadataSchemas.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.MetadataSchemas, ct);
        logger.LogInformation("Deleted metadata schema {SchemaId}", id);
    }
}
