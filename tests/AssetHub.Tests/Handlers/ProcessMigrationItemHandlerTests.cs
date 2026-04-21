using System.Text;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Messages;
using AssetHub.Domain.Entities;
using AssetHub.Tests.Helpers;
using AssetHub.Worker.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Handlers;

public class ProcessMigrationItemHandlerTests
{
    private readonly Mock<IMigrationRepository> _migrationRepo = new();
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<ICollectionRepository> _collectionRepo = new();
    private readonly Mock<ICollectionAclRepository> _aclRepo = new();
    private readonly Mock<IMinIOAdapter> _minio = new();
    private readonly Mock<IMediaProcessingService> _media = new();
    private readonly Mock<IAuditService> _audit = new();

    private const string Bucket = "assets";

    private ProcessMigrationItemHandler CreateHandler()
        => new(
            _migrationRepo.Object,
            _assetRepo.Object,
            _assetCollectionRepo.Object,
            _collectionRepo.Object,
            _aclRepo.Object,
            _minio.Object,
            _media.Object,
            _audit.Object,
            TestCacheHelper.CreateHybridCache(),
            Options.Create(new MinIOSettings { BucketName = Bucket }),
            NullLogger<ProcessMigrationItemHandler>.Instance);

    private static Migration MakeMigration(
        Guid? id = null,
        MigrationStatus status = MigrationStatus.Running,
        bool dryRun = false,
        Guid? defaultCollectionId = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Test",
            SourceType = MigrationSourceType.CsvUpload,
            Status = status,
            DryRun = dryRun,
            DefaultCollectionId = defaultCollectionId,
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        };

    private static MigrationItem MakeItem(
        Guid migrationId,
        string fileName = "photo.jpg",
        string? sha256 = null,
        MigrationItemStatus status = MigrationItemStatus.Pending,
        List<string>? collectionNames = null)
        => new()
        {
            Id = Guid.NewGuid(),
            MigrationId = migrationId,
            FileName = fileName,
            Sha256 = sha256,
            Status = status,
            IsFileStaged = true,
            IdempotencyKey = Guid.NewGuid().ToString(),
            CollectionNames = collectionNames ?? new List<string>(),
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task HandleAsync_ItemNotFound_LogsAndReturns()
    {
        var cmd = new ProcessMigrationItemCommand { MigrationId = Guid.NewGuid(), MigrationItemId = Guid.NewGuid() };
        _migrationRepo.Setup(r => r.GetItemByIdAsync(cmd.MigrationItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MigrationItem?)null);

        await CreateHandler().HandleAsync(cmd, CancellationToken.None);

        _migrationRepo.Verify(r => r.UpdateItemAsync(It.IsAny<MigrationItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(MigrationItemStatus.Succeeded)]
    [InlineData(MigrationItemStatus.Failed)]
    [InlineData(MigrationItemStatus.Skipped)]
    public async Task HandleAsync_TerminalItem_SkipsProcessing(MigrationItemStatus terminal)
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, status: terminal);
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        _migrationRepo.Verify(r => r.UpdateItemAsync(It.IsAny<MigrationItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _migrationRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_MigrationCancelled_MarksItemSkipped()
    {
        var migration = MakeMigration(status: MigrationStatus.Cancelled);
        var item = MakeItem(migration.Id);
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 0, 0, 1, 0, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Skipped, item.Status);
        Assert.Equal(MigrationConstants.ErrorCodes.MigrationCancelled, item.ErrorCode);
        Assert.NotNull(item.ProcessedAt);
    }

    [Fact]
    public async Task HandleAsync_DryRunMissingFilename_Fails()
    {
        var migration = MakeMigration(dryRun: true);
        var item = MakeItem(migration.Id, fileName: "");
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 0, 1, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Failed, item.Status);
        Assert.Equal(MigrationConstants.ErrorCodes.MissingFilename, item.ErrorCode);
        Assert.Equal(1, item.AttemptCount);
    }

    [Fact]
    public async Task HandleAsync_DryRunDuplicateSha256_Skipped()
    {
        var migration = MakeMigration(dryRun: true);
        var item = MakeItem(migration.Id, sha256: "abc123");
        var existing = new Asset
        {
            Id = Guid.NewGuid(),
            Title = "existing",
            AssetType = AssetType.Image,
            OriginalObjectKey = "originals/x.jpg",
            Sha256 = "abc123",
            CreatedByUserId = "user-1"
        };
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _assetRepo.Setup(a => a.GetBySha256Async("abc123", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 0, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Skipped, item.Status);
        Assert.Equal(MigrationConstants.ErrorCodes.Duplicate, item.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_DryRunValid_Succeeded()
    {
        var migration = MakeMigration(dryRun: true);
        var item = MakeItem(migration.Id, sha256: "hash123");
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _assetRepo.Setup(a => a.GetBySha256Async("hash123", It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Succeeded, item.Status);
        Assert.Null(item.ErrorCode);
        Assert.NotNull(item.ProcessedAt);
    }

    [Fact]
    public async Task HandleAsync_StagedFileMissing_Fails()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id);
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 0, 1, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Failed, item.Status);
        Assert.Equal(MigrationConstants.ErrorCodes.FileNotFound, item.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_DuplicateSha256_SkipsAndLinksExistingAsset()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, sha256: "dup");
        var existing = new Asset
        {
            Id = Guid.NewGuid(),
            Title = "existing",
            AssetType = AssetType.Image,
            OriginalObjectKey = "originals/x.jpg",
            Sha256 = "dup",
            CreatedByUserId = "user-1"
        };
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assetRepo.Setup(a => a.GetBySha256Async("dup", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 0, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Skipped, item.Status);
        Assert.Equal(existing.Id, item.AssetId);
        Assert.Equal(MigrationConstants.ErrorCodes.Duplicate, item.ErrorCode);
        _assetRepo.Verify(a => a.CreateAsync(It.IsAny<Asset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_UploadsCreatesAssetAndSchedulesProcessing()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, fileName: "landscape.jpg", sha256: "new-hash");
        item.Title = "My Landscape";

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assetRepo.Setup(a => a.GetBySha256Async("new-hash", It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(12345, "image/jpeg", "etag-1"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("fake-bytes")));

        Asset? createdAsset = null;
        _assetRepo.Setup(a => a.CreateAsync(It.IsAny<Asset>(), It.IsAny<CancellationToken>()))
            .Callback<Asset, CancellationToken>((a, _) => createdAsset = a)
            .Returns(Task.CompletedTask);

        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Succeeded, item.Status);
        Assert.NotNull(createdAsset);
        Assert.Equal(AssetType.Image, createdAsset!.AssetType);
        Assert.Equal("My Landscape", createdAsset.Title);
        Assert.Equal(12345, createdAsset.SizeBytes);
        Assert.Equal("image/jpeg", createdAsset.ContentType);
        Assert.Equal(item.AssetId, createdAsset.Id);

        _minio.Verify(m => m.UploadAsync(
            Bucket,
            It.Is<string>(k => k.StartsWith($"{Constants.StoragePrefixes.Originals}/")),
            It.IsAny<Stream>(),
            "image/jpeg",
            It.IsAny<CancellationToken>()), Times.Once);
        _media.Verify(m => m.ScheduleProcessingAsync(
            createdAsset.Id,
            It.IsAny<string>(),
            createdAsset.OriginalObjectKey,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_AddsToDefaultCollection()
    {
        var defaultColId = Guid.NewGuid();
        var migration = MakeMigration(defaultCollectionId: defaultColId);
        var item = MakeItem(migration.Id);

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(100, "image/png", "etag"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 1, 2, 3 }));
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        _assetCollectionRepo.Verify(a => a.AddToCollectionAsync(
            It.IsAny<Guid>(), defaultColId, migration.CreatedByUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_ResolvesExistingNamedCollection()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, collectionNames: new List<string> { "Vacation 2026" });
        var existingCol = new Collection
        {
            Id = Guid.NewGuid(),
            Name = "Vacation 2026",
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        };

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(50, "image/png", "e"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());
        _collectionRepo.Setup(c => c.GetByNameAsync("Vacation 2026", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCol);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        _collectionRepo.Verify(c => c.CreateAsync(It.IsAny<Collection>(), It.IsAny<CancellationToken>()), Times.Never);
        _assetCollectionRepo.Verify(a => a.AddToCollectionAsync(
            It.IsAny<Guid>(), existingCol.Id, migration.CreatedByUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_CreatesNewCollectionWithAdminAcl()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, collectionNames: new List<string> { "New Album" });

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(50, "image/png", "e"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());
        _collectionRepo.Setup(c => c.GetByNameAsync("New Album", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Collection?)null);
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        Collection? createdCollection = null;
        _collectionRepo.Setup(c => c.CreateAsync(It.IsAny<Collection>(), It.IsAny<CancellationToken>()))
            .Callback<Collection, CancellationToken>((c, _) => createdCollection = c)
            .Returns<Collection, CancellationToken>((c, _) => Task.FromResult(c));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.NotNull(createdCollection);
        Assert.Equal("New Album", createdCollection!.Name);
        _aclRepo.Verify(a => a.SetAccessAsync(
            createdCollection.Id,
            Constants.PrincipalTypes.User,
            migration.CreatedByUserId,
            RoleHierarchy.Roles.Admin,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("image/jpeg", AssetType.Image)]
    [InlineData("image/png", AssetType.Image)]
    [InlineData("video/mp4", AssetType.Video)]
    [InlineData("application/pdf", AssetType.Document)]
    [InlineData("text/plain", AssetType.Document)]
    public async Task HandleAsync_DeterminesAssetType(string contentType, AssetType expected)
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id);

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(50, contentType, "e"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        Asset? createdAsset = null;
        _assetRepo.Setup(a => a.CreateAsync(It.IsAny<Asset>(), It.IsAny<CancellationToken>()))
            .Callback<Asset, CancellationToken>((a, _) => createdAsset = a)
            .Returns(Task.CompletedTask);

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(expected, createdAsset!.AssetType);
    }

    [Fact]
    public async Task HandleAsync_ExceptionDuringProcessing_TruncatesErrorAndFails()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id);
        var longMessage = new string('x', MigrationConstants.Limits.MaxErrorMessageLength + 500);

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(longMessage));
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 0, 1, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationItemStatus.Failed, item.Status);
        Assert.Equal(MigrationConstants.ErrorCodes.ProcessingError, item.ErrorCode);
        Assert.Equal(MigrationConstants.Limits.MaxErrorMessageLength, item.ErrorMessage!.Length);
    }

    [Fact]
    public async Task HandleAsync_Finalize_EmitsCompletedAudit()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, sha256: "s1");
        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assetRepo.Setup(a => a.GetBySha256Async("s1", It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(50, "image/png", "e"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(1, 0, 0, 1, 0, 1, 1, 0));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationStatus.Completed, migration.Status);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Completed,
            Constants.ScopeTypes.Migration,
            migration.Id,
            null,
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Finalize_WithPendingSiblings_DoesNotFinalize()
    {
        var migration = MakeMigration();
        var item = MakeItem(migration.Id, sha256: "s2");

        _migrationRepo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _migrationRepo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _minio.Setup(m => m.ExistsAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assetRepo.Setup(a => a.GetBySha256Async("s2", It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);
        _minio.Setup(m => m.StatObjectAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ObjectStatInfo(50, "image/png", "e"));
        _minio.Setup(m => m.DownloadAsync(Bucket, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream());
        _migrationRepo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationItemCounts(3, 2, 0, 1, 0, 3, 3, 2));

        await CreateHandler().HandleAsync(
            new ProcessMigrationItemCommand { MigrationId = migration.Id, MigrationItemId = item.Id },
            CancellationToken.None);

        Assert.Equal(MigrationStatus.Running, migration.Status);
        _audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
