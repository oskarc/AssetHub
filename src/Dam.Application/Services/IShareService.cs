using Dam.Application.Dtos;

namespace Dam.Application.Services;

/// <summary>
/// Encapsulates share link creation: scope validation, token/password cryptography,
/// persistence, audit logging, and optional email notification.
/// Authorization must be verified by the caller.
/// </summary>
public interface IShareService
{
    /// <summary>
    /// Validates the share scope, resolves the collection to authorize against,
    /// and returns the collectionId + content name for the caller to check authorization.
    /// </summary>
    /// <returns>Null collectionId if scope is invalid (error message set).</returns>
    Task<ShareScopeValidation> ValidateScopeAsync(
        CreateShareDto dto, CancellationToken ct = default);

    /// <summary>
    /// Creates a share link: generates token, encrypts it, hashes password, persists the share,
    /// and logs an audit event. Call only after authorization is confirmed.
    /// </summary>
    Task<ShareCreationResult> CreateShareAsync(
        CreateShareDto dto, string userId, string baseUrl, CancellationToken ct = default);
}

public class ShareScopeValidation
{
    public bool IsValid { get; set; }
    /// <summary>
    /// For asset scope: all collection IDs the asset belongs to (user needs access to ANY).
    /// For collection scope: the single collection ID.
    /// </summary>
    public List<Guid> CollectionIdsToCheck { get; set; } = new();
    public string? ContentName { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ErrorStatusCode { get; set; }
}

public class ShareCreationResult
{
    public required ShareResponseDto Response { get; set; }
    public bool EmailFailed { get; set; }
}
