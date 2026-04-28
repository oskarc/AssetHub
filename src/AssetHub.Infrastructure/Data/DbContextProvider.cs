using AssetHub.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Infrastructure.Data;

/// <summary>
/// Hands out a fresh <see cref="AssetHubDbContext"/> per call (so concurrent
/// component-driven calls in a Blazor circuit can't trip EF's
/// "second operation on this context instance" detector), with one important
/// twist: when an "ambient" context is in scope (set by <see cref="UnitOfWork"/>
/// for the duration of a transactional work delegate), every <see cref="AcquireAsync"/>
/// call returns that ambient context instead of minting a new one. That way
/// repositories invoked inside <c>UnitOfWork.ExecuteAsync</c> all share the same
/// context — and therefore the same transaction — without having to pass it
/// around as a parameter.
///
/// Usage in a repository:
/// <code>
/// public async Task&lt;Foo&gt; GetAsync(Guid id, CancellationToken ct = default)
/// {
///     await using var lease = await _provider.AcquireAsync(ct);
///     return await lease.Db.Foos.FindAsync([id], ct);
/// }
/// </code>
/// <see cref="DbContextLease.DisposeAsync"/> only disposes the context when
/// this call actually owned its creation — disposing an ambient context would
/// pull the rug out from under the surrounding <see cref="UnitOfWork"/>.
/// </summary>
public sealed class DbContextProvider(IDbContextFactory<AssetHubDbContext> factory)
{
    public async Task<DbContextLease> AcquireAsync(CancellationToken ct = default)
    {
        if (DbContextScope.Current is { } ambient)
            return new DbContextLease(ambient, owned: false);
        var db = await factory.CreateDbContextAsync(ct);
        return new DbContextLease(db, owned: true);
    }
}

/// <summary>
/// A handle to a <see cref="AssetHubDbContext"/> obtained through
/// <see cref="DbContextProvider.AcquireAsync"/>. <c>await using</c> always —
/// the lease decides whether disposing actually closes the context (when this
/// call created it) or is a no-op (when the context belongs to an outer
/// <see cref="UnitOfWork"/> scope).
/// </summary>
public sealed class DbContextLease : IAsyncDisposable
{
    public AssetHubDbContext Db { get; }
    private readonly bool _owned;

    internal DbContextLease(AssetHubDbContext db, bool owned)
    {
        Db = db;
        _owned = owned;
    }

    public ValueTask DisposeAsync() => _owned ? Db.DisposeAsync() : ValueTask.CompletedTask;
}

/// <summary>
/// AsyncLocal slot used by <see cref="UnitOfWork"/> to publish its context
/// to the current logical call. <see cref="DbContextProvider"/> reads this on
/// each acquire so repos automatically participate in the surrounding
/// transaction.
/// </summary>
internal static class DbContextScope
{
    private static readonly AsyncLocal<AssetHubDbContext?> _current = new();

    public static AssetHubDbContext? Current => _current.Value;

    /// <summary>Set the ambient context. Returns a handle that clears it on dispose.</summary>
    public static IDisposable Begin(AssetHubDbContext db)
    {
        var prior = _current.Value;
        _current.Value = db;
        return new ScopeRelease(prior);
    }

    private sealed class ScopeRelease(AssetHubDbContext? prior) : IDisposable
    {
        public void Dispose() => _current.Value = prior;
    }
}
