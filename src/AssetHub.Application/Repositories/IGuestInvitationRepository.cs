using AssetHub.Domain.Entities;

namespace AssetHub.Application.Repositories;

public interface IGuestInvitationRepository
{
    Task<List<GuestInvitation>> ListAllAsync(CancellationToken ct = default);

    Task<GuestInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Token-hash lookup for the magic-link redemption path.</summary>
    Task<GuestInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<GuestInvitation> CreateAsync(GuestInvitation invitation, CancellationToken ct = default);

    Task UpdateAsync(GuestInvitation invitation, CancellationToken ct = default);

    /// <summary>
    /// Atomically marks the invitation as accepted only if it's still
    /// pending (not revoked, not already accepted, not expired). Returns
    /// true on the winning call; false if another concurrent accept beat
    /// us to it or the row's state changed since the load. Closes the
    /// race window in <c>GuestInvitationService.AcceptAsync</c>.
    /// </summary>
    Task<bool> TryMarkAcceptedAsync(
        Guid id, string keycloakUserId, DateTime acceptedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Active (accepted + not yet revoked) invitations whose expiry has
    /// passed. Used by the background expiry sweep to revoke ACLs.
    /// </summary>
    Task<List<GuestInvitation>> ListExpiredAcceptedAsync(DateTime cutoff, CancellationToken ct = default);
}
