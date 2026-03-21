using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Tests for AssetService.UpdateAsync input validation.
/// Verifies that service-layer guards fire before reaching the database,
/// since Minimal API endpoints do not auto-enforce DataAnnotations.
/// </summary>
[Collection("Database")]
public class AssetServiceValidationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _assetRepo = null!;
    private AssetCollectionRepository _acRepo = null!;
    private CollectionAuthorizationService _authService = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string ContributorUser = "contributor-user-001";

    public AssetServiceValidationTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();

        var cache = TestCacheHelper.CreateHybridCache();
        _assetRepo = new AssetRepository(_db, cache, NullLogger<AssetRepository>.Instance);
        _acRepo = new AssetCollectionRepository(_db, cache, NullLogger<AssetCollectionRepository>.Instance);
        _authService = new CollectionAuthorizationService(_db, NullLogger<CollectionAuthorizationService>.Instance);
        _auditMock = new Mock<IAuditService>();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private AssetService CreateSut(string userId = ContributorUser)
    {
        var minioSettings = Options.Create(new MinIOSettings { BucketName = "test" });

        return new AssetService(
            new AssetServiceRepositories(_assetRepo, _acRepo),
            _authService,
            new Mock<IAssetDeletionService>().Object,
            _auditMock.Object,
            TestCacheHelper.CreateHybridCache(),
            new CurrentUser(userId, isSystemAdmin: false),
            minioSettings);
    }

    private async Task<(Asset asset, Collection col)> SeedContributorAccessAsync()
    {
        var col = TestData.CreateCollection();
        var asset = TestData.CreateAsset(createdByUserId: ContributorUser);
        var acl = TestData.CreateAcl(col.Id, ContributorUser, AclRole.Contributor);
        var ac = new AssetCollection { Id = Guid.NewGuid(), AssetId = asset.Id, CollectionId = col.Id, AddedByUserId = ContributorUser };
        _db.Collections.Add(col);
        _db.Assets.Add(asset);
        _db.CollectionAcls.Add(acl);
        _db.AssetCollections.Add(ac);
        await _db.SaveChangesAsync();
        return (asset, col);
    }

    [Fact]
    public async Task UpdateAsync_EmptyTitle_ReturnsBadRequest()
    {
        var (asset, _) = await SeedContributorAccessAsync();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Title = "" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("required", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_WhitespaceTitle_ReturnsBadRequest()
    {
        var (asset, _) = await SeedContributorAccessAsync();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Title = "   " }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_TitleTooLong_ReturnsBadRequest()
    {
        var (asset, _) = await SeedContributorAccessAsync();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Title = new string('a', 256) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("255", result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionTooLong_ReturnsBadRequest()
    {
        var (asset, _) = await SeedContributorAccessAsync();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Description = new string('x', 2001) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("2000", result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_CopyrightTooLong_ReturnsBadRequest()
    {
        var (asset, _) = await SeedContributorAccessAsync();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Copyright = new string('c', 501) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("500", result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_TooManyTags_ReturnsBadRequest()
    {
        var (asset, _) = await SeedContributorAccessAsync();
        var tooManyTags = Enumerable.Range(0, Constants.Limits.MaxTagsPerAsset + 1).Select(i => $"tag{i}").ToList();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Tags = tooManyTags }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains(Constants.Limits.MaxTagsPerAsset.ToString(), result.Error.Message);
    }

    [Fact]
    public async Task UpdateAsync_ValidTitle_Succeeds()
    {
        var (asset, _) = await SeedContributorAccessAsync();

        var result = await CreateSut().UpdateAsync(asset.Id, new UpdateAssetDto { Title = "New Title" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
    }
}
