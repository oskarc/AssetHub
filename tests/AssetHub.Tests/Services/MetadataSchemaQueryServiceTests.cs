using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Moq;

namespace AssetHub.Tests.Services;

public class MetadataSchemaQueryServiceTests
{
    private readonly Mock<IMetadataSchemaRepository> _repo = new();
    private MetadataSchemaQueryService CreateService() => new(_repo.Object);

    [Fact]
    public async Task GetAllAsync_ReturnsDtoList()
    {
        var svc = CreateService();
        var schemas = new List<MetadataSchema>
        {
            TestData.CreateMetadataSchema(name: "A"),
            TestData.CreateMetadataSchema(name: "B")
        };
        _repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(schemas);

        var result = await svc.GetAllAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("A", result.Value[0].Name);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((MetadataSchema?)null);

        var result = await svc.GetByIdAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsDtoWithFields()
    {
        var svc = CreateService();
        var field = TestData.CreateMetadataField(key: "title", type: MetadataFieldType.Text);
        var schema = TestData.CreateMetadataSchema(name: "Basic", fields: new() { field });
        _repo.Setup(r => r.GetByIdAsync(schema.Id, It.IsAny<CancellationToken>())).ReturnsAsync(schema);

        var result = await svc.GetByIdAsync(schema.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Basic", result.Value!.Name);
        Assert.Single(result.Value.Fields);
        Assert.Equal("title", result.Value.Fields[0].Key);
    }

    [Fact]
    public async Task GetApplicableAsync_InvalidAssetType_ReturnsBadRequest()
    {
        var svc = CreateService();

        var result = await svc.GetApplicableAsync("audio", collectionId: null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetApplicableAsync_ValidAssetType_CallsRepoWithParsedType()
    {
        var svc = CreateService();
        _repo.Setup(r => r.GetApplicableAsync(AssetType.Image, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema> { TestData.CreateMetadataSchema(name: "Image stuff") });

        var result = await svc.GetApplicableAsync("image", collectionId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        _repo.Verify(r => r.GetApplicableAsync(AssetType.Image, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetApplicableAsync_NullAssetType_SkipsValidationAndPassesNull()
    {
        var svc = CreateService();
        var cid = Guid.NewGuid();
        _repo.Setup(r => r.GetApplicableAsync(null, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema>());

        var result = await svc.GetApplicableAsync(null, cid, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.GetApplicableAsync(null, cid, It.IsAny<CancellationToken>()), Times.Once);
    }
}
