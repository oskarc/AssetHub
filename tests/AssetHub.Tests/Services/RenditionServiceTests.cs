using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Validation, ACL, and cache-hit logic for the rendition pipeline. The
/// actual resize step (ImageMagick) is behind <see cref="IRenditionImageResizer"/>
/// and mocked here; integration coverage of the magick path lives with
/// the existing image-processing tests.
/// </summary>
public class RenditionServiceTests
{
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<ICollectionAuthorizationService> _authService = new();
    private readonly Mock<IRenditionImageResizer> _resizer = new();
    private readonly Mock<IMinIOAdapter> _minio = new();

    private readonly IOptions<RenditionSettings> _renditionOpts =
        Options.Create(new RenditionSettings());
    private readonly IOptions<MinIOSettings> _minioOpts =
        Options.Create(new MinIOSettings { BucketName = "test" });

    private const string AdminId = "admin-1";

    private RenditionService Create(string userId = AdminId, bool isAdmin = true)
        => new(_assetRepo.Object, _assetCollectionRepo.Object, _authService.Object,
               _resizer.Object, _minio.Object,
               _renditionOpts, _minioOpts,
               new CurrentUser(userId, isAdmin),
               NullLogger<RenditionService>.Instance);

    private static Asset MakeImage(Guid id) => new()
    {
        Id = id,
        Title = "img",
        AssetType = AssetType.Image,
        Status = AssetStatus.Ready,
        ContentType = "image/jpeg",
        SizeBytes = 1024,
        OriginalObjectKey = "originals/img.jpg",
        CreatedAt = DateTime.UtcNow,
        CreatedByUserId = AdminId
    };

    private static RenditionRequest ValidRequest() =>
        new(Width: 400, Height: 200, FitMode: "cover", Format: "webp");

    [Fact]
    public async Task Anonymous_ReturnsForbidden()
    {
        var svc = new RenditionService(
            _assetRepo.Object, _assetCollectionRepo.Object, _authService.Object,
            _resizer.Object, _minio.Object, _renditionOpts, _minioOpts,
            CurrentUser.Anonymous, NullLogger<RenditionService>.Instance);

        var result = await svc.GetOrGenerateAsync(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task NoDimensions_BadRequest()
    {
        var svc = Create();
        var result = await svc.GetOrGenerateAsync(Guid.NewGuid(),
            new RenditionRequest(null, null, "cover", "webp"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Theory]
    [InlineData(123, null)]
    [InlineData(null, 999)]
    public async Task DisallowedDimensions_BadRequest(int? w, int? h)
    {
        var svc = Create();
        var result = await svc.GetOrGenerateAsync(Guid.NewGuid(),
            new RenditionRequest(w, h, "cover", "webp"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DisallowedFitMode_BadRequest()
    {
        var svc = Create();
        var result = await svc.GetOrGenerateAsync(Guid.NewGuid(),
            new RenditionRequest(400, null, "stretch", "webp"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DisallowedFormat_BadRequest()
    {
        var svc = Create();
        var result = await svc.GetOrGenerateAsync(Guid.NewGuid(),
            new RenditionRequest(400, null, "cover", "bmp"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task NonImageAsset_BadRequest()
    {
        var asset = MakeImage(Guid.NewGuid());
        asset.AssetType = AssetType.Video;
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var svc = Create();
        var result = await svc.GetOrGenerateAsync(asset.Id, ValidRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task NoCollectionAccess_Forbidden()
    {
        var asset = MakeImage(Guid.NewGuid());
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });
        _authService.Setup(a => a.FilterAccessibleAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var svc = Create(isAdmin: false);
        var result = await svc.GetOrGenerateAsync(asset.Id, ValidRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CacheHit_ReturnsPresignedUrlWithoutGenerating()
    {
        var asset = MakeImage(Guid.NewGuid());
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _minio.Setup(m => m.ExistsAsync("test", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _minio.Setup(m => m.GetPresignedDownloadUrlAsync(
                "test", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://minio/presigned/cached.webp");

        var svc = Create();
        var result = await svc.GetOrGenerateAsync(asset.Id, ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CacheHit);
        Assert.Equal("https://minio/presigned/cached.webp", result.Value.Url);
        Assert.Equal("image/webp", result.Value.ContentType);
        // Cache hit means the resizer is never invoked.
        _resizer.Verify(r => r.ResizeAsync(It.IsAny<RenditionResizeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CacheMiss_InvokesResizerThenReturnsUrl()
    {
        var asset = MakeImage(Guid.NewGuid());
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _minio.Setup(m => m.ExistsAsync("test", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _minio.Setup(m => m.GetPresignedDownloadUrlAsync(
                "test", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://minio/presigned/fresh.webp");

        RenditionResizeRequest? captured = null;
        _resizer.Setup(r => r.ResizeAsync(It.IsAny<RenditionResizeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RenditionResizeRequest, CancellationToken>((req, _) => captured = req)
            .Returns(Task.CompletedTask);

        var svc = Create();
        var result = await svc.GetOrGenerateAsync(asset.Id, ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CacheHit);
        Assert.NotNull(captured);
        Assert.Equal(asset.OriginalObjectKey, captured!.SourceObjectKey);
        Assert.Equal("image/webp", captured.TargetContentType);
        Assert.Equal(400, captured.Width);
        Assert.Equal(200, captured.Height);
        Assert.Equal("cover", captured.FitMode);
        Assert.Equal("webp", captured.Format);
    }

    [Fact]
    public async Task CacheKey_IsDeterministicAcrossIdenticalRequests()
    {
        var asset = MakeImage(Guid.NewGuid());
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _minio.Setup(m => m.GetPresignedDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("u");

        var keys = new HashSet<string>();
        _minio.Setup(m => m.ExistsAsync("test", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, k, _) => keys.Add(k))
            .ReturnsAsync(true);

        var svc = Create();
        await svc.GetOrGenerateAsync(asset.Id, ValidRequest(), CancellationToken.None);
        await svc.GetOrGenerateAsync(asset.Id, ValidRequest(), CancellationToken.None);

        Assert.Single(keys);
    }

    [Fact]
    public async Task CacheKey_DiffersAcrossDimensions()
    {
        var asset = MakeImage(Guid.NewGuid());
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _minio.Setup(m => m.GetPresignedDownloadUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("u");

        var keys = new HashSet<string>();
        _minio.Setup(m => m.ExistsAsync("test", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, k, _) => keys.Add(k))
            .ReturnsAsync(true);

        var svc = Create();
        await svc.GetOrGenerateAsync(asset.Id,
            new RenditionRequest(400, null, "cover", "webp"), CancellationToken.None);
        await svc.GetOrGenerateAsync(asset.Id,
            new RenditionRequest(800, null, "cover", "webp"), CancellationToken.None);

        Assert.Equal(2, keys.Count);
    }
}
