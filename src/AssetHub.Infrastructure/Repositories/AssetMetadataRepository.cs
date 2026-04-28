using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class AssetMetadataRepository(
    DbContextProvider provider,
    ILogger<AssetMetadataRepository> logger) : IAssetMetadataRepository
{
    public async Task<List<AssetMetadataValue>> GetByAssetIdAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.AssetMetadataValues
            .AsNoTracking()
            .Include(v => v.MetadataField)
            .Include(v => v.ValueTaxonomyTerm)
            .Where(v => v.AssetId == assetId)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, List<AssetMetadataValue>>> GetByAssetIdsAsync(IEnumerable<Guid> assetIds, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var values = await db.AssetMetadataValues
            .AsNoTracking()
            .Include(v => v.MetadataField)
            .Include(v => v.ValueTaxonomyTerm)
            .Where(v => assetIds.Contains(v.AssetId))
            .ToListAsync(ct);

        return values.GroupBy(v => v.AssetId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task ReplaceForAssetAsync(Guid assetId, List<AssetMetadataValue> values, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        // Run delete + insert in one transaction so a mid-operation failure doesn't leave the asset with partial metadata.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AssetMetadataValues.Where(v => v.AssetId == assetId).ExecuteDeleteAsync(ct);

        foreach (var value in values)
        {
            if (value.Id == Guid.Empty)
                value.Id = Guid.NewGuid();
            value.AssetId = assetId;
        }

        db.AssetMetadataValues.AddRange(values);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Replaced {ValueCount} metadata values for asset {AssetId}", values.Count, assetId);
    }

    public async Task ReplaceForAssetsAsync(IEnumerable<(Guid AssetId, List<AssetMetadataValue> Values)> batch, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var entries = batch.ToList();
        if (entries.Count == 0) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var assetIds = entries.Select(e => e.AssetId).ToList();
        await db.AssetMetadataValues.Where(v => assetIds.Contains(v.AssetId)).ExecuteDeleteAsync(ct);

        foreach (var (assetId, values) in entries)
        {
            foreach (var value in values)
            {
                if (value.Id == Guid.Empty)
                    value.Id = Guid.NewGuid();
                value.AssetId = assetId;
            }
            db.AssetMetadataValues.AddRange(values);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Replaced metadata values for {AssetCount} assets", entries.Count);
    }

    public async Task DeleteByAssetIdAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var count = await db.AssetMetadataValues.Where(v => v.AssetId == assetId).ExecuteDeleteAsync(ct);
        if (count > 0)
            logger.LogInformation("Deleted {Count} metadata values for asset {AssetId}", count, assetId);
    }

    public async Task DeleteBySchemaIdAsync(Guid schemaId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        var fieldIds = await db.MetadataFields
            .Where(f => f.MetadataSchemaId == schemaId)
            .Select(f => f.Id)
            .ToListAsync(ct);

        if (fieldIds.Count > 0)
        {
            var count = await db.AssetMetadataValues
                .Where(v => fieldIds.Contains(v.MetadataFieldId))
                .ExecuteDeleteAsync(ct);
            logger.LogInformation("Deleted {Count} metadata values for schema {SchemaId}", count, schemaId);
        }
    }
}
