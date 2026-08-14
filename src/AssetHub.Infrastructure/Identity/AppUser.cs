using Microsoft.AspNetCore.Identity;

namespace AssetHub.Infrastructure.Identity;

/// <summary>
/// The local Identity user. Lives in Infrastructure rather than Domain because
/// <see cref="IdentityUser"/> is a framework type and Domain carries zero
/// package references by standard.
/// </summary>
/// <remarks>
/// The key stays <see cref="string"/> — the same shape as the Keycloak subject
/// it replaces. Every user reference already persisted (CollectionAcl.PrincipalId,
/// Asset.CreatedByUserId, AuditEvent.ActorUserId) is a string, so switching
/// providers changes no column type anywhere.
/// </remarks>
public sealed class AppUser : IdentityUser
{
    /// <summary>Display name shown in the UI. Falls back to the username when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>When the account was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
