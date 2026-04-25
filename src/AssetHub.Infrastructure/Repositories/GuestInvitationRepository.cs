using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class GuestInvitationRepository(AssetHubDbContext db) : IGuestInvitationRepository
{
    public async Task<List<GuestInvitation>> ListAllAsync(CancellationToken ct = default)
        => await db.GuestInvitations.AsNoTracking().OrderByDescending(g => g.CreatedAt).ToListAsync(ct);

    public async Task<GuestInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.GuestInvitations.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<GuestInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await db.GuestInvitations.FirstOrDefaultAsync(g => g.TokenHash == tokenHash, ct);

    public async Task<GuestInvitation> CreateAsync(GuestInvitation invitation, CancellationToken ct = default)
    {
        if (invitation.Id == Guid.Empty) invitation.Id = Guid.NewGuid();
        if (invitation.CreatedAt == default) invitation.CreatedAt = DateTime.UtcNow;
        db.GuestInvitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return invitation;
    }

    public async Task UpdateAsync(GuestInvitation invitation, CancellationToken ct = default)
    {
        db.GuestInvitations.Update(invitation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryMarkAcceptedAsync(
        Guid id, string keycloakUserId, DateTime acceptedAtUtc, CancellationToken ct = default)
    {
        // Single-statement conditional update — Postgres handles concurrency:
        // exactly one of N concurrent accepts will see AcceptedAt = NULL and
        // change the row; the rest get 0 affected rows and lose the race.
        var rowsAffected = await db.GuestInvitations
            .Where(g => g.Id == id
                && g.AcceptedAt == null
                && g.RevokedAt == null
                && g.ExpiresAt > acceptedAtUtc)
            .ExecuteUpdateAsync(set => set
                .SetProperty(g => g.AcceptedAt, acceptedAtUtc)
                .SetProperty(g => g.AcceptedUserId, keycloakUserId), ct);
        return rowsAffected == 1;
    }

    public async Task<List<GuestInvitation>> ListExpiredAcceptedAsync(DateTime cutoff, CancellationToken ct = default)
    {
        return await db.GuestInvitations
            .AsNoTracking()
            .Where(g => g.AcceptedAt != null && g.RevokedAt == null && g.ExpiresAt <= cutoff)
            .ToListAsync(ct);
    }
}
