using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

/// <summary>
/// Persistence surface for personal access tokens. Lookup-by-hash is on the hot
/// authentication path — implementations must keep it constant-time on the unique
/// index (idx_pat_token_hash_unique).
/// </summary>
public interface IPersonalAccessTokenRepository
{
    /// <summary>List a user's PATs, newest first. Includes revoked + expired rows so the UI can show history.</summary>
    Task<List<PersonalAccessToken>> ListForOwnerAsync(string ownerUserId, CancellationToken ct = default);

    /// <summary>Lookup by id, scoped to the owner. Returns null if absent or owned by someone else.</summary>
    Task<PersonalAccessToken?> GetForOwnerAsync(Guid id, string ownerUserId, CancellationToken ct = default);

    /// <summary>
    /// Auth-handler hot path: lookup a token by its SHA-256 hash. Returns null if no row matches.
    /// Tracked entity — caller can mutate LastUsedAt and SaveChanges through the same context.
    /// </summary>
    Task<PersonalAccessToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Persist a new PAT. Caller is responsible for setting Id, CreatedAt, and TokenHash.</summary>
    Task<PersonalAccessToken> CreateAsync(PersonalAccessToken token, CancellationToken ct = default);

    /// <summary>Persist mutations on an already-tracked entity (revocation, LastUsedAt).</summary>
    Task UpdateAsync(PersonalAccessToken token, CancellationToken ct = default);
}
