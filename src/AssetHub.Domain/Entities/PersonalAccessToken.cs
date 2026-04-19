namespace AssetHub.Domain.Entities;

/// <summary>
/// Long-lived bearer token an authenticated user can mint to call AssetHub's REST API
/// without going through the OIDC flow. The plaintext value is shown to the user once on
/// creation; only the SHA-256 hash is persisted, so a leaked DB row can't be replayed.
/// PATs carry the same Keycloak sub as their owner and are restricted by the Scopes
/// list (an empty list means full owner-impersonation).
/// </summary>
public class PersonalAccessToken
{
    public Guid Id { get; set; }

    /// <summary>Human-friendly label the owner picked at creation ("CI deploy", "scratch laptop").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Keycloak sub of the user who created the PAT. Authentication uses this as the user id.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the plaintext token. Indexed unique — the PAT auth handler looks up by this.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Coarse-grained capability list (e.g., "assets:read", "shares:manage", "admin"). An empty list
    /// means the PAT acts as the owner with no extra restrictions — same access the user has via OIDC.
    /// Scope enforcement is opt-in per endpoint via [RequireScope("...")] (added in a later phase).
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    /// <summary>Optional absolute expiry. Null = never expires (until revoked).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Set when the user revokes the token. Once non-null, the token can never authenticate again.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Updated by the auth handler on each successful authentication. Surfaced in the UI.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>True when the token has not been revoked and has not expired.</summary>
    public bool IsActive(DateTime now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Idempotent — a re-revoke does not overwrite the original RevokedAt timestamp.</summary>
    public void MarkRevoked()
    {
        RevokedAt ??= DateTime.UtcNow;
    }

    /// <summary>Stamps LastUsedAt — call from the auth handler on each successful verification.</summary>
    public void MarkUsed(DateTime now)
    {
        LastUsedAt = now;
    }
}
