using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
/// <remarks>
/// Wraps <see cref="DatabaseFacade.BeginTransactionAsync"/> in the context's
/// configured <see cref="IExecutionStrategy"/>. Today the strategy is the
/// default (non-retrying) one, so this is just one attempt. If
/// <c>EnableRetryOnFailure</c> ever lands on the EF Core registration, the
/// strategy retries the whole delegate — including the transaction — on
/// transient failures. Either way the work delegate must be idempotent
/// because it can be re-invoked.
/// </remarks>
public sealed class UnitOfWork(AssetHubDbContext db) : IUnitOfWork
{
    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async tct =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(tct);
            await work(tct);
            await tx.CommitAsync(tct);
        }, ct);

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async tct =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(tct);
            var result = await work(tct);
            await tx.CommitAsync(tct);
            return result;
        }, ct);
}
