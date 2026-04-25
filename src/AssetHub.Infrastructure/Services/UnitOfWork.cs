using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class UnitOfWork(AssetHubDbContext db) : IUnitOfWork
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await work(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
