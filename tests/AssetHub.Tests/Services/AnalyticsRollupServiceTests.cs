using System.Security.Cryptography;
using AssetHub.Application;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for <see cref="AnalyticsRollupService"/> (T5-ANL-01).
/// Run against a real Postgres so we exercise the EF model config (composite
/// PKs, value-converted enum columns) rather than mocking around it.
/// </summary>
[Collection("Database")]
public class AnalyticsRollupServiceTests
{
    private readonly PostgresFixture _fixture;

    public AnalyticsRollupServiceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RollupDayAsync_GroupsAssetDownloadsByAssetId()
    {
        var dbName = $"test_anlrollup_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        // 3 download events for A, 1 for B, all on the target day.
        await SeedAuditEventsAsync(db, fromUtc, "asset.downloaded", Constants.ScopeTypes.Asset,
            (assetA, 3), (assetB, 1));

        var service = CreateService(dbName);
        var result = await service.RollupDayAsync(day, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value!.DownloadRowsWritten);

        await using var verify = _fixture.CreateDbContextForExistingDb(dbName);
        var rows = await verify.AnalyticsDailyRollups
            .Where(r => r.Date == day && r.Metric == AnalyticsCountMetric.DownloadsByAsset)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows.Single(r => r.EntityId == assetA.ToString("D")).Count);
        Assert.Equal(1, rows.Single(r => r.EntityId == assetB.ToString("D")).Count);
    }

    [Fact]
    public async Task RollupDayAsync_IsIdempotent_OnReRun()
    {
        var dbName = $"test_anlrollup_idem_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var asset = Guid.NewGuid();
        await SeedAuditEventsAsync(db, fromUtc, "asset.downloaded", Constants.ScopeTypes.Asset, (asset, 5));

        var service = CreateService(dbName);
        await service.RollupDayAsync(day, CancellationToken.None);
        // Re-run for the same day.
        await service.RollupDayAsync(day, CancellationToken.None);

        await using var verify = _fixture.CreateDbContextForExistingDb(dbName);
        var rows = await verify.AnalyticsDailyRollups
            .Where(r => r.Date == day && r.Metric == AnalyticsCountMetric.DownloadsByAsset)
            .ToListAsync();
        // Re-running for the same day must NOT double-count — same row count, same total.
        Assert.Single(rows);
        Assert.Equal(5, rows[0].Count);
    }

    [Fact]
    public async Task RollupDayAsync_GroupsExposureByRecipientHash()
    {
        var dbName = $"test_anlrollup_exp_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
        var downloadedAt = day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        var hashA = RandomBytes(32);
        var hashB = RandomBytes(32);

        // Three downloads to recipient A (user-id hash), two to recipient B (email hash).
        db.WatermarkDownloads.AddRange(
            MakeWatermarkDownload(downloadedAt, userIdHash: hashA),
            MakeWatermarkDownload(downloadedAt, userIdHash: hashA),
            MakeWatermarkDownload(downloadedAt, userIdHash: hashA),
            MakeWatermarkDownload(downloadedAt, emailHash: hashB),
            MakeWatermarkDownload(downloadedAt, emailHash: hashB));
        await db.SaveChangesAsync();

        var service = CreateService(dbName);
        await service.RollupDayAsync(day, CancellationToken.None);

        await using var verify = _fixture.CreateDbContextForExistingDb(dbName);
        var rows = await verify.AnalyticsDailyRollups
            .Where(r => r.Date == day && r.Metric == AnalyticsCountMetric.ExposureByRecipient)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows.Single(r => r.EntityId == Convert.ToBase64String(hashA)).Count);
        Assert.Equal(2, rows.Single(r => r.EntityId == Convert.ToBase64String(hashB)).Count);
    }

    [Fact]
    public async Task RollupDayAsync_StoresStorageSnapshotByCollection()
    {
        var dbName = $"test_anlrollup_storage_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var coll = new Collection
        {
            Id = Guid.NewGuid(),
            Name = "C",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "alice"
        };
        db.Collections.Add(coll);

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            Title = "A",
            AssetType = AssetType.Image,
            ContentType = "image/png",
            SizeBytes = 1024,
            Status = AssetStatus.Ready,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = "alice"
        };
        db.Assets.Add(asset);
        db.AssetCollections.Add(new AssetCollection
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            CollectionId = coll.Id,
            AddedAt = DateTime.UtcNow,
            AddedByUserId = "alice"
        });
        await db.SaveChangesAsync();

        var service = CreateService(dbName);
        await service.RollupDayAsync(day, CancellationToken.None);

        await using var verify = _fixture.CreateDbContextForExistingDb(dbName);
        var byColl = await verify.AnalyticsStorageRollups
            .Where(r => r.Date == day && r.Metric == AnalyticsStorageMetric.StorageByCollection)
            .ToListAsync();
        Assert.Single(byColl);
        Assert.Equal(coll.Id.ToString("D"), byColl[0].EntityId);
        Assert.Equal(1024, byColl[0].Bytes);
        Assert.Equal(1, byColl[0].AssetCount);

        var byType = await verify.AnalyticsStorageRollups
            .Where(r => r.Date == day && r.Metric == AnalyticsStorageMetric.StorageByAssetType)
            .ToListAsync();
        Assert.Single(byType);
        Assert.Equal("image", byType[0].EntityId);
    }

    private AnalyticsRollupService CreateService(string dbName)
    {
        var provider = _fixture.CreateDbContextProvider(dbName);
        return new AnalyticsRollupService(
            provider,
            new NoopAuditService(),
            NullLogger<AnalyticsRollupService>.Instance);
    }

    private static async Task SeedAuditEventsAsync(
        AssetHubDbContext db,
        DateTime onDay,
        string eventType,
        string targetType,
        params (Guid TargetId, int Count)[] entries)
    {
        foreach (var (targetId, count) in entries)
        {
            for (var i = 0; i < count; i++)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid(),
                    EventType = eventType,
                    TargetType = targetType,
                    TargetId = targetId,
                    ActorUserId = "alice",
                    CreatedAt = onDay.AddMinutes(i)
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private static WatermarkDownload MakeWatermarkDownload(
        DateTime downloadedAt,
        byte[]? userIdHash = null,
        byte[]? emailHash = null) => new()
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            DownloadedAt = downloadedAt,
            DownloadPath = "raw",
            AlgorithmVersion = "v1",
            PayloadHmac = RandomBytes(32),
            RecipientUserIdHash = userIdHash,
            RecipientEmailHash = emailHash
        };

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    private sealed class NoopAuditService : IAuditService
    {
        public Task LogAsync(
            string eventType, string targetType, Guid? targetId, string? actorUserId,
            Dictionary<string, object>? details = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
