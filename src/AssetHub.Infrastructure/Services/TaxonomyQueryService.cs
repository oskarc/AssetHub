using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

public sealed class TaxonomyQueryService(
    ITaxonomyRepository repo) : ITaxonomyQueryService
{
    public async Task<ServiceResult<List<TaxonomySummaryDto>>> GetAllAsync(CancellationToken ct)
    {
        var taxonomies = await repo.GetAllAsync(ct);
        return taxonomies.Select(t => new TaxonomySummaryDto
        {
            Id = t.Id,
            Name = t.Name,
            TermCount = t.Terms.Count
        }).ToList();
    }

    public async Task<ServiceResult<TaxonomyDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var taxonomy = await repo.GetByIdAsync(id, ct);
        if (taxonomy is null) return ServiceError.NotFound("Taxonomy not found");
        return ToDto(taxonomy);
    }

    internal static TaxonomyDto ToDto(Taxonomy t)
    {
        var rootTerms = t.Terms
            .Where(term => term.ParentTermId is null)
            .OrderBy(term => term.SortOrder)
            .Select(term => ToTermDto(term, t.Terms.ToList()))
            .ToList();

        return new TaxonomyDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            CreatedAt = t.CreatedAt,
            CreatedByUserId = t.CreatedByUserId,
            Terms = rootTerms
        };
    }

    private static TaxonomyTermDto ToTermDto(TaxonomyTerm term, List<TaxonomyTerm> allTerms)
    {
        var children = allTerms
            .Where(c => c.ParentTermId == term.Id)
            .OrderBy(c => c.SortOrder)
            .Select(c => ToTermDto(c, allTerms))
            .ToList();

        return new TaxonomyTermDto
        {
            Id = term.Id,
            ParentTermId = term.ParentTermId,
            Label = term.Label,
            LabelSv = term.LabelSv,
            Slug = term.Slug,
            SortOrder = term.SortOrder,
            Children = children.Count > 0 ? children : null
        };
    }
}
