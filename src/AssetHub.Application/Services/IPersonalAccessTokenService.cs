using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Services;

/// <summary>
/// User-facing operations for personal access tokens (create / list / revoke) plus
/// the verify hook used by the PAT auth handler. Plaintext tokens never leave this
/// boundary — they're returned exactly once on creation, stored as SHA-256 hashes,
/// and verified by hashing the inbound bearer and looking up the row.
/// </summary>
public interface IPersonalAccessTokenService
{
    /// <summary>
    /// Plaintext token prefix. Lets the auth scheme selector route quickly without parsing a JWT.
    /// Lives on the contract so all layers (auth handler, smart selector, tests) agree on the value.
    /// </summary>
    public const string TokenPrefix = "pat_";


    /// <summary>
    /// Mints a new PAT for the current user. Returns the plaintext token in the response —
    /// the caller MUST surface it to the user immediately because the server only persists
    /// the SHA-256 hash. Validates that ExpiresAt (if set) is in the future and that every
    /// requested scope is on the allow-list.
    /// </summary>
    Task<ServiceResult<CreatedPersonalAccessTokenDto>> CreateAsync(CreatePersonalAccessTokenRequest request, CancellationToken ct);

    /// <summary>
    /// Lists the current user's PATs (active + revoked + expired), newest first.
    /// </summary>
    Task<ServiceResult<List<PersonalAccessTokenDto>>> ListMineAsync(CancellationToken ct);

    /// <summary>
    /// Revokes one of the current user's PATs. Idempotent — a second revoke succeeds
    /// without overwriting the original RevokedAt timestamp. Returns NotFound if the
    /// token does not belong to the caller (avoids leaking ids).
    /// </summary>
    Task<ServiceResult> RevokeAsync(Guid tokenId, CancellationToken ct);

    /// <summary>
    /// Auth-handler hook: hashes the supplied plaintext, looks up the matching row, and
    /// stamps LastUsedAt on success. Returns null when no row matches OR when the row
    /// is revoked / expired — never both states are exposed separately so timing leaks
    /// nothing about token existence. Plaintext is expected to start with "pat_".
    /// </summary>
    Task<PersonalAccessToken?> VerifyAndStampAsync(string plaintextToken, CancellationToken ct);

    /// <summary>
    /// Returns the SHA-256 hash representation used for storage / lookup. Exposed so
    /// the auth handler and tests can hash without re-implementing the algorithm.
    /// </summary>
    string ComputeHash(string plaintextToken);
}
