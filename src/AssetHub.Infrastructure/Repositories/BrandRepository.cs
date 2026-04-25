using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Repositories;

public sealed class BrandRepository(AssetHubDbContext db) : IBrandRepository
{
    public async Task<List<Brand>> ListAllAsync(CancellationToken ct = default)
        => await db.Brands.AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct);

    public async Task<Brand?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<Brand?> GetDefaultAsync(CancellationToken ct = default)
        => await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.IsDefault, ct);

    public async Task<Brand> CreateAsync(Brand brand, CancellationToken ct = default)
    {
        if (brand.Id == Guid.Empty) brand.Id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        if (brand.CreatedAt == default) brand.CreatedAt = now;
        if (brand.UpdatedAt == default) brand.UpdatedAt = now;
        db.Brands.Add(brand);
        await db.SaveChangesAsync(ct);
        return brand;
    }

    public async Task UpdateAsync(Brand brand, CancellationToken ct = default)
    {
        brand.UpdatedAt = DateTime.UtcNow;
        db.Brands.Update(brand);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await db.Brands.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    public async Task ClearDefaultExceptAsync(Guid newDefaultId, CancellationToken ct = default)
    {
        // Single UPDATE — race-safe enough for admin-only flows; the unique
        // partial index on (IsDefault = true) is the ultimate guard.
        await db.Brands
            .Where(b => b.IsDefault && b.Id != newDefaultId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDefault, false), ct);
    }
}
