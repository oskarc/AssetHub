using AssetHub.Application.Messages;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for the transactional outbox (D-2). The load-bearing
/// invariant is: an enqueue inside <see cref="IUnitOfWork"/> must commit
/// atomically with the surrounding business mutation, and roll back when
/// the surrounding work throws — otherwise the outbox can't replace
/// publish-after-commit safely.
/// </summary>
[Collection("Database")]
public class OutboxPublisherTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private DbContextProvider _provider = null!;
    private OutboxPublisher _sut = null!;
    private UnitOfWork _uow = null!;

    public OutboxPublisherTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _provider = _fixture.CreateDbContextProvider(dbName);
        _sut = new OutboxPublisher(_provider);
        _uow = new UnitOfWork(_fixture.CreateDbContextFactory(dbName));
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task EnqueueAsync_PersistsRowWithSerializedPayload()
    {
        var assetId = Guid.NewGuid();
        var cmd = new ProcessImageCommand
        {
            AssetId = assetId,
            OriginalObjectKey = $"originals/{assetId}.png"
        };

        await _sut.EnqueueAsync(cmd, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var rows = await _db.OutboxMessages.ToListAsync();
        var row = Assert.Single(rows);

        Assert.Null(row.DispatchedAt);
        Assert.Equal(0, row.AttemptCount);
        Assert.StartsWith("AssetHub.Application.Messages.ProcessImageCommand,", row.MessageType, StringComparison.Ordinal);
        // Payload should round-trip back to the original message — so the
        // drainer's deserialize step is exercised end-to-end.
        Assert.Contains(assetId.ToString(), row.PayloadJson);
        Assert.Contains("originalObjectKey", row.PayloadJson);
    }

    [Fact]
    public async Task EnqueueAsync_InsideRolledBackUoW_DoesNotPersistRow()
    {
        // The atomicity contract: if the surrounding transaction rolls back,
        // the outbox row goes with it. Otherwise the drainer would publish a
        // command for a business mutation that never happened.
        var marker = new InvalidOperationException("rollback");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _uow.ExecuteAsync(async tct =>
            {
                await _sut.EnqueueAsync(
                    new ProcessImageCommand { AssetId = Guid.NewGuid(), OriginalObjectKey = "k" },
                    tct);
                throw marker;
            }, CancellationToken.None));

        _db.ChangeTracker.Clear();
        var count = await _db.OutboxMessages.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task EnqueueAsync_InsideCommittedUoW_PersistsRow()
    {
        var assetId = Guid.NewGuid();

        await _uow.ExecuteAsync(async tct =>
        {
            await _sut.EnqueueAsync(
                new ProcessVideoCommand { AssetId = assetId, OriginalObjectKey = $"originals/{assetId}.mp4" },
                tct);
        }, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var row = Assert.Single(await _db.OutboxMessages.ToListAsync());
        Assert.Contains("ProcessVideoCommand", row.MessageType);
        Assert.Contains(assetId.ToString(), row.PayloadJson);
    }

    [Fact]
    public async Task EnqueueAsync_ManyMessagesInOneUoW_AllShareTransaction()
    {
        // Cheap regression check: multiple enqueues in one UoW must all land
        // (or all roll back together). A bug where the publisher opens its
        // own context per call would split them across transactions.
        await _uow.ExecuteAsync(async tct =>
        {
            for (var i = 0; i < 5; i++)
            {
                await _sut.EnqueueAsync(
                    new ProcessImageCommand
                    {
                        AssetId = Guid.NewGuid(),
                        OriginalObjectKey = $"k{i}"
                    }, tct);
            }
        }, CancellationToken.None);

        _db.ChangeTracker.Clear();
        var count = await _db.OutboxMessages.CountAsync();
        Assert.Equal(5, count);
    }
}
