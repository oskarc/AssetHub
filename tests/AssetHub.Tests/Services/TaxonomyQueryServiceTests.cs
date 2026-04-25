using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Moq;

namespace AssetHub.Tests.Services;

public class TaxonomyQueryServiceTests
{
    private readonly Mock<ITaxonomyRepository> _repo = new();
    private TaxonomyQueryService CreateService() => new(_repo.Object);

    [Fact]
    public async Task GetAllAsync_ReturnsSummaryListWithTermCounts()
    {
        var svc = CreateService();
        var taxonomies = new List<Taxonomy>
        {
            TestData.CreateTaxonomy(name: "Colors", terms: new()
            {
                TestData.CreateTaxonomyTerm(label: "Red"),
                TestData.CreateTaxonomyTerm(label: "Blue")
            }),
            TestData.CreateTaxonomy(name: "Shapes")
        };
        _repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(taxonomies);

        var result = await svc.GetAllAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(2, result.Value[0].TermCount);
        Assert.Equal(0, result.Value[1].TermCount);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Taxonomy?)null);

        var result = await svc.GetByIdAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_WithHierarchy_BuildsNestedTree()
    {
        var svc = CreateService();
        var taxonomyId = Guid.NewGuid();
        var europeId = Guid.NewGuid();
        var taxonomy = TestData.CreateTaxonomy(id: taxonomyId, name: "Geography", terms: new()
        {
            TestData.CreateTaxonomyTerm(id: europeId, taxonomyId: taxonomyId, label: "Europe", sortOrder: 0),
            TestData.CreateTaxonomyTerm(taxonomyId: taxonomyId, parentTermId: europeId, label: "Sweden", sortOrder: 0),
            TestData.CreateTaxonomyTerm(taxonomyId: taxonomyId, parentTermId: europeId, label: "Norway", sortOrder: 1),
        });
        _repo.Setup(r => r.GetByIdAsync(taxonomyId, It.IsAny<CancellationToken>())).ReturnsAsync(taxonomy);

        var result = await svc.GetByIdAsync(taxonomyId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var root = Assert.Single(result.Value!.Terms);
        Assert.Equal("Europe", root.Label);
        Assert.NotNull(root.Children);
        Assert.Equal(2, root.Children!.Count);
        Assert.Equal("Sweden", root.Children[0].Label);
    }
}
