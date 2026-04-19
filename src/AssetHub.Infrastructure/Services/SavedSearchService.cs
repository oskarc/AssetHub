using System.Text.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class SavedSearchService(
    ISavedSearchRepository repo,
    CurrentUser currentUser,
    ILogger<SavedSearchService> logger) : ISavedSearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceResult<List<SavedSearchDto>>> GetMineAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var saved = await repo.GetByOwnerAsync(currentUser.UserId, ct);
        return saved.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<SavedSearchDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var saved = await repo.GetByIdAsync(id, currentUser.UserId, ct);
        if (saved is null) return ServiceError.NotFound("Saved search not found");
        return ToDto(saved);
    }

    public async Task<ServiceResult<SavedSearchDto>> CreateAsync(CreateSavedSearchDto dto, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        if (!DomainEnumExtensions.IsValidSavedSearchNotifyCadence(dto.Notify))
            return ServiceError.BadRequest($"Unknown notify cadence: {dto.Notify}");

        if (await repo.ExistsByNameAsync(currentUser.UserId, dto.Name, ct: ct))
            return ServiceError.Conflict($"A saved search named '{dto.Name}' already exists");

        var saved = new SavedSearch
        {
            Name = dto.Name,
            OwnerUserId = currentUser.UserId,
            RequestJson = JsonSerializer.Serialize(dto.Request, JsonOptions),
            Notify = dto.Notify.ToSavedSearchNotifyCadence()
        };

        var created = await repo.CreateAsync(saved, ct);
        logger.LogInformation("User {UserId} created saved search {Id}", currentUser.UserId, created.Id);
        return ToDto(created);
    }

    public async Task<ServiceResult<SavedSearchDto>> UpdateAsync(Guid id, UpdateSavedSearchDto dto, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var saved = await repo.GetByIdAsync(id, currentUser.UserId, ct);
        if (saved is null) return ServiceError.NotFound("Saved search not found");

        if (dto.Name is not null)
        {
            if (await repo.ExistsByNameAsync(currentUser.UserId, dto.Name, excludeId: id, ct: ct))
                return ServiceError.Conflict($"A saved search named '{dto.Name}' already exists");
            saved.Name = dto.Name;
        }

        if (dto.Request is not null)
            saved.RequestJson = JsonSerializer.Serialize(dto.Request, JsonOptions);

        if (dto.Notify is not null)
        {
            if (!DomainEnumExtensions.IsValidSavedSearchNotifyCadence(dto.Notify))
                return ServiceError.BadRequest($"Unknown notify cadence: {dto.Notify}");
            saved.Notify = dto.Notify.ToSavedSearchNotifyCadence();
        }

        var updated = await repo.UpdateAsync(saved, ct);
        return ToDto(updated);
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        await repo.DeleteAsync(id, currentUser.UserId, ct);
        return ServiceResult.Success;
    }

    private static SavedSearchDto ToDto(SavedSearch s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        OwnerUserId = s.OwnerUserId,
        Request = JsonSerializer.Deserialize<AssetSearchRequest>(s.RequestJson, JsonOptions) ?? new AssetSearchRequest(),
        Notify = s.Notify.ToDbString(),
        LastRunAt = s.LastRunAt,
        CreatedAt = s.CreatedAt
    };
}
