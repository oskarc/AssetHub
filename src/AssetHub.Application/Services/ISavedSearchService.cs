using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Per-user saved searches. Each user manages their own — no sharing in v1.
/// Admins have no special access to other users' saved searches.
/// </summary>
public interface ISavedSearchService
{
    /// <summary>Returns the caller's saved searches, ordered by most recently created.</summary>
    Task<ServiceResult<List<SavedSearchDto>>> GetMineAsync(CancellationToken ct);

    /// <summary>Returns a single saved search by id. NotFound when it doesn't belong to the caller.</summary>
    Task<ServiceResult<SavedSearchDto>> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Creates a new saved search owned by the caller.</summary>
    Task<ServiceResult<SavedSearchDto>> CreateAsync(CreateSavedSearchDto dto, CancellationToken ct);

    /// <summary>Updates the caller's saved search.</summary>
    Task<ServiceResult<SavedSearchDto>> UpdateAsync(Guid id, UpdateSavedSearchDto dto, CancellationToken ct);

    /// <summary>Deletes the caller's saved search.</summary>
    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);
}
