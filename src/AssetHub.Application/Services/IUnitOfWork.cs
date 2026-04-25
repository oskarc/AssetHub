namespace AssetHub.Application.Services;

/// <summary>
/// Wraps a sequence of repository + audit calls in a single database
/// transaction so that an audit-write failure rolls the action back, and
/// vice-versa. Closes the gap in A-4 of the security review where
/// <c>repo.UpdateAsync()</c> and <c>audit.LogAsync()</c> ran as two
/// separate implicit transactions.
/// </summary>
/// <remarks>
/// <para>
/// Implementations should use <c>IExecutionStrategy</c> internally so that
/// transient connection failures retry automatically. Callers must not nest
/// — the work delegate runs inside <c>BeginTransactionAsync</c>; opening a
/// second transaction on the same connection will throw.
/// </para>
/// <para>
/// Use this for security-critical state changes (share creation, ACL
/// changes, PAT mint/revoke, workflow transitions, guest provisioning,
/// webhook secret rotation). For low-stakes audit-only events
/// (notifications, dashboard reads) the standalone <see cref="IAuditService.LogAsync"/>
/// is fine.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a single database transaction.
    /// Commits on success; rolls back on any exception thrown by the
    /// delegate (the exception is then re-thrown to the caller).
    /// </summary>
    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="work"/> inside a single database transaction
    /// and returns its result.
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
