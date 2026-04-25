using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

public class BrandServiceTests
{
    private readonly Mock<IBrandRepository> _repo = new();
    private readonly Mock<ICollectionRepository> _collectionRepo = new();
    private readonly Mock<IMinIOAdapter> _minio = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly IOptions<MinIOSettings> _minioSettings = Options.Create(new MinIOSettings { BucketName = "test" });

    private const string AdminId = "admin-1";

    private BrandService Create(string userId = AdminId, bool isAdmin = true)
        => new(_repo.Object, _collectionRepo.Object, _minio.Object, _audit.Object,
               new PassThroughUnitOfWork(),
               new CurrentUser(userId, isAdmin), _minioSettings,
               NullLogger<BrandService>.Instance);

    [Fact]
    public async Task Create_NonAdmin_Forbidden()
    {
        var svc = Create(isAdmin: false);

        var result = await svc.CreateAsync(
            new CreateBrandDto { Name = "x", PrimaryColor = "#fff", SecondaryColor = "#000" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Create_HappyPath_PersistsAndAudits()
    {
        Brand? captured = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<Brand>(), It.IsAny<CancellationToken>()))
            .Callback<Brand, CancellationToken>((b, _) => captured = b)
            .ReturnsAsync((Brand b, CancellationToken _) => b);

        var svc = Create();
        var result = await svc.CreateAsync(new CreateBrandDto
        {
            Name = "Acme",
            PrimaryColor = "#FF0000",
            SecondaryColor = "#00FF00",
            IsDefault = false
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("Acme", captured!.Name);
        Assert.False(captured.IsDefault);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.BrandCreated,
                Constants.ScopeTypes.Brand, It.IsAny<Guid?>(), AdminId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_AsDefault_DemotesOtherDefaults()
    {
        _repo.Setup(r => r.CreateAsync(It.IsAny<Brand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Brand b, CancellationToken _) => b);

        var svc = Create();
        await svc.CreateAsync(new CreateBrandDto
        {
            Name = "x",
            PrimaryColor = "#fff",
            SecondaryColor = "#000",
            IsDefault = true
        }, CancellationToken.None);

        _repo.Verify(r => r.ClearDefaultExceptAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Update_TogglingDefaultOn_DemotesOthers()
    {
        var existing = new Brand
        {
            Id = Guid.NewGuid(),
            Name = "x",
            IsDefault = false,
            PrimaryColor = "#000",
            SecondaryColor = "#fff",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var svc = Create();
        await svc.UpdateAsync(existing.Id, new UpdateBrandDto { IsDefault = true }, CancellationToken.None);

        _repo.Verify(r => r.ClearDefaultExceptAsync(existing.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.True(existing.IsDefault);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Brand?)null);
        var svc = Create();
        var result = await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadLogo_RejectsBadContentType()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Brand { Id = Guid.NewGuid(), Name = "x", PrimaryColor = "#fff", SecondaryColor = "#000" });

        var svc = Create();
        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await svc.UploadLogoAsync(Guid.NewGuid(), ms, "logo.exe", "application/x-msdownload", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UploadLogo_RejectsOversize()
    {
        var brand = new Brand
        {
            Id = Guid.NewGuid(), Name = "x",
            PrimaryColor = "#fff", SecondaryColor = "#000"
        };
        _repo.Setup(r => r.GetByIdAsync(brand.Id, It.IsAny<CancellationToken>())).ReturnsAsync(brand);

        // 2 MB > 1 MB cap.
        var bytes = new byte[2 * 1024 * 1024];
        using var ms = new MemoryStream(bytes);
        var svc = Create();
        var result = await svc.UploadLogoAsync(brand.Id, ms, "logo.png", "image/png", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task AssignToCollection_PersistsBrandId()
    {
        var brand = new Brand
        {
            Id = Guid.NewGuid(), Name = "x",
            PrimaryColor = "#fff", SecondaryColor = "#000"
        };
        var collection = new Collection { Id = Guid.NewGuid(), Name = "C", BrandId = null };

        _repo.Setup(r => r.GetByIdAsync(brand.Id, It.IsAny<CancellationToken>())).ReturnsAsync(brand);
        _collectionRepo.Setup(r => r.GetByIdAsync(collection.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);

        var svc = Create();
        var result = await svc.AssignToCollectionAsync(brand.Id, collection.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(brand.Id, collection.BrandId);
        _collectionRepo.Verify(r => r.UpdateAsync(collection, It.IsAny<CancellationToken>()), Times.Once);
    }
}
