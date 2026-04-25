using AssetHub.Application.Services;

namespace AssetHub.Tests.Helpers;

/// <summary>
/// IUnitOfWork test stub for unit tests with mocked repos — invokes the
/// work delegate directly. No real DbContext / transaction. Transactional
/// behaviour is exercised separately in integration tests.
/// </summary>
public sealed class PassThroughUnitOfWork : IUnitOfWork
{
    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct) => work(ct);
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct) => work(ct);
}
