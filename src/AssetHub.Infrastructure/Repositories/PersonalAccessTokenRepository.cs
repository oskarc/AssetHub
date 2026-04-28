using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Repositories;

public sealed class PersonalAccessTokenRepository(
    DbContextProvider provider,
    ILogger<PersonalAccessTokenRepository> logger) : IPersonalAccessTokenRepository
{
    public async Task<List<PersonalAccessToken>> ListForOwnerAsync(string ownerUserId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.PersonalAccessTokens
            .AsNoTracking()
            .Where(t => t.OwnerUserId == ownerUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<PersonalAccessToken?> GetForOwnerAsync(Guid id, string ownerUserId, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.PersonalAccessTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerUserId == ownerUserId, ct);
    }

    public async Task<PersonalAccessToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        return await db.PersonalAccessTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public async Task<PersonalAccessToken> CreateAsync(PersonalAccessToken token, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        if (token.Id == Guid.Empty)
            token.Id = Guid.NewGuid();
        if (token.CreatedAt == default)
            token.CreatedAt = DateTime.UtcNow;

        db.PersonalAccessTokens.Add(token);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created PAT {TokenId} for user {OwnerUserId}", token.Id, token.OwnerUserId);
        return token;
    }

    public async Task UpdateAsync(PersonalAccessToken token, CancellationToken ct = default)
    {
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;
        db.PersonalAccessTokens.Update(token);
        await db.SaveChangesAsync(ct);
    }
}
