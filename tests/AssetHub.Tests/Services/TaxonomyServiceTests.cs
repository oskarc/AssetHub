using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class TaxonomyServiceTests
{
    private readonly Mock<ITaxonomyRepository> _repo = new();

    private TaxonomyService CreateService(string userId = "admin-001", bool isAdmin = true)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new TaxonomyService(_repo.Object, currentUser, NullLogger<TaxonomyService>.Instance);
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);
        var dto = new CreateTaxonomyDto { Name = "Colors" };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        var svc = CreateService();
        _repo.Setup(r => r.ExistsByNameAsync("Colors", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await svc.CreateAsync(new CreateTaxonomyDto { Name = "Colors" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_ValidFlatTaxonomy_Succeeds()
    {
        var svc = CreateService("admin-99");
        Taxonomy? captured = null;
        _repo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.CreateAsync(It.IsAny<Taxonomy>(), It.IsAny<CancellationToken>()))
            .Callback<Taxonomy, CancellationToken>((t, _) => captured = t)
            .ReturnsAsync((Taxonomy t, CancellationToken _) => t);

        var dto = new CreateTaxonomyDto
        {
            Name = "Colors",
            Terms = new()
            {
                new CreateTaxonomyTermDto { Label = "Red", SortOrder = 0 },
                new CreateTaxonomyTermDto { Label = "Blue", SortOrder = 1 }
            }
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Colors", result.Value!.Name);
        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Terms.Count);
        Assert.All(captured.Terms, t => Assert.Null(t.ParentTermId));
    }

    [Fact]
    public async Task CreateAsync_HierarchicalTerms_LinksChildrenToParentsWithRealIds()
    {
        // Regression: FlattenTerms used to read term.Id before it was assigned, so every child ended up
        // with ParentTermId = Guid.Empty. The fix sets Id = Guid.NewGuid() in the TaxonomyTerm initializer
        // so children can point to a real id. This test asserts the fix.
        var svc = CreateService();
        Taxonomy? captured = null;
        _repo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.CreateAsync(It.IsAny<Taxonomy>(), It.IsAny<CancellationToken>()))
            .Callback<Taxonomy, CancellationToken>((t, _) => captured = t)
            .ReturnsAsync((Taxonomy t, CancellationToken _) => t);

        var dto = new CreateTaxonomyDto
        {
            Name = "Geography",
            Terms = new()
            {
                new CreateTaxonomyTermDto
                {
                    Label = "Europe",
                    Children = new()
                    {
                        new CreateTaxonomyTermDto { Label = "Sweden" },
                        new CreateTaxonomyTermDto { Label = "Norway" }
                    }
                }
            }
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);

        var europe = captured!.Terms.Single(t => t.Label == "Europe");
        var sweden = captured.Terms.Single(t => t.Label == "Sweden");
        var norway = captured.Terms.Single(t => t.Label == "Norway");

        Assert.NotEqual(Guid.Empty, europe.Id);
        Assert.Null(europe.ParentTermId);
        Assert.Equal(europe.Id, sweden.ParentTermId);
        Assert.Equal(europe.Id, norway.ParentTermId);
    }

    [Fact]
    public async Task CreateAsync_GeneratesSlugFromLabel_WhenSlugMissing()
    {
        var svc = CreateService();
        Taxonomy? captured = null;
        _repo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repo.Setup(r => r.CreateAsync(It.IsAny<Taxonomy>(), It.IsAny<CancellationToken>()))
            .Callback<Taxonomy, CancellationToken>((t, _) => captured = t)
            .ReturnsAsync((Taxonomy t, CancellationToken _) => t);

        var dto = new CreateTaxonomyDto
        {
            Name = "Ämnen",
            Terms = new() { new CreateTaxonomyTermDto { Label = "Hello World" } }
        };

        await svc.CreateAsync(dto, CancellationToken.None);

        Assert.Equal("hello-world", captured!.Terms.Single().Slug);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.UpdateAsync(Guid.NewGuid(), new UpdateTaxonomyDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdForUpdateAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Taxonomy?)null);

        var result = await svc.UpdateAsync(id, new UpdateTaxonomyDto { Name = "X" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ReturnsConflict()
    {
        var svc = CreateService();
        var taxonomy = TestData.CreateTaxonomy(name: "Colors");
        _repo.Setup(r => r.GetByIdForUpdateAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(taxonomy);
        _repo.Setup(r => r.ExistsByNameAsync("Shapes", taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await svc.UpdateAsync(taxonomy.Id, new UpdateTaxonomyDto { Name = "Shapes" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesNameAndDescription()
    {
        var svc = CreateService();
        var taxonomy = TestData.CreateTaxonomy(name: "Colors");
        _repo.Setup(r => r.GetByIdForUpdateAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(taxonomy);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Taxonomy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Taxonomy t, CancellationToken _) => t);

        var result = await svc.UpdateAsync(taxonomy.Id,
            new UpdateTaxonomyDto { Name = "Palette", Description = "Updated" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Palette", result.Value!.Name);
        Assert.Equal("Updated", result.Value.Description);
    }

    // ── ReplaceTermsAsync ───────────────────────────────────────────

    [Fact]
    public async Task ReplaceTermsAsync_HierarchicalStructure_PreservesParentLinks()
    {
        var svc = CreateService();
        var taxonomy = TestData.CreateTaxonomy(name: "Geography");
        _repo.Setup(r => r.GetByIdAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(taxonomy);

        ICollection<TaxonomyTerm>? captured = null;
        _repo.Setup(r => r.ReplaceTermsAsync(taxonomy.Id, It.IsAny<ICollection<TaxonomyTerm>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ICollection<TaxonomyTerm>, CancellationToken>((_, terms, _) => captured = terms)
            .Returns(Task.CompletedTask);

        var terms = new List<UpsertTaxonomyTermDto>
        {
            new()
            {
                Label = "Europe",
                Children = new()
                {
                    new UpsertTaxonomyTermDto { Label = "Sweden" }
                }
            }
        };

        var result = await svc.ReplaceTermsAsync(taxonomy.Id, terms, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        var europe = captured!.Single(t => t.Label == "Europe");
        var sweden = captured.Single(t => t.Label == "Sweden");
        Assert.NotEqual(Guid.Empty, europe.Id);
        Assert.Equal(europe.Id, sweden.ParentTermId);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Taxonomy?)null);

        var result = await svc.DeleteAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_ReferencedByFields_ReturnsConflict()
    {
        var svc = CreateService();
        var taxonomy = TestData.CreateTaxonomy();
        _repo.Setup(r => r.GetByIdAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(taxonomy);
        _repo.Setup(r => r.IsReferencedByFieldsAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await svc.DeleteAsync(taxonomy.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_Valid_Succeeds()
    {
        var svc = CreateService();
        var taxonomy = TestData.CreateTaxonomy();
        _repo.Setup(r => r.GetByIdAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(taxonomy);
        _repo.Setup(r => r.IsReferencedByFieldsAsync(taxonomy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await svc.DeleteAsync(taxonomy.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.DeleteAsync(taxonomy.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
