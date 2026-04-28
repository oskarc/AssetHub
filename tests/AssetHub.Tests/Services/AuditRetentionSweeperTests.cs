using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetHub.Tests.Services;

/// <summary>
/// Integration tests for the audit-retention sweeper (T5-AUDIT-01) against real
/// PostgreSQL. Drives <see cref="IAuditRetentionSweeper"/> directly so we don't
/// have to spin up the BackgroundService loop.
/// </summary>
[Collection("Database")]
public class AuditRetentionSweeperTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AuditEventRepository _auditRepo = null!;
    private AuditService _auditService = null!;

    public AuditRetentionSweeperTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        var provider = _fixture.CreateDbContextProvider(dbName);
        _auditRepo = new AuditEventRepository(provider);
        _auditService = new AuditService(provider, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AuditRetentionSweeper CreateSweeper(AuditRetentionSettings settings) =>
        new(
            _auditRepo,
            _auditService,
            Options.Create(settings),
            NullLogger<AuditRetentionSweeper>.Instance);

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
    public async Task Sweep_DeletesPerEventTypeBeforeDefault_AndEmitsMetaEvent()
    {
        var now = DateTime.UtcNow;

        // Set up a representative mix:
        // - 2 asset.downloaded older than the per-event override (90d) → purged
        // - 1 asset.downloaded inside the per-event window (30d ago)   → kept
        // - 2 asset.created older than default retention (730d)        → purged
        // - 1 asset.created inside the default window (300d ago)        → kept
        // - 1 share.accessed older than its per-event override (90d)   → purged
        _db.AuditEvents.AddRange(
            Make("asset.downloaded", now.AddDays(-200)),
            Make("asset.downloaded", now.AddDays(-100)),
            Make("asset.downloaded", now.AddDays(-30)),
            Make("asset.created", now.AddDays(-1000)),
            Make("asset.created", now.AddDays(-800)),
            Make("asset.created", now.AddDays(-300)),
            Make("share.accessed", now.AddDays(-200)));
        await _db.SaveChangesAsync();

        var sweeper = CreateSweeper(new AuditRetentionSettings
        {
            DefaultRetentionDays = 730,
            SweepIntervalSeconds = 3600,
            BatchSize = 100,
            PerEventTypeOverrides = new Dictionary<string, int>
            {
                ["asset.downloaded"] = 90,
                ["share.accessed"] = 90,
            },
        });

        var purged = await sweeper.SweepAsync(CancellationToken.None);

        // 2 + 1 + 2 = 5 rows purged.
        Assert.Equal(5, purged);

        // Verify retained rows.
        var remaining = _db.AuditEvents
            .Where(e => e.EventType != "audit.retention_purged")
            .OrderBy(e => e.EventType).ThenBy(e => e.CreatedAt)
            .ToList();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, r => r.EventType == "asset.downloaded" && r.CreatedAt > now.AddDays(-90));
        Assert.Contains(remaining, r => r.EventType == "asset.created" && r.CreatedAt > now.AddDays(-730));

        // Exactly one meta-audit row, with the expected shape.
        var metaEvents = _db.AuditEvents.Where(e => e.EventType == "audit.retention_purged").ToList();
        Assert.Single(metaEvents);
        var meta = metaEvents[0];
        Assert.Equal("audit", meta.TargetType);
        Assert.Null(meta.ActorUserId);
        Assert.True(meta.DetailsJson.ContainsKey("purged_count"));
        Assert.True(meta.DetailsJson.ContainsKey("default_retention_days"));
        Assert.True(meta.DetailsJson.ContainsKey("per_event_type"));
    }

    [Fact]
    public async Task Sweep_NothingToPurge_DoesNotEmitMetaEvent()
    {
        var now = DateTime.UtcNow;
        _db.AuditEvents.AddRange(
            Make("asset.created", now.AddDays(-30)),
            Make("share.accessed", now.AddDays(-10)));
        await _db.SaveChangesAsync();

        var sweeper = CreateSweeper(new AuditRetentionSettings
        {
            DefaultRetentionDays = 730,
            SweepIntervalSeconds = 3600,
            BatchSize = 100,
            PerEventTypeOverrides = new Dictionary<string, int>
            {
                ["share.accessed"] = 90,
            },
        });

        var purged = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.False(_db.AuditEvents.Any(e => e.EventType == "audit.retention_purged"));
        Assert.Equal(2, _db.AuditEvents.Count(e => e.EventType != "audit.retention_purged"));
    }

    [Fact]
    public async Task Sweep_EmptyOverrides_AppliesDefaultRetentionToAllTypes()
    {
        var now = DateTime.UtcNow;
        _db.AuditEvents.AddRange(
            Make("asset.created", now.AddDays(-1000)),    // older than default 730 → purged
            Make("share.accessed", now.AddDays(-1000)),   // older than default 730 → purged
            Make("asset.downloaded", now.AddDays(-100))); // within default window → kept
        await _db.SaveChangesAsync();

        var sweeper = CreateSweeper(new AuditRetentionSettings
        {
            DefaultRetentionDays = 730,
            SweepIntervalSeconds = 3600,
            BatchSize = 100,
            PerEventTypeOverrides = new Dictionary<string, int>(),
        });

        var purged = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(2, purged);
        var remaining = _db.AuditEvents.Where(e => e.EventType != "audit.retention_purged").ToList();
        Assert.Single(remaining);
        Assert.Equal("asset.downloaded", remaining[0].EventType);
    }

    [Fact]
    public async Task Sweep_BackloggedEventType_DrainsAcrossMultipleBatches()
    {
        var now = DateTime.UtcNow;
        // 25 expired rows of one type, batch size 10 → drains in 3 round-trips.
        for (var i = 0; i < 25; i++)
            _db.AuditEvents.Add(Make("asset.downloaded", now.AddDays(-100 - i)));
        await _db.SaveChangesAsync();

        var sweeper = CreateSweeper(new AuditRetentionSettings
        {
            DefaultRetentionDays = 730,
            SweepIntervalSeconds = 3600,
            BatchSize = 10,
            PerEventTypeOverrides = new Dictionary<string, int>
            {
                ["asset.downloaded"] = 30,
            },
        });

        var purged = await sweeper.SweepAsync(CancellationToken.None);

        Assert.Equal(25, purged);
        Assert.False(_db.AuditEvents.Any(e => e.EventType == "asset.downloaded"));
    }
}
