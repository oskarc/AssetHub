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

public class BrandResolverTests
{
    private readonly Mock<IBrandRepository> _brandRepo = new();
    private readonly Mock<ICollectionRepository> _collectionRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<IMinIOAdapter> _minio = new();
    private readonly IOptions<MinIOSettings> _minioSettings = Options.Create(new MinIOSettings { BucketName = "test" });

    private BrandResolver Create()
        => new(_brandRepo.Object, _collectionRepo.Object, _assetCollectionRepo.Object,
               _minio.Object, _minioSettings, NullLogger<BrandResolver>.Instance);

    private static Brand MakeBrand(string name = "Acme") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        PrimaryColor = "#fff",
        SecondaryColor = "#000",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        CreatedByUserId = "admin"
    };

    [Fact]
    public async Task CollectionShare_ReturnsCollectionsBrand()
    {
        var brand = MakeBrand();
        var collection = new Collection { Id = Guid.NewGuid(), Name = "C", BrandId = brand.Id };

        _collectionRepo.Setup(r => r.GetByIdAsync(collection.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _brandRepo.Setup(r => r.GetByIdAsync(brand.Id, It.IsAny<CancellationToken>())).ReturnsAsync(brand);

        var sut = Create();
        var dto = await sut.ResolveForShareAsync(
            Constants.ScopeTypes.Collection, collection.Id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(brand.Id, dto!.Id);
    }

    [Fact]
    public async Task CollectionShare_NoBrandAssigned_FallsBackToDefault()
    {
        var collection = new Collection { Id = Guid.NewGuid(), Name = "C", BrandId = null };
        var defaultBrand = MakeBrand("Default");
        defaultBrand.IsDefault = true;

        _collectionRepo.Setup(r => r.GetByIdAsync(collection.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        _brandRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(defaultBrand);

        var sut = Create();
        var dto = await sut.ResolveForShareAsync(
            Constants.ScopeTypes.Collection, collection.Id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(defaultBrand.Id, dto!.Id);
    }

    [Fact]
    public async Task AssetShare_PicksFirstContainingCollectionWithBrand()
    {
        var assetId = Guid.NewGuid();
        var brand = MakeBrand();
        var unbranded = new Collection { Id = Guid.NewGuid(), Name = "u", BrandId = null };
        var branded = new Collection { Id = Guid.NewGuid(), Name = "b", BrandId = brand.Id };

        _assetCollectionRepo.Setup(r => r.GetCollectionsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Collection> { unbranded, branded });
        _brandRepo.Setup(r => r.GetByIdAsync(brand.Id, It.IsAny<CancellationToken>())).ReturnsAsync(brand);

        var sut = Create();
        var dto = await sut.ResolveForShareAsync(Constants.ScopeTypes.Asset, assetId, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(brand.Id, dto!.Id);
    }

    [Fact]
    public async Task AssetShare_NoBranded_FallsBackToDefault()
    {
        var assetId = Guid.NewGuid();
        var unbranded = new Collection { Id = Guid.NewGuid(), Name = "u", BrandId = null };
        var defaultBrand = MakeBrand("Default");

        _assetCollectionRepo.Setup(r => r.GetCollectionsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Collection> { unbranded });
        _brandRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(defaultBrand);

        var sut = Create();
        var dto = await sut.ResolveForShareAsync(Constants.ScopeTypes.Asset, assetId, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal(defaultBrand.Id, dto!.Id);
    }

    [Fact]
    public async Task NoBrandsAtAll_ReturnsNull()
    {
        var assetId = Guid.NewGuid();
        _assetCollectionRepo.Setup(r => r.GetCollectionsForAssetAsync(assetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Collection>());
        _brandRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Brand?)null);

        var sut = Create();
        var dto = await sut.ResolveForShareAsync(Constants.ScopeTypes.Asset, assetId, CancellationToken.None);

        Assert.Null(dto);
    }

    [Fact]
    public async Task ResolverException_ReturnsNullInsteadOfThrowing()
    {
        // Backend bug must never crash the public share page.
        _assetCollectionRepo.Setup(r => r.GetCollectionsForAssetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var sut = Create();
        var dto = await sut.ResolveForShareAsync(
            Constants.ScopeTypes.Asset, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(dto);
    }
}
