using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class TaxonomyRepository(
    AssetHubDbContext db,
    HybridCache cache,
    ILogger<TaxonomyRepository> logger) : ITaxonomyRepository
{
    public async Task<Taxonomy?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.Taxonomy(id),
            async ct => await db.Taxonomies
                .AsNoTracking()
                .Include(t => t.Terms.OrderBy(term => term.SortOrder))
                .FirstOrDefaultAsync(t => t.Id == id, ct),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.TaxonomyTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: [CacheKeys.Tags.Taxonomies],
            cancellationToken: ct);
    }

    public async Task<Taxonomy?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        return await db.Taxonomies
            .Include(t => t.Terms)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<List<Taxonomy>> GetAllAsync(CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync(
            CacheKeys.TaxonomiesAll(),
            async ct => await db.Taxonomies
                .AsNoTracking()
                .Include(t => t.Terms)
                .OrderBy(t => t.Name)
                .ToListAsync(ct),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.TaxonomyTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            tags: [CacheKeys.Tags.Taxonomies],
            cancellationToken: ct) ?? [];
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = db.Taxonomies.Where(t => t.Name == name);
        if (excludeId.HasValue)
            query = query.Where(t => t.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }

    public async Task<bool> IsReferencedByFieldsAsync(Guid taxonomyId, CancellationToken ct = default)
    {
        return await db.MetadataFields.AnyAsync(f => f.TaxonomyId == taxonomyId, ct);
    }

    public async Task<List<TaxonomyTerm>> GetTermsByIdsAsync(IEnumerable<Guid> termIds, CancellationToken ct = default)
    {
        return await db.TaxonomyTerms
            .AsNoTracking()
            .Where(t => termIds.Contains(t.Id))
            .ToListAsync(ct);
    }

    public async Task<Taxonomy> CreateAsync(Taxonomy taxonomy, CancellationToken ct = default)
    {
        if (taxonomy.Id == Guid.Empty)
            taxonomy.Id = Guid.NewGuid();
        if (taxonomy.CreatedAt == default)
            taxonomy.CreatedAt = DateTime.UtcNow;

        foreach (var term in taxonomy.Terms)
        {
            if (term.Id == Guid.Empty)
                term.Id = Guid.NewGuid();
            term.TaxonomyId = taxonomy.Id;
        }

        db.Taxonomies.Add(taxonomy);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Taxonomies, ct);
        logger.LogInformation("Created taxonomy {TaxonomyId} '{TaxonomyName}'", taxonomy.Id, taxonomy.Name);
        return taxonomy;
    }

    public async Task<Taxonomy> UpdateAsync(Taxonomy taxonomy, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Taxonomies, ct);
        logger.LogInformation("Updated taxonomy {TaxonomyId} '{TaxonomyName}'", taxonomy.Id, taxonomy.Name);
        return taxonomy;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await db.Taxonomies.Where(t => t.Id == id).ExecuteDeleteAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Taxonomies, ct);
        logger.LogInformation("Deleted taxonomy {TaxonomyId}", id);
    }

    public async Task ReplaceTermsAsync(Guid taxonomyId, ICollection<TaxonomyTerm> terms, CancellationToken ct = default)
    {
        await db.TaxonomyTerms.Where(t => t.TaxonomyId == taxonomyId).ExecuteDeleteAsync(ct);

        foreach (var term in terms)
        {
            if (term.Id == Guid.Empty)
                term.Id = Guid.NewGuid();
            term.TaxonomyId = taxonomyId;
        }

        db.TaxonomyTerms.AddRange(terms);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.Tags.Taxonomies, ct);
        logger.LogInformation("Replaced {TermCount} terms for taxonomy {TaxonomyId}", terms.Count, taxonomyId);
    }
}
