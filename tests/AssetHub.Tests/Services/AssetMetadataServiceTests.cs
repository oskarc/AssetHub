using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class AssetMetadataServiceTests
{
    private readonly Mock<IAssetMetadataRepository> _valuesRepo = new();
    private readonly Mock<IMetadataSchemaRepository> _schemaRepo = new();
    private readonly Mock<ITaxonomyRepository> _taxonomyRepo = new();
    private readonly Mock<IAssetRepository> _assetRepo = new();
    private readonly Mock<IAssetCollectionRepository> _assetCollectionRepo = new();
    private readonly Mock<ICollectionAuthorizationService> _authService = new();

    private AssetMetadataService CreateService(string userId = "user-001", bool isAdmin = false)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new AssetMetadataService(
            _valuesRepo.Object,
            _schemaRepo.Object,
            _taxonomyRepo.Object,
            _assetRepo.Object,
            _assetCollectionRepo.Object,
            _authService.Object,
            currentUser,
            NullLogger<AssetMetadataService>.Instance);
    }

    /// <summary>
    /// Common setup: asset exists, belongs to a collection the user can write to, with a single
    /// global text field so callers can focus on the validation path they're testing.
    /// Returns the asset + the field so tests can reference the field id.
    /// </summary>
    private (Asset Asset, MetadataField Field) SetupHappyPath(
        string userId = "user-001",
        MetadataFieldType type = MetadataFieldType.Text,
        bool required = false,
        Action<MetadataField>? customizeField = null)
    {
        var asset = TestData.CreateAsset();
        var collectionId = Guid.NewGuid();
        var field = TestData.CreateMetadataField(key: "notes", type: type, required: required);
        customizeField?.Invoke(field);
        var schema = TestData.CreateMetadataSchema(fields: new() { field });

        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { collectionId });
        _authService.Setup(a => a.FilterAccessibleAsync(userId, It.IsAny<IEnumerable<Guid>>(), RoleHierarchy.Roles.Contributor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { collectionId });
        _schemaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema> { schema });
        _valuesRepo.Setup(r => r.GetByAssetIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetMetadataValue>());

        return (asset, field);
    }

    // ── GetByAssetIdAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetByAssetIdAsync_AssetNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);

        var result = await svc.GetByAssetIdAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetByAssetIdAsync_Valid_ReturnsValues()
    {
        var svc = CreateService();
        var asset = TestData.CreateAsset();
        var field = TestData.CreateMetadataField(key: "notes", label: "Notes");
        var value = TestData.CreateAssetMetadataValue(assetId: asset.Id, fieldId: field.Id, valueText: "hello");
        value.MetadataField = field;

        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _valuesRepo.Setup(r => r.GetByAssetIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetMetadataValue> { value });

        var result = await svc.GetByAssetIdAsync(asset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value!);
        Assert.Equal("notes", dto.FieldKey);
        Assert.Equal("hello", dto.ValueText);
    }

    // ── SetAsync: authorization ────────────────────────────────────

    [Fact]
    public async Task SetAsync_AssetNotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);

        var result = await svc.SetAsync(id, new SetAssetMetadataDto { Values = new() { NewValue(Guid.NewGuid()) } }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetAsync_AssetInNoCollection_ReturnsForbiddenForNonAdmin()
    {
        var svc = CreateService(userId: "user-X", isAdmin: false);
        var asset = TestData.CreateAsset();
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await svc.SetAsync(asset.Id, new SetAssetMetadataDto { Values = new() { NewValue(Guid.NewGuid()) } }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetAsync_UserLacksContributor_ReturnsForbidden()
    {
        var svc = CreateService(userId: "user-X");
        var asset = TestData.CreateAsset();
        var cid = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
        _authService.Setup(a => a.FilterAccessibleAsync("user-X", It.IsAny<IEnumerable<Guid>>(), RoleHierarchy.Roles.Contributor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        var result = await svc.SetAsync(asset.Id, new SetAssetMetadataDto { Values = new() { NewValue(Guid.NewGuid()) } }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task SetAsync_SystemAdmin_BypassesCollectionAuthCheck()
    {
        var svc = CreateService(userId: "admin-1", isAdmin: true);
        var asset = TestData.CreateAsset();
        var field = TestData.CreateMetadataField(key: "notes");
        var schema = TestData.CreateMetadataSchema(fields: new() { field });

        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _schemaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema> { schema });
        _valuesRepo.Setup(r => r.GetByAssetIdAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AssetMetadataValue>());

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "hello") } },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _authService.Verify(a => a.FilterAccessibleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── SetAsync: value-level validation ───────────────────────────

    [Fact]
    public async Task SetAsync_DuplicateFieldId_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath();

        var dto = new SetAssetMetadataDto
        {
            Values = new() { NewValue(field.Id, text: "a"), NewValue(field.Id, text: "b") }
        };

        var result = await svc.SetAsync(asset.Id, dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("more than once", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_FieldDoesNotApplyToAsset_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, _) = SetupHappyPath();
        var unknownFieldId = Guid.NewGuid();

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(unknownFieldId, text: "x") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("does not apply", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_TextExceedsMaxLength_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(customizeField: f => f.MaxLength = 5);

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "too long") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("exceeds max length", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_TextPatternMismatch_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(customizeField: f => f.PatternRegex = "^[0-9]+$");

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "abc") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("pattern", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_UrlFieldWithInvalidUrl_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Url);

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "not a url") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("URL", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_SelectValueNotInOptions_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Select,
            customizeField: f => f.SelectOptions = new() { "small", "medium", "large" });

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "huge") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("not a valid option", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_NumberBelowMin_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Number,
            customizeField: f => f.NumericMin = 10);

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, numeric: 5) } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains(">=", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_NumberAboveMax_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Number,
            customizeField: f => f.NumericMax = 10);

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, numeric: 99) } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("<=", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_BooleanFieldInvalidText_ReturnsBadRequest()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Boolean);

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "maybe") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("'true' or 'false'", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_RequiredFieldWithEmptyValues_ReturnsBadRequest()
    {
        var svc = CreateService();
        // Two fields: one required, one optional. Supply only the optional one (with a value)
        // so we hit the "missing required" branch, not "does not apply".
        var required = TestData.CreateMetadataField(key: "required", required: true);
        var optional = TestData.CreateMetadataField(key: "optional");
        var schema = TestData.CreateMetadataSchema(fields: new() { required, optional });
        var asset = TestData.CreateAsset();
        var cid = Guid.NewGuid();

        _assetRepo.Setup(r => r.GetByIdAsync(asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(asset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
        _authService.Setup(a => a.FilterAccessibleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>(), RoleHierarchy.Roles.Contributor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { cid });
        _schemaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema> { schema });

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(optional.Id, text: "only-optional") } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("Required field 'required' is missing", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_TaxonomyTermDoesNotExist_ReturnsBadRequest()
    {
        var svc = CreateService();
        var taxonomyId = Guid.NewGuid();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Taxonomy,
            customizeField: f => f.TaxonomyId = taxonomyId);

        var missingTermId = Guid.NewGuid();
        _taxonomyRepo.Setup(r => r.GetTermsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaxonomyTerm>());

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, termId: missingTermId) } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("does not exist", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_TaxonomyTermFromWrongTaxonomy_ReturnsBadRequest()
    {
        var svc = CreateService();
        var expectedTaxonomy = Guid.NewGuid();
        var otherTaxonomy = Guid.NewGuid();
        var (asset, field) = SetupHappyPath(type: MetadataFieldType.Taxonomy,
            customizeField: f => f.TaxonomyId = expectedTaxonomy);

        var term = TestData.CreateTaxonomyTerm(taxonomyId: otherTaxonomy, label: "stranger");
        _taxonomyRepo.Setup(r => r.GetTermsByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaxonomyTerm> { term });

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, termId: term.Id) } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("does not belong to taxonomy", result.Error.Message);
    }

    [Fact]
    public async Task SetAsync_ValidValues_ReplacesAndReturnsValues()
    {
        var svc = CreateService();
        var (asset, field) = SetupHappyPath();

        var result = await svc.SetAsync(asset.Id,
            new SetAssetMetadataDto { Values = new() { NewValue(field.Id, text: "hello") } },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _valuesRepo.Verify(r => r.ReplaceForAssetAsync(
            asset.Id,
            It.Is<List<AssetMetadataValue>>(vs => vs.Count == 1 && vs[0].ValueText == "hello"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── BulkSetAsync ────────────────────────────────────────────────

    [Fact]
    public async Task BulkSetAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.BulkSetAsync(
            new BulkSetAssetMetadataDto { Assets = new() { new BulkAssetMetadataEntry { AssetId = Guid.NewGuid(), Values = new() { NewValue(Guid.NewGuid()) } } } },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task BulkSetAsync_AssetNotFound_ReturnsNotFoundWithoutWriting()
    {
        var svc = CreateService(isAdmin: true);
        var missingId = Guid.NewGuid();
        _assetRepo.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((Asset?)null);
        _schemaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema>());

        var dto = new BulkSetAssetMetadataDto
        {
            Assets = new() { new BulkAssetMetadataEntry { AssetId = missingId, Values = new() { NewValue(Guid.NewGuid()) } } }
        };

        var result = await svc.BulkSetAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
        _valuesRepo.Verify(r => r.ReplaceForAssetsAsync(It.IsAny<IEnumerable<(Guid, List<AssetMetadataValue>)>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BulkSetAsync_AllValid_CallsReplaceForAssetsAsyncAtomically()
    {
        var svc = CreateService(isAdmin: true);
        var field = TestData.CreateMetadataField(key: "notes");
        var schema = TestData.CreateMetadataSchema(fields: new() { field });
        var a1 = TestData.CreateAsset();
        var a2 = TestData.CreateAsset();

        _assetRepo.Setup(r => r.GetByIdAsync(a1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a1);
        _assetRepo.Setup(r => r.GetByIdAsync(a2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a2);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _schemaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema> { schema });

        var dto = new BulkSetAssetMetadataDto
        {
            Assets = new()
            {
                new BulkAssetMetadataEntry { AssetId = a1.Id, Values = new() { NewValue(field.Id, text: "one") } },
                new BulkAssetMetadataEntry { AssetId = a2.Id, Values = new() { NewValue(field.Id, text: "two") } }
            }
        };

        var result = await svc.BulkSetAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _valuesRepo.Verify(r => r.ReplaceForAssetsAsync(
            It.Is<IEnumerable<(Guid AssetId, List<AssetMetadataValue> Values)>>(b => b.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BulkSetAsync_OneEntryInvalid_ReturnsBadRequestWithoutWriting()
    {
        var svc = CreateService(isAdmin: true);
        var field = TestData.CreateMetadataField(key: "count", type: MetadataFieldType.Number, numericMin: 0, numericMax: 10);
        var schema = TestData.CreateMetadataSchema(fields: new() { field });
        var a1 = TestData.CreateAsset();
        var a2 = TestData.CreateAsset();

        _assetRepo.Setup(r => r.GetByIdAsync(a1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a1);
        _assetRepo.Setup(r => r.GetByIdAsync(a2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a2);
        _assetCollectionRepo.Setup(r => r.GetCollectionIdsForAssetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _schemaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetadataSchema> { schema });

        var dto = new BulkSetAssetMetadataDto
        {
            Assets = new()
            {
                new BulkAssetMetadataEntry { AssetId = a1.Id, Values = new() { NewValue(field.Id, numeric: 5) } },
                // a2 supplies a number above the configured max → invalid
                new BulkAssetMetadataEntry { AssetId = a2.Id, Values = new() { NewValue(field.Id, numeric: 999) } }
            }
        };

        var result = await svc.BulkSetAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _valuesRepo.Verify(r => r.ReplaceForAssetsAsync(It.IsAny<IEnumerable<(Guid, List<AssetMetadataValue>)>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static SetMetadataValueDto NewValue(Guid fieldId, string? text = null, decimal? numeric = null, DateTime? date = null, Guid? termId = null)
        => new()
        {
            MetadataFieldId = fieldId,
            ValueText = text,
            ValueNumeric = numeric,
            ValueDate = date,
            ValueTaxonomyTermId = termId
        };
}
