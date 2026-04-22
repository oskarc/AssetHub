using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Worker.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Handlers;

public class S3MigrationScanHandlerTests
{
    private readonly Mock<IMigrationRepository> _repo = new();
    private readonly Mock<IS3ConnectorClient> _s3 = new();
    private readonly Mock<IMigrationSecretProtector> _protector = new();
    private readonly Mock<IAuditService> _audit = new();

    public S3MigrationScanHandlerTests()
    {
        _protector.Setup(p => p.Protect(It.IsAny<string>()))
            .Returns<string>(s => $"enc({s})");
        _protector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns<string>(s => s.StartsWith("enc(") && s.EndsWith(')') ? s[4..^1] : s);
    }

    private S3MigrationScanHandler CreateHandler()
        => new(_repo.Object, _s3.Object, _protector.Object, _audit.Object, NullLogger<S3MigrationScanHandler>.Instance);

    private static S3SourceConfigDto ValidConfig() => new()
    {
        Endpoint = "https://s3.eu-west-1.amazonaws.com",
        Bucket = "bucket-a",
        Prefix = "photos/",
        AccessKey = "AK",
        SecretKey = "SK",
        Region = "eu-west-1"
    };

    private Migration MakeS3Migration(
        MigrationStatus status = MigrationStatus.Validating,
        bool withValidConfig = true)
    {
        var m = new Migration
        {
            Id = Guid.NewGuid(),
            Name = "S3",
            SourceType = MigrationSourceType.S3,
            Status = status,
            CreatedByUserId = "admin-1",
            CreatedAt = DateTime.UtcNow
        };
        if (withValidConfig)
            m.SourceConfig = MigrationS3ConfigCodec.Write(ValidConfig(), _protector.Object);
        return m;
    }

    [Fact]
    public async Task HandleAsync_MigrationNotFound_NoOp()
    {
        var cmd = new S3MigrationScanCommand { MigrationId = Guid.NewGuid() };
        _repo.Setup(r => r.GetByIdAsync(cmd.MigrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Migration?)null);

        await CreateHandler().HandleAsync(cmd, CancellationToken.None);

        _s3.VerifyNoOtherCalls();
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_MigrationNotValidating_NoOp()
    {
        var m = MakeS3Migration(MigrationStatus.Draft);
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        _s3.Verify(s => s.ListObjectsAsync(It.IsAny<S3SourceConfigDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_NonS3SourceType_RecordsFailure()
    {
        var m = new Migration
        {
            Id = Guid.NewGuid(),
            Name = "Csv",
            SourceType = MigrationSourceType.CsvUpload,
            Status = MigrationStatus.Validating,
            CreatedByUserId = "admin-1",
            CreatedAt = DateTime.UtcNow
        };
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        Assert.Equal(MigrationStatus.Failed, m.Status);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.S3ScanFailed,
            Constants.ScopeTypes.Migration,
            m.Id,
            null,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_InvalidStoredConfig_RecordsFailure()
    {
        var m = MakeS3Migration(withValidConfig: false);
        m.SourceConfig.Clear();  // codec.Read will throw
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        Assert.Equal(MigrationStatus.Failed, m.Status);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.S3ScanFailed,
            Constants.ScopeTypes.Migration,
            m.Id,
            null,
            It.Is<Dictionary<string, object>?>(d => d != null && (string)d["errorCode"] == "invalid_config"),
            It.IsAny<CancellationToken>()), Times.Once);
        _s3.Verify(s => s.ListObjectsAsync(It.IsAny<S3SourceConfigDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ListThrows_RecordsFailureAndLeavesMigrationFailed()
    {
        var m = MakeS3Migration();
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);
        _s3.Setup(s => s.ListObjectsAsync(It.IsAny<S3SourceConfigDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("S3 Access Denied"));

        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        Assert.Equal(MigrationStatus.Failed, m.Status);
        Assert.NotNull(m.FinishedAt);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.S3ScanFailed,
            Constants.ScopeTypes.Migration,
            m.Id,
            null,
            It.Is<Dictionary<string, object>?>(d => d != null
                && (string)d["errorCode"] == "scan_failed"
                && ((string)d["errorMessage"]).Contains("Access Denied")),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.AddItemsAsync(It.IsAny<IEnumerable<MigrationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CreatesItemsAndAuditsCompleted()
    {
        var m = MakeS3Migration();
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        var objects = new List<S3ObjectInfo>
        {
            new("photos/a/alpha.jpg", 1024, "etag-a"),
            new("photos/b/beta.png", 2048, "etag-b"),
            new("photos/placeholder/", 0, "etag-dir"),        // directory placeholder — skipped
            new("", 0, "etag-empty"),                          // empty key — skipped
            new("photos/c/gamma.gif", 4096, "etag-c")
        };
        _s3.Setup(s => s.ListObjectsAsync(It.IsAny<S3SourceConfigDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(objects);

        List<MigrationItem>? captured = null;
        _repo.Setup(r => r.AddItemsAsync(It.IsAny<IEnumerable<MigrationItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MigrationItem>, CancellationToken>((items, _) => captured = items.ToList())
            .Returns(Task.CompletedTask);

        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(3, captured!.Count);
        Assert.Equal(3, m.ItemsTotal);
        Assert.Equal(MigrationStatus.Draft, m.Status);

        var first = captured[0];
        Assert.Equal("photos/a/alpha.jpg", first.ExternalId);
        Assert.Equal("photos/a/alpha.jpg", first.SourcePath);
        Assert.Equal("alpha.jpg", first.FileName);
        Assert.Equal("alpha", first.Title);
        Assert.Equal(MigrationItemStatus.Pending, first.Status);
        Assert.NotEqual(string.Empty, first.IdempotencyKey);

        // RowNumber increments across retained items only (placeholders don't consume a row)
        Assert.Equal(1, captured[0].RowNumber);
        Assert.Equal(2, captured[1].RowNumber);
        Assert.Equal(3, captured[2].RowNumber);

        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.S3ScanCompleted,
            Constants.ScopeTypes.Migration,
            m.Id,
            null,
            It.Is<Dictionary<string, object>?>(d => d != null
                && (string)d["bucket"] == "bucket-a"
                && (string)d["prefix"] == "photos/"
                && (int)d["objectsFound"] == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_IdempotencyKeysAreStable()
    {
        var m = MakeS3Migration();
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        _s3.Setup(s => s.ListObjectsAsync(It.IsAny<S3SourceConfigDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<S3ObjectInfo> { new("photos/x.jpg", 1, "e") });

        var captures = new List<List<MigrationItem>>();
        _repo.Setup(r => r.AddItemsAsync(It.IsAny<IEnumerable<MigrationItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MigrationItem>, CancellationToken>((items, _) => captures.Add(items.ToList()))
            .Returns(Task.CompletedTask);

        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        // Reset status to Validating and re-run; idempotency key for the same (migrationId, key)
        // pair must be identical across runs.
        m.Status = MigrationStatus.Validating;
        await CreateHandler().HandleAsync(new S3MigrationScanCommand { MigrationId = m.Id }, CancellationToken.None);

        Assert.Equal(2, captures.Count);
        Assert.Equal(captures[0][0].IdempotencyKey, captures[1][0].IdempotencyKey);
    }
}
