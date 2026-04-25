using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Magic-link guest access (T4-GUEST-01). Admin invites by email; the
/// invitee redeems a Data-Protection-signed token, which provisions a
/// Keycloak guest user and grants viewer ACL on the named collections.
/// </summary>
public interface IGuestInvitationService
{
    Task<ServiceResult<List<GuestInvitationResponseDto>>> ListAsync(CancellationToken ct);

    Task<ServiceResult<CreatedGuestInvitationDto>> CreateAsync(
        CreateGuestInvitationDto dto, string baseUrl, CancellationToken ct);

    /// <summary>
    /// Anonymous redemption. Validates the token, provisions / re-uses a
    /// Keycloak guest user, grants ACLs, marks the invitation accepted.
    /// </summary>
    Task<ServiceResult<AcceptGuestInvitationResponseDto>> AcceptAsync(string token, CancellationToken ct);

    /// <summary>Admin revoke — strips ACLs and marks revoked.</summary>
    Task<ServiceResult> RevokeAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// One-way signed magic-link tokens for guest invitations.
/// Implementation uses ASP.NET Core Data Protection under purpose
/// <c>Constants.DataProtection.GuestInvitationProtector</c>; the token
/// payload is just the invitation id, and the receive path looks up the
/// invitation by its hashed plaintext (the entity stores
/// <c>TokenHash</c>). Tampered tokens fail at <c>Unprotect</c>.
/// </summary>
public interface IGuestInvitationTokenService
{
    /// <summary>Generates a fresh plaintext token + its SHA-256 hash for storage.</summary>
    GuestInvitationToken Generate(Guid invitationId);

    /// <summary>Returns the invitation id encoded in the token, or null on tamper / decode failure.</summary>
    Guid? TryParse(string token);

    /// <summary>SHA-256 the supplied plaintext for a TokenHash lookup.</summary>
    string HashToken(string plaintext);
}

public sealed record GuestInvitationToken(string Plaintext, string Hash);
