using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class MetadataSchemaServiceTests
{
    private readonly Mock<IMetadataSchemaRepository> _schemaRepo = new();
    private readonly Mock<IAssetMetadataRepository> _valuesRepo = new();
    private readonly Mock<ICollectionRepository> _collectionRepo = new();

    private MetadataSchemaService CreateService(string userId = "admin-001", bool isAdmin = true)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new MetadataSchemaService(
            _schemaRepo.Object,
            _valuesRepo.Object,
            _collectionRepo.Object,
            currentUser,
            NullLogger<MetadataSchemaService>.Instance);
    }

    private static CreateMetadataSchemaDto ValidGlobalDto(string name = "Basic") => new()
    {
        Name = name,
        Scope = "global",
        Fields = new()
        {
            new CreateMetadataFieldDto
            {
                Key = "title",
                Label = "Title",
                Type = "text",
                SortOrder = 0
            }
        }
    };

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.CreateAsync(ValidGlobalDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_UnknownScope_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Scope = "bogus";

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_AssetTypeScopeWithoutAssetType_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Scope = "asset_type";
        dto.AssetType = null;

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_CollectionScopeWithoutCollectionId_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Scope = "collection";
        dto.CollectionId = null;

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_CollectionDoesNotExist_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Scope = "collection";
        dto.CollectionId = Guid.NewGuid();

        _collectionRepo.Setup(r => r.ExistsAsync(dto.CollectionId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("does not exist", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        var svc = CreateService();
        _schemaRepo.Setup(r => r.ExistsByNameAsync("Basic", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await svc.CreateAsync(ValidGlobalDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidAssetType_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Scope = "asset_type";
        dto.AssetType = "audio";

        _schemaRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("Unknown asset type", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateFieldKey_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Fields.Add(new CreateMetadataFieldDto
        {
            Key = "title",   // same as the existing field
            Label = "Different label",
            Type = "text"
        });
        _schemaRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("Duplicate field key", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_TaxonomyFieldWithoutTaxonomyId_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Fields[0].Type = "taxonomy";
        dto.Fields[0].TaxonomyId = null;
        _schemaRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("TaxonomyId is required", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_UnknownFieldType_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Fields[0].Type = "colour";
        _schemaRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("Unknown field type", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_ValidGlobalSchema_ReturnsDto()
    {
        var svc = CreateService("admin-42");
        _schemaRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _schemaRepo.Setup(r => r.CreateAsync(It.IsAny<MetadataSchema>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSchema s, CancellationToken _) => s);

        var result = await svc.CreateAsync(ValidGlobalDto("Basic"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Basic", result.Value!.Name);
        Assert.Equal("global", result.Value.Scope);
        Assert.Equal("admin-42", result.Value.CreatedByUserId);
        Assert.Single(result.Value.Fields);
        Assert.Equal("title", result.Value.Fields[0].Key);
    }

    [Fact]
    public async Task CreateAsync_ValidCollectionScope_VerifiesCollectionExists()
    {
        var svc = CreateService();
        var dto = ValidGlobalDto();
        dto.Scope = "collection";
        dto.CollectionId = Guid.NewGuid();

        _collectionRepo.Setup(r => r.ExistsAsync(dto.CollectionId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _schemaRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _schemaRepo.Setup(r => r.CreateAsync(It.IsAny<MetadataSchema>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSchema s, CancellationToken _) => s);

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _collectionRepo.Verify(r => r.ExistsAsync(dto.CollectionId!.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.UpdateAsync(Guid.NewGuid(), new UpdateMetadataSchemaDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_SchemaNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _schemaRepo.Setup(r => r.GetByIdForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSchema?)null);

        var result = await svc.UpdateAsync(id, new UpdateMetadataSchemaDto { Name = "X" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ReturnsConflict()
    {
        var svc = CreateService();
        var existing = TestData.CreateMetadataSchema(name: "Original");
        _schemaRepo.Setup(r => r.GetByIdForUpdateAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _schemaRepo.Setup(r => r.ExistsByNameAsync("Taken", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await svc.UpdateAsync(existing.Id, new UpdateMetadataSchemaDto { Name = "Taken" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WithFields_IncrementsVersion()
    {
        var svc = CreateService();
        var existing = TestData.CreateMetadataSchema(version: 3,
            fields: new() { TestData.CreateMetadataField(key: "a", type: MetadataFieldType.Text) });

        _schemaRepo.Setup(r => r.GetByIdForUpdateAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _schemaRepo.Setup(r => r.UpdateAsync(It.IsAny<MetadataSchema>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSchema s, CancellationToken _) => s);

        var dto = new UpdateMetadataSchemaDto
        {
            Fields = new()
            {
                new UpdateMetadataFieldDto { Key = "a", Label = "A", Type = "text" },
                new UpdateMetadataFieldDto { Key = "b", Label = "B", Type = "number" }
            }
        };

        var result = await svc.UpdateAsync(existing.Id, dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Version);
        Assert.Equal(2, result.Value.Fields.Count);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.DeleteAsync(Guid.NewGuid(), force: false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_SchemaNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _schemaRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataSchema?)null);

        var result = await svc.DeleteAsync(id, force: false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_HasValuesWithoutForce_ReturnsConflict()
    {
        var svc = CreateService();
        var schema = TestData.CreateMetadataSchema();
        _schemaRepo.Setup(r => r.GetByIdAsync(schema.Id, It.IsAny<CancellationToken>())).ReturnsAsync(schema);
        _schemaRepo.Setup(r => r.HasMetadataValuesAsync(schema.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await svc.DeleteAsync(schema.Id, force: false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
        _schemaRepo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ForceTrue_DeletesValuesAndSchema()
    {
        var svc = CreateService();
        var schema = TestData.CreateMetadataSchema();
        _schemaRepo.Setup(r => r.GetByIdAsync(schema.Id, It.IsAny<CancellationToken>())).ReturnsAsync(schema);

        var result = await svc.DeleteAsync(schema.Id, force: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _valuesRepo.Verify(r => r.DeleteBySchemaIdAsync(schema.Id, It.IsAny<CancellationToken>()), Times.Once);
        _schemaRepo.Verify(r => r.DeleteAsync(schema.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NoValues_DeletesSchemaWithoutTouchingValues()
    {
        var svc = CreateService();
        var schema = TestData.CreateMetadataSchema();
        _schemaRepo.Setup(r => r.GetByIdAsync(schema.Id, It.IsAny<CancellationToken>())).ReturnsAsync(schema);
        _schemaRepo.Setup(r => r.HasMetadataValuesAsync(schema.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await svc.DeleteAsync(schema.Id, force: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _valuesRepo.Verify(r => r.DeleteBySchemaIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _schemaRepo.Verify(r => r.DeleteAsync(schema.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
