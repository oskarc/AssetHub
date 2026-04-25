using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class TaxonomyService(
    ITaxonomyRepository repo,
    CurrentUser currentUser,
    ILogger<TaxonomyService> logger) : ITaxonomyService
{
    private const string AdminsOnlyMessage = "Only administrators can manage taxonomies";

    public async Task<ServiceResult<TaxonomyDto>> CreateAsync(CreateTaxonomyDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden(AdminsOnlyMessage);

        if (await repo.ExistsByNameAsync(dto.Name, ct: ct))
            return ServiceError.Conflict($"A taxonomy named '{dto.Name}' already exists");

        var taxonomy = new Taxonomy
        {
            Name = dto.Name,
            Description = dto.Description,
            CreatedByUserId = currentUser.UserId,
            Terms = FlattenTerms(dto.Terms, null)
        };

        var created = await repo.CreateAsync(taxonomy, ct);
        logger.LogInformation("Admin {UserId} created taxonomy {TaxonomyId} '{TaxonomyName}'", currentUser.UserId, created.Id, created.Name);
        return TaxonomyQueryService.ToDto(created);
    }

    public async Task<ServiceResult<TaxonomyDto>> UpdateAsync(Guid id, UpdateTaxonomyDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden(AdminsOnlyMessage);

        var taxonomy = await repo.GetByIdForUpdateAsync(id, ct);
        if (taxonomy is null) return ServiceError.NotFound("Taxonomy not found");

        if (dto.Name is not null)
        {
            if (await repo.ExistsByNameAsync(dto.Name, excludeId: id, ct: ct))
                return ServiceError.Conflict($"A taxonomy named '{dto.Name}' already exists");
            taxonomy.Name = dto.Name;
        }

        if (dto.Description is not null)
            taxonomy.Description = dto.Description;

        var updated = await repo.UpdateAsync(taxonomy, ct);
        logger.LogInformation("Admin {UserId} updated taxonomy {TaxonomyId}", currentUser.UserId, id);
        return TaxonomyQueryService.ToDto(updated);
    }

    public async Task<ServiceResult<TaxonomyDto>> ReplaceTermsAsync(Guid id, List<UpsertTaxonomyTermDto> terms, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden(AdminsOnlyMessage);

        var taxonomy = await repo.GetByIdAsync(id, ct);
        if (taxonomy is null) return ServiceError.NotFound("Taxonomy not found");

        var flattened = FlattenTerms(terms.Select(t => new CreateTaxonomyTermDto
        {
            Label = t.Label,
            LabelSv = t.LabelSv,
            Slug = t.Slug,
            SortOrder = t.SortOrder,
            Children = t.Children?.Select(c => new CreateTaxonomyTermDto
            {
                Label = c.Label,
                LabelSv = c.LabelSv,
                Slug = c.Slug,
                SortOrder = c.SortOrder
            }).ToList()
        }).ToList(), null);

        await repo.ReplaceTermsAsync(id, flattened, ct);

        var refreshed = await repo.GetByIdAsync(id, ct);
        logger.LogInformation("Admin {UserId} replaced terms for taxonomy {TaxonomyId}", currentUser.UserId, id);
        return TaxonomyQueryService.ToDto(refreshed!);
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden(AdminsOnlyMessage);

        var taxonomy = await repo.GetByIdAsync(id, ct);
        if (taxonomy is null) return ServiceError.NotFound("Taxonomy not found");

        if (await repo.IsReferencedByFieldsAsync(id, ct))
            return ServiceError.Conflict("Taxonomy is referenced by one or more metadata fields. Remove those references first.");

        await repo.DeleteAsync(id, ct);
        logger.LogInformation("Admin {UserId} deleted taxonomy {TaxonomyId}", currentUser.UserId, id);
        return ServiceResult.Success;
    }

    private static List<TaxonomyTerm> FlattenTerms(IEnumerable<CreateTaxonomyTermDto>? dtos, Guid? parentId)
    {
        if (dtos is null) return [];

        var result = new List<TaxonomyTerm>();
        foreach (var dto in dtos)
        {
            // Assign Id up front so child terms can reference it as ParentTermId.
            // Without this, children would record ParentTermId = Guid.Empty and fail the FK.
            var term = new TaxonomyTerm
            {
                Id = Guid.NewGuid(),
                Label = dto.Label,
                LabelSv = dto.LabelSv,
                Slug = string.IsNullOrWhiteSpace(dto.Slug) ? GenerateSlug(dto.Label) : dto.Slug,
                SortOrder = dto.SortOrder,
                ParentTermId = parentId
            };
            result.Add(term);
            result.AddRange(FlattenTerms(dto.Children, term.Id));
        }
        return result;
    }

    private static string GenerateSlug(string label)
    {
        return label.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("å", "a").Replace("ä", "a").Replace("ö", "o")
            .Replace("Å", "a").Replace("Ä", "a").Replace("Ö", "o");
    }
}
