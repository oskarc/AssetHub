using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Dam.Infrastructure.Repositories;

public class ShareRepository(AssetHubDbContext dbContext) : IShareRepository
{
    public async Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Share?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<List<Share>> GetByScopeAsync(string scopeType, Guid scopeId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .Where(s => s.ScopeType == scopeType && s.ScopeId == scopeId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Share>> GetByUserAsync(string userId, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await dbContext.Shares
            .Where(s => s.CreatedByUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Share>> GetAllAsync(bool includeAsset = false, bool includeCollection = false, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Shares.AsQueryable();
        
        if (includeAsset)
            query = query.Include(s => s.Asset);
        
        if (includeCollection)
            query = query.Include(s => s.Collection);
        
        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task CreateAsync(Share share, CancellationToken cancellationToken = default)
    {
        dbContext.Shares.Add(share);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Share share, CancellationToken cancellationToken = default)
    {
        dbContext.Shares.Update(share);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var share = await dbContext.Shares.FindAsync(new object[] { id }, cancellationToken);
        if (share != null)
        {
            dbContext.Shares.Remove(share);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
