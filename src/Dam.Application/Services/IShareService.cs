using Dam.Application.Dtos;
using Microsoft.AspNetCore.Http;

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
        CreateShareDto dto, string userId, string baseUrl, HttpContext httpContext, CancellationToken ct = default);
}

public class ShareScopeValidation
{
    public bool IsValid { get; set; }
    public Guid? CollectionIdToCheck { get; set; }
    public string? ContentName { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ErrorStatusCode { get; set; }
}

public class ShareCreationResult
{
    public required ShareResponseDto Response { get; set; }
    public bool EmailFailed { get; set; }
}
