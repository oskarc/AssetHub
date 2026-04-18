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

/// <summary>
/// Unit tests for ImageEditingService — replace, copy, copy-with-presets save modes,
/// and ACL denial paths.
/// </summary>
public class ImageEditingServiceTests
{
    private readonly Mock<IAssetRepository> _assetRepoMock = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepoMock = new();
    private readonly Mock<IExportPresetRepository> _presetRepoMock = new();
    private readonly Mock<IAssetUploadService> _uploadServiceMock = new();
    private readonly Mock<IMinIOAdapter> _minioAdapterMock = new();
    private readonly Mock<ICollectionAuthorizationService> _authServiceMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<IMessageBus> _messageBusMock = new();
    private readonly IOptions<MinIOSettings> _minioSettings = Options.Create(new MinIOSettings { BucketName = "test-bucket" });

    private static readonly Guid SourceAssetId = Guid.NewGuid();
    private static readonly Guid CollectionId = Guid.NewGuid();

    private ImageEditingService CreateService(string userId = "user-001", bool isAdmin = false)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new ImageEditingService(
            _assetRepoMock.Object,
            _assetCollectionRepoMock.Object,
            _presetRepoMock.Object,
            _uploadServiceMock.Object,
            _minioAdapterMock.Object,
            _authServiceMock.Object,
            _auditMock.Object,
            _messageBusMock.Object,
            currentUser,
            _minioSettings,
            NullLogger<ImageEditingService>.Instance);
    }

    private static Asset CreateSourceAsset(Guid? id = null)
    {
        return TestData.CreateAsset(
            id: id ?? SourceAssetId,
            title: "Test Image",
            assetType: AssetType.Image,
            status: AssetStatus.Ready);
    }

    private void SetupSourceAssetFound(Asset? asset = null)
    {
        var a = asset ?? CreateSourceAsset();
        _assetRepoMock.Setup(r => r.GetByIdAsync(a.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(a);
        _assetCollectionRepoMock.Setup(r => r.GetCollectionIdsForAssetAsync(a.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { CollectionId });
    }

    private void SetupContributorAccess(string userId = "user-001")
    {
        _authServiceMock.Setup(a => a.FilterAccessibleAsync(
                userId, It.IsAny<IEnumerable<Guid>>(), "contributor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { CollectionId });
    }

    private void SetupManagerAccess(string userId = "user-001")
    {
        _authServiceMock.Setup(a => a.FilterAccessibleAsync(
                userId, It.IsAny<IEnumerable<Guid>>(), "manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { CollectionId });
        _authServiceMock.Setup(a => a.FilterAccessibleAsync(
                userId, It.IsAny<IEnumerable<Guid>>(), "contributor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { CollectionId });
    }

    private void SetupNoManagerAccess(string userId = "user-001")
    {
        _authServiceMock.Setup(a => a.FilterAccessibleAsync(
                userId, It.IsAny<IEnumerable<Guid>>(), "manager", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private void SetupNoContributorAccess(string userId = "user-001")
    {
        _authServiceMock.Setup(a => a.FilterAccessibleAsync(
                userId, It.IsAny<IEnumerable<Guid>>(), "contributor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private void SetupSuccessfulReplace()
    {
        _uploadServiceMock.Setup(s => s.ReplaceImageFileAsync(
                It.IsAny<Guid>(), It.IsAny<ReplaceImageFileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitUploadResponse
            {
                AssetId = SourceAssetId,
                ObjectKey = "originals/test.png",
                UploadUrl = "https://example.com/upload",
                ExpiresInSeconds = 3600
            });

        _uploadServiceMock.Setup(s => s.ConfirmPreScannedUploadAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetUploadResult
            {
                Id = SourceAssetId,
                Status = "processing"
            });
    }

    private void SetupSuccessfulCopy(Guid? newAssetId = null)
    {
        var copyId = newAssetId ?? Guid.NewGuid();
        _uploadServiceMock.Setup(s => s.SaveImageCopyAsync(
                It.IsAny<Guid>(), It.IsAny<SaveImageCopyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InitUploadResponse
            {
                AssetId = copyId,
                ObjectKey = $"originals/{copyId}.png",
                UploadUrl = "https://example.com/upload",
                ExpiresInSeconds = 3600
            });

        _uploadServiceMock.Setup(s => s.ConfirmPreScannedUploadAsync(
                copyId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetUploadResult
            {
                Id = copyId,
                Status = "processing"
            });

        _assetRepoMock.Setup(r => r.GetByIdAsync(copyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestData.CreateAsset(id: copyId, title: "Test Image (edited)"));
    }

    // ── Source asset validation ─────────────────────────────────────

    [Fact]
    public async Task ApplyEditAsync_SourceNotFound_ReturnsNotFound()
    {
        _assetRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Asset?)null);

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Copy };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(Guid.NewGuid(), dto, stream, "test.png", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ApplyEditAsync_NotAnImage_ReturnsBadRequest()
    {
        var videoAsset = TestData.CreateAsset(assetType: AssetType.Video);
        _assetRepoMock.Setup(r => r.GetByIdAsync(videoAsset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(videoAsset);

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Copy };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(videoAsset.Id, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ApplyEditAsync_NoCollectionAccess_ReturnsForbidden()
    {
        SetupSourceAssetFound();
        SetupNoContributorAccess();

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Copy };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── Replace mode ────────────────────────────────────────────────

    [Fact]
    public async Task ApplyEditAsync_Replace_ManagerAccess_ReturnsSuccess()
    {
        SetupSourceAssetFound();
        SetupManagerAccess();
        SetupSuccessfulReplace();

        var svc = CreateService();
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.Replace,
            EditDocument = """{"v":1,"layers":[]}"""
        };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SourceAssetId, result.Value!.AssetId);
    }

    [Fact]
    public async Task ApplyEditAsync_Replace_NoManagerAccess_ReturnsForbidden()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();
        SetupNoManagerAccess();

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Replace };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ApplyEditAsync_Replace_StoresEditDocument()
    {
        SetupSourceAssetFound();
        SetupManagerAccess();
        SetupSuccessfulReplace();

        var svc = CreateService();
        var editDoc = """{"v":1,"layers":[{"type":"text"}]}""";
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.Replace,
            EditDocument = editDoc
        };
        using var stream = new MemoryStream(new byte[10]);

        await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        _assetRepoMock.Verify(r => r.UpdateAsync(
            It.Is<Asset>(a => a.EditDocument == editDoc), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyEditAsync_Replace_LogsAuditEvent()
    {
        SetupSourceAssetFound();
        SetupManagerAccess();
        SetupSuccessfulReplace();

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Replace };
        using var stream = new MemoryStream(new byte[10]);

        await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        _auditMock.Verify(a => a.LogAsync(
            "asset.edited", "asset", SourceAssetId, "user-001",
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Copy mode ───────────────────────────────────────────────────

    [Fact]
    public async Task ApplyEditAsync_Copy_ContributorAccess_ReturnsSuccess()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();
        SetupSuccessfulCopy();

        var svc = CreateService();
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.Copy,
            Title = "My Edited Copy"
        };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.AssetId);
    }

    [Fact]
    public async Task ApplyEditAsync_Copy_SetsSourceAssetId()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();
        var copyId = Guid.NewGuid();
        SetupSuccessfulCopy(copyId);

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Copy };
        using var stream = new MemoryStream(new byte[10]);

        await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        _assetRepoMock.Verify(r => r.UpdateAsync(
            It.Is<Asset>(a => a.SourceAssetId == SourceAssetId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyEditAsync_Copy_LogsAuditEvent()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();
        SetupSuccessfulCopy();

        var svc = CreateService();
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Copy };
        using var stream = new MemoryStream(new byte[10]);

        await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        _auditMock.Verify(a => a.LogAsync(
            "asset.edited", "asset", It.IsAny<Guid>(), "user-001",
            It.Is<Dictionary<string, object>>(d => d.ContainsKey("sourceAssetId")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CopyWithPresets mode ────────────────────────────────────────

    [Fact]
    public async Task ApplyEditAsync_CopyWithPresets_ValidPresets_ReturnsSuccess()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();
        var copyId = Guid.NewGuid();
        SetupSuccessfulCopy(copyId);

        var presetIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var presets = presetIds.Select(id => TestData.CreateExportPreset(id: id)).ToList();
        _presetRepoMock.Setup(r => r.GetByIdsAsync(presetIds, It.IsAny<CancellationToken>()))
            .ReturnsAsync(presets);

        var svc = CreateService();
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.CopyWithPresets,
            PresetIds = presetIds
        };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(copyId, result.Value!.AssetId);
    }

    [Fact]
    public async Task ApplyEditAsync_CopyWithPresets_NoPresetIds_ReturnsBadRequest()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();

        var svc = CreateService();
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.CopyWithPresets,
            PresetIds = Array.Empty<Guid>()
        };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ApplyEditAsync_CopyWithPresets_InvalidPresetIds_ReturnsBadRequest()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();

        var presetIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        // Only one returned — mismatch
        _presetRepoMock.Setup(r => r.GetByIdsAsync(presetIds, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExportPreset> { TestData.CreateExportPreset(id: presetIds[0]) });

        var svc = CreateService();
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.CopyWithPresets,
            PresetIds = presetIds
        };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task ApplyEditAsync_CopyWithPresets_PublishesWolverineCommand()
    {
        SetupSourceAssetFound();
        SetupContributorAccess();
        var copyId = Guid.NewGuid();
        SetupSuccessfulCopy(copyId);

        var presetIds = new[] { Guid.NewGuid() };
        _presetRepoMock.Setup(r => r.GetByIdsAsync(presetIds, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExportPreset> { TestData.CreateExportPreset(id: presetIds[0]) });

        var svc = CreateService();
        var dto = new ImageEditRequestDto
        {
            SaveMode = ImageEditSaveMode.CopyWithPresets,
            PresetIds = presetIds
        };
        using var stream = new MemoryStream(new byte[10]);

        await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        _messageBusMock.Verify(m => m.PublishAsync(
            It.Is<ApplyExportPresetsCommand>(c =>
                c.SourceAssetId == copyId &&
                c.PresetIds.Count == 1 &&
                c.RequestedByUserId == "user-001"),
            It.IsAny<DeliveryOptions>()), Times.Once);
    }

    // ── Admin bypass ────────────────────────────────────────────────

    [Fact]
    public async Task ApplyEditAsync_Replace_AdminBypassesAcl_ReturnsSuccess()
    {
        SetupSourceAssetFound();
        SetupSuccessfulReplace();
        // No ACL setup needed — admin bypasses

        var svc = CreateService(userId: "admin-001", isAdmin: true);
        var dto = new ImageEditRequestDto { SaveMode = ImageEditSaveMode.Replace };
        using var stream = new MemoryStream(new byte[10]);

        var result = await svc.ApplyEditAsync(SourceAssetId, dto, stream, "test.png", 10, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
