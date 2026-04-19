using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Services;

public sealed class AssetSearchService(
    AssetHubDbContext db,
    ICollectionRepository collectionRepo,
    CurrentUser currentUser,
    ILogger<AssetSearchService> logger) : IAssetSearchService
{
    private const int FacetBucketLimit = 50;

    public async Task<ServiceResult<AssetSearchResponse>> SearchAsync(AssetSearchRequest request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        // Resolve the set of collections the caller can see. Admins get all, others get only the
        // collections their ACL allows. Results are always implicitly scoped to these ids.
        var accessibleCollectionIds = currentUser.IsSystemAdmin
            ? await db.Collections.AsNoTracking().Select(c => c.Id).ToListAsync(ct)
            : (await collectionRepo.GetAccessibleCollectionsAsync(currentUser.UserId, ct))
                .Select(c => c.Id).ToList();

        if (accessibleCollectionIds.Count == 0)
        {
            return new AssetSearchResponse
            {
                Items = new(),
                TotalCount = 0,
                Facets = new()
            };
        }

        var mainQuery = BuildBaseQuery(request, accessibleCollectionIds, excludeDimension: null);

        var total = await mainQuery.CountAsync(ct);

        var sorted = ApplySort(mainQuery, request.Sort, hasText: !string.IsNullOrWhiteSpace(request.Text));
        var assets = await sorted.Skip(request.Skip).Take(request.Take).AsNoTracking().ToListAsync(ct);

        var items = assets.Select(a => AssetMapper.ToDto(a)).ToList();

        var facets = await BuildFacetsAsync(request, accessibleCollectionIds, ct);

        logger.LogInformation("Search by user {UserId} matched {Total} assets (took={Take}, facets={FacetCount})",
            currentUser.UserId, total, request.Take, facets.Count);

        return new AssetSearchResponse
        {
            Items = items,
            TotalCount = total,
            Facets = facets
        };
    }

    // ── Base query ──────────────────────────────────────────────────────

    private IQueryable<Asset> BuildBaseQuery(
        AssetSearchRequest request,
        List<Guid> accessibleCollectionIds,
        string? excludeDimension)
    {
        IQueryable<Asset> q = db.Assets
            .AsNoTracking()
            .Where(a => a.Status != AssetStatus.Uploading);

        // Caller's accessible collections — always applied, regardless of excludeDimension.
        q = q.Where(a => a.AssetCollections.Any(ac => accessibleCollectionIds.Contains(ac.CollectionId)));

        // Text — tsvector match via EF shadow property.
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var text = request.Text;
            q = q.Where(a => EF.Property<NpgsqlTsVector>(a, "SearchVector")
                .Matches(EF.Functions.WebSearchToTsQuery("simple", text)));
        }

        if (excludeDimension != "asset_type" && request.AssetTypes is { Count: > 0 })
        {
            var types = request.AssetTypes.Where(DomainEnumExtensions.IsValidAssetType)
                .Select(t => t.ToAssetType()).ToList();
            if (types.Count > 0)
                q = q.Where(a => types.Contains(a.AssetType));
        }

        if (excludeDimension != "status" && request.Statuses is { Count: > 0 })
        {
            var statuses = request.Statuses
                .Select(s => s.ToAssetStatus())
                .Where(s => s != AssetStatus.Unknown)
                .ToList();
            if (statuses.Count > 0)
                q = q.Where(a => statuses.Contains(a.Status));
        }

        if (excludeDimension != "collection" && request.CollectionIds is { Count: > 0 })
        {
            // Intersect caller-supplied filter with what they can see.
            var filter = request.CollectionIds.Intersect(accessibleCollectionIds).ToList();
            if (filter.Count > 0)
                q = q.Where(a => a.AssetCollections.Any(ac => filter.Contains(ac.CollectionId)));
            else
                // Caller asked for collections they can't see — empty result.
                q = q.Where(a => false);
        }

        if (excludeDimension != "tag" && request.Tags is { Count: > 0 })
        {
            var tags = request.Tags;
            q = q.Where(a => a.Tags.Any(t => tags.Contains(t)));
        }

        if (request.CreatedAfter.HasValue)
            q = q.Where(a => a.CreatedAt >= request.CreatedAfter.Value);
        if (request.CreatedBefore.HasValue)
            q = q.Where(a => a.CreatedAt <= request.CreatedBefore.Value);

        if (request.MetadataFilters is not null)
        {
            foreach (var (fieldId, values) in request.MetadataFilters)
            {
                if (excludeDimension == MetaFacet(fieldId)) continue;
                if (values is null || values.Count == 0) continue;

                var localFieldId = fieldId;
                var localValues = values;
                q = q.Where(a => db.AssetMetadataValues.Any(v =>
                    v.AssetId == a.Id
                    && v.MetadataFieldId == localFieldId
                    && ((v.ValueText != null && localValues.Contains(v.ValueText))
                        || (v.ValueTaxonomyTermId.HasValue
                            && localValues.Contains(v.ValueTaxonomyTermId.Value.ToString())))));
            }
        }

        return q;
    }

    private static IQueryable<Asset> ApplySort(IQueryable<Asset> q, string sort, bool hasText) => sort switch
    {
        "relevance" when hasText => q.OrderByDescending(a => a.CreatedAt),   // server keeps insertion order when tsvector rank isn't available here
        "created_asc" => q.OrderBy(a => a.CreatedAt),
        "created_desc" => q.OrderByDescending(a => a.CreatedAt),
        "title_asc" => q.OrderBy(a => a.Title),
        "title_desc" => q.OrderByDescending(a => a.Title),
        _ => q.OrderByDescending(a => a.CreatedAt)
    };

    // ── Facets ─────────────────────────────────────────────────────────

    private async Task<Dictionary<string, List<FacetBucket>>> BuildFacetsAsync(
        AssetSearchRequest request,
        List<Guid> accessibleCollectionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, List<FacetBucket>>();
        if (request.Facets is null || request.Facets.Count == 0) return result;

        foreach (var dimension in request.Facets)
        {
            var buckets = await AggregateFacetAsync(dimension, request, accessibleCollectionIds, ct);
            if (buckets is not null) result[dimension] = buckets;
        }

        return result;
    }

    private async Task<List<FacetBucket>?> AggregateFacetAsync(
        string dimension,
        AssetSearchRequest request,
        List<Guid> accessibleCollectionIds,
        CancellationToken ct)
    {
        // Each dimension re-builds the base query excluding its own filter, so the returned bucket
        // counts represent "how many assets would match if I added this value to my filters".
        var q = BuildBaseQuery(request, accessibleCollectionIds, excludeDimension: dimension);

        return dimension switch
        {
            "asset_type" => await AssetTypeBuckets(q, ct),
            "status" => await StatusBuckets(q, ct),
            "collection" => await CollectionBuckets(q, ct),
            "tag" => await TagBuckets(q, ct),
            _ when dimension.StartsWith("meta:", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(dimension[5..], out var fieldId) => await MetadataBuckets(q, fieldId, ct),
            _ => null
        };
    }

    private static async Task<List<FacetBucket>> AssetTypeBuckets(IQueryable<Asset> q, CancellationToken ct)
    {
        var raw = await q
            .GroupBy(a => a.AssetType)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(FacetBucketLimit)
            .ToListAsync(ct);

        return raw.Select(r => new FacetBucket
        {
            Value = r.Value.ToDbString(),
            Label = r.Value.ToDbString(),
            Count = r.Count
        }).ToList();
    }

    private static async Task<List<FacetBucket>> StatusBuckets(IQueryable<Asset> q, CancellationToken ct)
    {
        var raw = await q
            .GroupBy(a => a.Status)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(FacetBucketLimit)
            .ToListAsync(ct);

        return raw.Select(r => new FacetBucket
        {
            Value = r.Value.ToDbString(),
            Label = r.Value.ToDbString(),
            Count = r.Count
        }).ToList();
    }

    private async Task<List<FacetBucket>> CollectionBuckets(IQueryable<Asset> q, CancellationToken ct)
    {
        var raw = await q
            .SelectMany(a => a.AssetCollections)
            .GroupBy(ac => ac.CollectionId)
            .Select(g => new { CollectionId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(FacetBucketLimit)
            .ToListAsync(ct);

        var ids = raw.Select(r => r.CollectionId).ToList();
        var names = await db.Collections
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return raw.Select(r => new FacetBucket
        {
            Value = r.CollectionId.ToString(),
            Label = names.TryGetValue(r.CollectionId, out var name) ? name : r.CollectionId.ToString(),
            Count = r.Count
        }).ToList();
    }

    private static async Task<List<FacetBucket>> TagBuckets(IQueryable<Asset> q, CancellationToken ct)
    {
        // Pull Tags arrays from the filtered result set and aggregate in memory. Postgres
        // unnest() via EF is fragile; keeping this simple until a performance measurement says
        // otherwise. Bounded by the already-narrowed filter set.
        var tagLists = await q.Select(a => a.Tags).ToListAsync(ct);
        return tagLists
            .SelectMany(t => t)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FacetBucket { Value = g.Key, Label = g.Key, Count = g.Count() })
            .OrderByDescending(b => b.Count)
            .Take(FacetBucketLimit)
            .ToList();
    }

    private async Task<List<FacetBucket>?> MetadataBuckets(IQueryable<Asset> q, Guid fieldId, CancellationToken ct)
    {
        var field = await db.MetadataFields.AsNoTracking()
            .Include(f => f.Taxonomy)
            .FirstOrDefaultAsync(f => f.Id == fieldId, ct);
        if (field is null || !field.Facetable) return null;

        // Join Assets in the filtered set with their values for this field.
        var pairs = await (
            from a in q
            join v in db.AssetMetadataValues.AsNoTracking().Where(v => v.MetadataFieldId == fieldId)
                on a.Id equals v.AssetId
            select new
            {
                Text = v.ValueText,
                TermId = v.ValueTaxonomyTermId
            })
            .ToListAsync(ct);

        if (field.Type == MetadataFieldType.Taxonomy)
        {
            var termIds = pairs.Where(p => p.TermId.HasValue).Select(p => p.TermId!.Value).Distinct().ToList();
            var termLabels = termIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await db.TaxonomyTerms.AsNoTracking()
                    .Where(t => termIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Label })
                    .ToDictionaryAsync(t => t.Id, t => t.Label, ct);

            return pairs
                .Where(p => p.TermId.HasValue)
                .GroupBy(p => p.TermId!.Value)
                .Select(g => new FacetBucket
                {
                    Value = g.Key.ToString(),
                    Label = termLabels.TryGetValue(g.Key, out var lbl) ? lbl : g.Key.ToString(),
                    Count = g.Count()
                })
                .OrderByDescending(b => b.Count)
                .Take(FacetBucketLimit)
                .ToList();
        }

        // Text / select / multi-select — group by ValueText.
        return pairs
            .Where(p => !string.IsNullOrEmpty(p.Text))
            .GroupBy(p => p.Text!)
            .Select(g => new FacetBucket { Value = g.Key, Label = g.Key, Count = g.Count() })
            .OrderByDescending(b => b.Count)
            .Take(FacetBucketLimit)
            .ToList();
    }

    private static string MetaFacet(Guid fieldId) => $"meta:{fieldId}";
}
