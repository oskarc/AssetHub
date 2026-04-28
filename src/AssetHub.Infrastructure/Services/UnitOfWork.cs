using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
/// <remarks>
/// Owns one <see cref="AssetHubDbContext"/> for the duration of the work
/// delegate, publishes it as the ambient context (via <see cref="DbContextScope"/>),
/// and wraps the work in a transaction. Repositories invoked inside the
/// delegate transparently pick up the ambient context through
/// <see cref="DbContextProvider.AcquireAsync"/>, so all of their changes land
/// in the same transaction.
///
/// Wraps <see cref="DatabaseFacade.BeginTransactionAsync"/> in the context's
/// configured <see cref="IExecutionStrategy"/>. Today the strategy is the
/// default (non-retrying) one. If <c>EnableRetryOnFailure</c> ever lands on
/// the EF Core registration, the strategy retries the whole delegate —
/// including the transaction — on transient failures. Either way the work
/// delegate must be idempotent because it can be re-invoked.
/// </remarks>
public sealed class UnitOfWork(IDbContextFactory<AssetHubDbContext> factory) : IUnitOfWork
{
    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct)
        => ExecuteCoreAsync(async (_, tct) => { await work(tct); return 0; }, ct);

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        => ExecuteCoreAsync(async (_, tct) => await work(tct), ct);

    private async Task<T> ExecuteCoreAsync<T>(
        Func<AssetHubDbContext, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async tct =>
        {
            using var _ = DbContextScope.Begin(db);
            await using var tx = await db.Database.BeginTransactionAsync(tct);
            var result = await work(db, tct);
            await tx.CommitAsync(tct);
            return result;
        }, ct);
    }
}
