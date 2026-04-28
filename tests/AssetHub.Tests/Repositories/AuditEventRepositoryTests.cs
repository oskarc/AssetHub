using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace AssetHub.Tests.Repositories;

/// <summary>
/// Integration tests for AuditEventRepository against real PostgreSQL.
/// Focused on the retention-sweep delete paths used by AuditRetentionService.
/// </summary>
[Collection("Database")]
public class AuditEventRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AuditEventRepository _repo = null!;

    public AuditEventRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _repo = new AuditEventRepository(_fixture.CreateDbContextProvider(dbName));
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private static AuditEvent Make(string eventType, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        EventType = eventType,
        TargetType = "test",
        TargetId = null,
        ActorUserId = null,
        CreatedAt = createdAt,
        DetailsJson = new(),
    };

    [Fact]
    public async Task DeleteOlderThanBatchAsync_DeletesOnlyOldRows()
    {
        var now = DateTime.UtcNow;
        _db.AuditEvents.AddRange(
            Make("asset.created", now.AddDays(-100)),
            Make("asset.created", now.AddDays(-200)),
            Make("asset.created", now.AddDays(-1)));
        await _db.SaveChangesAsync();

        var deleted = await _repo.DeleteOlderThanBatchAsync(now.AddDays(-30), 100);

        Assert.Equal(2, deleted);
        var remaining = _db.AuditEvents.Where(e => true).ToList();
        Assert.Single(remaining);
        Assert.True(remaining[0].CreatedAt > now.AddDays(-30));
    }

    [Fact]
    public async Task DeleteOlderThanBatchAsync_RespectsBatchCap()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
            _db.AuditEvents.Add(Make("asset.created", now.AddDays(-100 - i)));
        await _db.SaveChangesAsync();

        var deleted = await _repo.DeleteOlderThanBatchAsync(now.AddDays(-30), 3);

        Assert.Equal(3, deleted);
        Assert.Equal(7, _db.AuditEvents.Count());
    }

    [Fact]
    public async Task DeleteByEventTypeOlderThanBatchAsync_DeletesOnlyMatchingType()
    {
        var now = DateTime.UtcNow;
        _db.AuditEvents.AddRange(
            Make("asset.downloaded", now.AddDays(-100)),
            Make("asset.downloaded", now.AddDays(-200)),
            Make("asset.created", now.AddDays(-100)),
            Make("share.accessed", now.AddDays(-100)));
        await _db.SaveChangesAsync();

        var deleted = await _repo.DeleteByEventTypeOlderThanBatchAsync(
            "asset.downloaded", now.AddDays(-30), 100);

        Assert.Equal(2, deleted);
        Assert.False(_db.AuditEvents.Any(e => e.EventType == "asset.downloaded"));
        Assert.True(_db.AuditEvents.Any(e => e.EventType == "asset.created"));
        Assert.True(_db.AuditEvents.Any(e => e.EventType == "share.accessed"));
    }

    [Fact]
    public async Task DeleteOlderThanBatchExcludingTypesAsync_SkipsExcludedTypes()
    {
        var now = DateTime.UtcNow;
        _db.AuditEvents.AddRange(
            Make("asset.downloaded", now.AddDays(-100)),
            Make("asset.created", now.AddDays(-100)),
            Make("share.accessed", now.AddDays(-100)),
            Make("collection.created", now.AddDays(-100)));
        await _db.SaveChangesAsync();

        var deleted = await _repo.DeleteOlderThanBatchExcludingTypesAsync(
            now.AddDays(-30),
            new[] { "asset.downloaded", "share.accessed" },
            100);

        Assert.Equal(2, deleted);
        Assert.True(_db.AuditEvents.Any(e => e.EventType == "asset.downloaded"));
        Assert.True(_db.AuditEvents.Any(e => e.EventType == "share.accessed"));
        Assert.False(_db.AuditEvents.Any(e => e.EventType == "asset.created"));
        Assert.False(_db.AuditEvents.Any(e => e.EventType == "collection.created"));
    }

    [Fact]
    public async Task DeleteOlderThanBatchExcludingTypesAsync_EmptyExclusionList_DeletesAll()
    {
        var now = DateTime.UtcNow;
        _db.AuditEvents.AddRange(
            Make("asset.created", now.AddDays(-100)),
            Make("share.accessed", now.AddDays(-100)));
        await _db.SaveChangesAsync();

        var deleted = await _repo.DeleteOlderThanBatchExcludingTypesAsync(
            now.AddDays(-30),
            Array.Empty<string>(),
            100);

        Assert.Equal(2, deleted);
        Assert.Empty(_db.AuditEvents);
    }
}
