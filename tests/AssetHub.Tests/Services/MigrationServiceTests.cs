using System.Text;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;

namespace AssetHub.Tests.Services;

public class MigrationServiceTests
{
    private readonly Mock<IMigrationRepository> _repo = new();
    private readonly Mock<ICollectionRepository> _collectionRepo = new();
    private readonly Mock<ICollectionAclRepository> _aclRepo = new();
    private readonly Mock<IMinIOAdapter> _minio = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly Mock<IMigrationSecretProtector> _secretProtector = new();

    private const string AdminUserId = "admin-001";

    public MigrationServiceTests()
    {
        // Default protector: round-trip through a `enc(...)` wrapper so tests can assert
        // that the plaintext secret never leaks into persisted state.
        _secretProtector.Setup(p => p.Protect(It.IsAny<string>()))
            .Returns<string>(s => $"enc({s})");
        _secretProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns<string>(s => s.StartsWith("enc(") && s.EndsWith(')') ? s[4..^1] : s);
    }

    private MigrationService CreateService(bool isAdmin = true)
    {
        var currentUser = new CurrentUser(AdminUserId, isAdmin);
        var minioSettings = Options.Create(new MinIOSettings { BucketName = "test-bucket" });
        return new MigrationService(
            _repo.Object,
            _collectionRepo.Object,
            _aclRepo.Object,
            _minio.Object,
            minioSettings,
            _audit.Object,
            _bus.Object,
            _secretProtector.Object,
            TestCacheHelper.CreateHybridCache(),
            currentUser,
            NullLogger<MigrationService>.Instance);
    }

    private static Migration MakeMigration(
        MigrationStatus status = MigrationStatus.Draft,
        int total = 0,
        Guid? defaultCollectionId = null,
        bool dryRun = false)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = "Test migration",
            SourceType = MigrationSourceType.CsvUpload,
            Status = status,
            DefaultCollectionId = defaultCollectionId,
            ItemsTotal = total,
            DryRun = dryRun,
            CreatedByUserId = AdminUserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

    private static MigrationItemCounts Counts(int pending = 0, int succeeded = 0, int failed = 0, int skipped = 0, int staged = 0)
        => new(
            Total: pending + succeeded + failed + skipped,
            Pending: pending,
            Processing: 0,
            Succeeded: succeeded,
            Failed: failed,
            Skipped: skipped,
            Staged: staged,
            StagedPending: Math.Min(staged, pending));

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "csv_upload" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_BothCollectionFields_ReturnsBadRequest()
    {
        var svc = CreateService();

        var result = await svc.CreateAsync(
            new CreateMigrationDto
            {
                Name = "X",
                SourceType = "csv_upload",
                DefaultCollectionId = Guid.NewGuid(),
                DefaultCollectionName = "Also-a-name"
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_DefaultCollectionIdMissing_ReturnsBadRequest()
    {
        var svc = CreateService();
        var cid = Guid.NewGuid();
        _collectionRepo.Setup(r => r.ExistsAsync(cid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "csv_upload", DefaultCollectionId = cid },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_DefaultCollectionNameReusesExisting()
    {
        var svc = CreateService();
        var existing = TestData.CreateCollection(name: "Existing");
        _collectionRepo.Setup(r => r.GetByNameAsync("Existing", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "csv_upload", DefaultCollectionName = "Existing" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value!.DefaultCollectionId);
        _collectionRepo.Verify(r => r.CreateAsync(It.IsAny<Collection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DefaultCollectionNameNew_CreatesCollectionAndAdminAcl()
    {
        var svc = CreateService();
        _collectionRepo.Setup(r => r.GetByNameAsync("Fresh", It.IsAny<CancellationToken>())).ReturnsAsync((Collection?)null);

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "csv_upload", DefaultCollectionName = "Fresh" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _collectionRepo.Verify(r => r.CreateAsync(It.Is<Collection>(c => c.Name == "Fresh"), It.IsAny<CancellationToken>()), Times.Once);
        _aclRepo.Verify(r => r.SetAccessAsync(
            It.IsAny<Guid>(),
            Constants.PrincipalTypes.User,
            AdminUserId,
            RoleHierarchy.Roles.Admin,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_HappyPath_EmitsCreatedAuditAndReturnsDraft()
    {
        var svc = CreateService();

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = " Trimmed ", SourceType = "csv_upload", DryRun = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Trimmed", result.Value!.Name);
        Assert.Equal("draft", result.Value.Status);
        Assert.True(result.Value.DryRun);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Created,
            Constants.ScopeTypes.Migration,
            It.IsAny<Guid>(),
            AdminUserId,
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.CreateAsync(It.IsAny<Migration>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateAsync — S3 ────────────────────────────────────────────

    private static S3SourceConfigDto ValidS3Config() => new()
    {
        Endpoint = "https://s3.eu-west-1.amazonaws.com",
        Bucket = "my-bucket",
        Prefix = "images/",
        AccessKey = "AKIA...",
        SecretKey = "super-secret",
        Region = "eu-west-1"
    };

    [Fact]
    public async Task CreateAsync_S3SourceMissingConfig_ReturnsBadRequest()
    {
        var svc = CreateService();

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "s3", S3Config = null },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_CsvSourceWithS3Config_ReturnsBadRequest()
    {
        var svc = CreateService();

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "csv_upload", S3Config = ValidS3Config() },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_S3Source_PersistsEncryptedSecretInSourceConfig()
    {
        var svc = CreateService();
        Migration? captured = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<Migration>(), It.IsAny<CancellationToken>()))
            .Callback<Migration, CancellationToken>((m, _) => captured = m)
            .ReturnsAsync((Migration m, CancellationToken _) => m);

        var result = await svc.CreateAsync(
            new CreateMigrationDto { Name = "X", SourceType = "s3", S3Config = ValidS3Config() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(MigrationSourceType.S3, captured!.SourceType);
        // Non-secret fields persist as-is
        Assert.Equal("my-bucket", captured.SourceConfig[MigrationS3ConfigCodec.Keys.Bucket]);
        Assert.Equal("images/", captured.SourceConfig[MigrationS3ConfigCodec.Keys.Prefix]);
        Assert.Equal("AKIA...", captured.SourceConfig[MigrationS3ConfigCodec.Keys.AccessKey]);
        // Secret key is the protector output, never the plaintext
        Assert.Equal("enc(super-secret)", captured.SourceConfig[MigrationS3ConfigCodec.Keys.SecretKeyEncrypted]);
        Assert.False(captured.SourceConfig.ContainsKey("secret_key"));
    }

    // ── StartS3ScanAsync ────────────────────────────────────────────

    [Fact]
    public async Task StartS3ScanAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.StartS3ScanAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartS3ScanAsync_MigrationNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Migration?)null);

        var result = await svc.StartS3ScanAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartS3ScanAsync_CsvMigration_ReturnsBadRequest()
    {
        var svc = CreateService();
        var m = MakeMigration();  // CsvUpload source
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        var result = await svc.StartS3ScanAsync(m.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartS3ScanAsync_NotDraft_ReturnsBadRequest()
    {
        var svc = CreateService();
        var m = MakeS3Migration(MigrationStatus.Running);
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        var result = await svc.StartS3ScanAsync(m.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartS3ScanAsync_CorruptedConfig_ReturnsBadRequest()
    {
        var svc = CreateService();
        var m = MakeS3Migration(MigrationStatus.Draft);
        m.SourceConfig.Clear();  // missing required keys — Read() throws
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        var result = await svc.StartS3ScanAsync(m.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _bus.Verify(b => b.PublishAsync(It.IsAny<S3MigrationScanCommand>(), It.IsAny<DeliveryOptions?>()), Times.Never);
    }

    [Fact]
    public async Task StartS3ScanAsync_HappyPath_TransitionsToValidatingAndPublishes()
    {
        var svc = CreateService();
        var m = MakeS3Migration(MigrationStatus.Draft);
        _repo.Setup(r => r.GetByIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(m);

        var result = await svc.StartS3ScanAsync(m.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationStatus.Validating, m.Status);
        _bus.Verify(b => b.PublishAsync(
            It.Is<S3MigrationScanCommand>(c => c.MigrationId == m.Id),
            It.IsAny<DeliveryOptions?>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.S3ScanStarted,
            Constants.ScopeTypes.Migration,
            m.Id,
            AdminUserId,
            It.Is<Dictionary<string, object>?>(d =>
                d != null &&
                (string)d["bucket"] == "my-bucket" &&
                (string)d["prefix"] == "images/"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private Migration MakeS3Migration(MigrationStatus status)
    {
        var m = new Migration
        {
            Id = Guid.NewGuid(),
            Name = "S3 test",
            SourceType = MigrationSourceType.S3,
            Status = status,
            CreatedByUserId = AdminUserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            SourceConfig = MigrationS3ConfigCodec.Write(ValidS3Config(), _secretProtector.Object)
        };
        return m;
    }

    // ── UploadManifestAsync ─────────────────────────────────────────

    [Fact]
    public async Task UploadManifestAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.UploadManifestAsync(Guid.NewGuid(), new MemoryStream(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadManifestAsync_MigrationNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Migration?)null);

        var result = await svc.UploadManifestAsync(id, new MemoryStream(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadManifestAsync_NotDraft_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Running);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.UploadManifestAsync(migration.Id, new MemoryStream(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadManifestAsync_EmptyCsv_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var csv = new MemoryStream(Encoding.UTF8.GetBytes("filename,title\n"));
        var result = await svc.UploadManifestAsync(migration.Id, csv, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _repo.Verify(r => r.RemoveAllItemsAsync(migration.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadManifestAsync_ValidCsv_PersistsItemsAndTotal()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        List<MigrationItem>? captured = null;
        _repo.Setup(r => r.AddItemsAsync(It.IsAny<IEnumerable<MigrationItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MigrationItem>, CancellationToken>((items, _) => captured = items.ToList())
            .Returns(Task.CompletedTask);

        var csv = new MemoryStream(Encoding.UTF8.GetBytes(
            "filename,title,description,tags,collection_names,sha256,metadata.campaign\n" +
            "a.jpg,Alpha,\"Alpha asset, quoted\",\"tag1;tag2\",\"CollA;CollB\",abc123,Spring\n" +
            "b.jpg,,,,,,\n"));

        var result = await svc.UploadManifestAsync(migration.Id, csv, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);

        var first = captured[0];
        Assert.Equal("a.jpg", first.FileName);
        Assert.Equal("Alpha", first.Title);
        Assert.Equal("Alpha asset, quoted", first.Description);
        Assert.Equal(new[] { "tag1", "tag2" }, first.Tags);
        Assert.Equal(new[] { "CollA", "CollB" }, first.CollectionNames);
        Assert.Equal("abc123", first.Sha256);
        Assert.Equal("Spring", first.MetadataJson["campaign"]);

        // Second row: all optional fields blank → uses filename-without-extension as title fallback
        Assert.Equal("b.jpg", captured[1].FileName);
        Assert.Equal("b", captured[1].Title);

        _repo.Verify(r => r.UpdateAsync(
            It.Is<Migration>(m => m.ItemsTotal == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadManifestAsync_ReUploadRemovesExistingItemsFirst()
    {
        var svc = CreateService();
        var migration = MakeMigration(total: 5);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var csv = new MemoryStream(Encoding.UTF8.GetBytes("filename\nx.jpg\n"));
        await svc.UploadManifestAsync(migration.Id, csv, CancellationToken.None);

        _repo.Verify(r => r.RemoveAllItemsAsync(migration.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadManifestAsync_RowWithoutFilenameIsSkipped()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        List<MigrationItem>? captured = null;
        _repo.Setup(r => r.AddItemsAsync(It.IsAny<IEnumerable<MigrationItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MigrationItem>, CancellationToken>((items, _) => captured = items.ToList())
            .Returns(Task.CompletedTask);

        var csv = new MemoryStream(Encoding.UTF8.GetBytes(
            "filename,title\n" +
            ",OrphanTitle\n" +
            "ok.jpg,Ok\n"));

        await svc.UploadManifestAsync(migration.Id, csv, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal("ok.jpg", captured![0].FileName);
    }

    [Fact]
    public async Task UploadManifestAsync_ExternalIdFallsBackToFilename()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        List<MigrationItem>? captured = null;
        _repo.Setup(r => r.AddItemsAsync(It.IsAny<IEnumerable<MigrationItem>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<MigrationItem>, CancellationToken>((items, _) => captured = items.ToList())
            .Returns(Task.CompletedTask);

        var csv = new MemoryStream(Encoding.UTF8.GetBytes("filename\nfoo.jpg\n"));
        await svc.UploadManifestAsync(migration.Id, csv, CancellationToken.None);

        Assert.Equal("foo.jpg", captured![0].ExternalId);
        Assert.False(string.IsNullOrEmpty(captured[0].IdempotencyKey));
    }

    // ── StartAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.StartAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Migration?)null);

        var result = await svc.StartAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartAsync_NotDraftOrPartial_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Completed, total: 10);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.StartAsync(migration.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartAsync_NoItems_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Draft, total: 0);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.StartAsync(migration.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task StartAsync_HappyPath_SetsRunningPublishesCommandEmitsAudit()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Draft, total: 3);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.StartAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationStatus.Running, migration.Status);
        Assert.NotNull(migration.StartedAt);
        _bus.Verify(b => b.PublishAsync(
            It.Is<StartMigrationCommand>(c => c.MigrationId == migration.Id),
            It.IsAny<DeliveryOptions?>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Started,
            Constants.ScopeTypes.Migration,
            migration.Id,
            AdminUserId,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_FromPartiallyCompleted_AllowedForResume()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.PartiallyCompleted, total: 2);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.StartAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationStatus.Running, migration.Status);
    }

    // ── CancelAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.CancelAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CancelAsync_NotRunning_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Draft);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.CancelAsync(migration.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CancelAsync_HappyPath_SetsCancelledWithFinishedAtAndAudit()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Running, total: 3);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Counts(succeeded: 2, failed: 1));

        var result = await svc.CancelAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MigrationStatus.Cancelled, migration.Status);
        Assert.NotNull(migration.FinishedAt);
        Assert.Equal(2, migration.ItemsSucceeded);
        Assert.Equal(1, migration.ItemsFailed);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Cancelled,
            Constants.ScopeTypes.Migration,
            migration.Id,
            AdminUserId,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_RunningMigration_RefreshesCountsFromItems()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Running, total: 10);
        migration.ItemsSucceeded = 0;
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Counts(pending: 3, succeeded: 5, failed: 2, staged: 7));

        var result = await svc.GetByIdAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.ItemsSucceeded);
        Assert.Equal(2, result.Value.ItemsFailed);
        Assert.Equal(7, result.Value.ItemsStaged);
    }

    // ── ListAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.ListAsync(0, 20, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ListAsync_ReturnsDtosWithStagedCounts()
    {
        var svc = CreateService();
        var m1 = MakeMigration();
        var m2 = MakeMigration(MigrationStatus.Completed);
        _repo.Setup(r => r.ListAsync(0, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Migration> { m1, m2 });
        _repo.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _repo.Setup(r => r.GetItemCountsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Counts(staged: 4));

        var result = await svc.ListAsync(0, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Migrations, dto => Assert.Equal(4, dto.ItemsStaged));
    }

    // ── GetProgressAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetProgressAsync_NonAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.GetProgressAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetProgressAsync_ComputesPercentage()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Running, total: 10);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemCountsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Counts(pending: 2, succeeded: 6, failed: 1, skipped: 1, staged: 8));

        var result = await svc.GetProgressAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8, result.Value!.ItemsProcessed);
        Assert.Equal(80, result.Value.ProgressPercent);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RunningMigration_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Running);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.DeleteAsync(migration.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_CompletedMigration_DeletesAndEmitsAudit()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Completed);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.DeleteAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.DeleteAsync(migration.Id, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Deleted,
            Constants.ScopeTypes.Migration,
            migration.Id,
            AdminUserId,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RetryFailedAsync ────────────────────────────────────────────

    [Fact]
    public async Task RetryFailedAsync_NotInFailedState_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Completed);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var result = await svc.RetryFailedAsync(migration.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RetryFailedAsync_NoFailedItems_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.CompletedWithErrors);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetFailedItemsAsync(migration.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MigrationItem>());

        var result = await svc.RetryFailedAsync(migration.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RetryFailedAsync_ResetsFailedItemsAndPublishesCommand()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.CompletedWithErrors);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        var failed = new List<MigrationItem>
        {
            new() { Id = Guid.NewGuid(), MigrationId = migration.Id, Status = MigrationItemStatus.Failed, ErrorCode = "X", ErrorMessage = "msg", FileName = "a" },
            new() { Id = Guid.NewGuid(), MigrationId = migration.Id, Status = MigrationItemStatus.Failed, ErrorCode = "Y", ErrorMessage = "msg2", FileName = "b" }
        };
        _repo.Setup(r => r.GetFailedItemsAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(failed);

        var result = await svc.RetryFailedAsync(migration.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(failed, i =>
        {
            Assert.Equal(MigrationItemStatus.Pending, i.Status);
            Assert.Null(i.ErrorCode);
            Assert.Null(i.ErrorMessage);
        });
        Assert.Equal(MigrationStatus.Running, migration.Status);
        _bus.Verify(b => b.PublishAsync(
            It.Is<StartMigrationCommand>(c => c.MigrationId == migration.Id),
            It.IsAny<DeliveryOptions?>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.Retried,
            Constants.ScopeTypes.Migration,
            migration.Id,
            AdminUserId,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UploadStagingFileAsync ─────────────────────────────────────

    [Fact]
    public async Task UploadStagingFileAsync_UploadsToMinIOAndMarksStaged()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await svc.UploadStagingFileAsync(migration.Id, "photo.jpg", stream, "image/jpeg", CancellationToken.None);

        Assert.True(result.IsSuccess);
        _minio.Verify(m => m.UploadAsync(
            "test-bucket",
            MigrationConstants.StagingKey(migration.Id, "photo.jpg"),
            stream,
            "image/jpeg",
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.MarkItemsStagedAsync(
            migration.Id,
            It.Is<IEnumerable<string>>(s => s.Contains("photo.jpg")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadStagingFileAsync_MigrationRunning_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration(MigrationStatus.Running);
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        using var stream = new MemoryStream();
        var result = await svc.UploadStagingFileAsync(migration.Id, "x.jpg", stream, "image/jpeg", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _minio.Verify(m => m.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadStagingFileAsync_SanitizesPathTraversal()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);

        using var stream = new MemoryStream();
        await svc.UploadStagingFileAsync(migration.Id, "../../../etc/passwd", stream, "text/plain", CancellationToken.None);

        _minio.Verify(m => m.UploadAsync(
            "test-bucket",
            It.Is<string>(k => !k.Contains("..") && k.StartsWith($"migrations/{migration.Id}/staging/")),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UnstageMigrationItemAsync ──────────────────────────────────

    [Fact]
    public async Task UnstageMigrationItemAsync_ItemNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((MigrationItem?)null);

        var result = await svc.UnstageMigrationItemAsync(migration.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UnstageMigrationItemAsync_ItemNotStaged_ReturnsBadRequest()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        var item = new MigrationItem { Id = Guid.NewGuid(), MigrationId = migration.Id, FileName = "x.jpg", IsFileStaged = false };
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await svc.UnstageMigrationItemAsync(migration.Id, item.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UnstageMigrationItemAsync_HappyPath_DeletesStagingAndUnmarks()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        var item = new MigrationItem { Id = Guid.NewGuid(), MigrationId = migration.Id, FileName = "x.jpg", IsFileStaged = true };
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await svc.UnstageMigrationItemAsync(migration.Id, item.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(item.IsFileStaged);
        _minio.Verify(m => m.DeleteAsync("test-bucket", MigrationConstants.StagingKey(migration.Id, "x.jpg"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpdateItemAsync(item, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnstageMigrationItemAsync_MinIODeleteFails_StillUnmarks()
    {
        var svc = CreateService();
        var migration = MakeMigration();
        var item = new MigrationItem { Id = Guid.NewGuid(), MigrationId = migration.Id, FileName = "x.jpg", IsFileStaged = true };
        _repo.Setup(r => r.GetByIdAsync(migration.Id, It.IsAny<CancellationToken>())).ReturnsAsync(migration);
        _repo.Setup(r => r.GetItemByIdAsync(item.Id, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _minio.Setup(m => m.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("simulated"));

        var result = await svc.UnstageMigrationItemAsync(migration.Id, item.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(item.IsFileStaged);
    }

    // ── BulkDeleteAsync ─────────────────────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_InvalidFilter_ReturnsBadRequest()
    {
        var svc = CreateService();

        var result = await svc.BulkDeleteAsync("not-a-filter", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("draft")]
    [InlineData("all")]
    public async Task BulkDeleteAsync_ValidFilter_DeletesAndEmitsAudit(string filter)
    {
        var svc = CreateService();
        _repo.Setup(r => r.DeleteByStatusAsync(It.IsAny<IReadOnlyList<MigrationStatus>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await svc.BulkDeleteAsync(filter, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        _audit.Verify(a => a.LogAsync(
            MigrationConstants.AuditEvents.BulkDeleted,
            Constants.ScopeTypes.Migration,
            null,
            AdminUserId,
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
