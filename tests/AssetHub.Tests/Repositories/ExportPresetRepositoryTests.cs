using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Repositories;

/// <summary>
/// Integration tests for ExportPresetRepository against a real PostgreSQL database.
/// </summary>
[Collection("Database")]
public class ExportPresetRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private ExportPresetRepository _repo = null!;

    public ExportPresetRepositoryTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new ExportPresetRepository(_db, TestCacheHelper.CreateHybridCache(), NullLogger<ExportPresetRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task CreateAsync_PersistsPreset()
    {
        var preset = TestData.CreateExportPreset();

        var created = await _repo.CreateAsync(preset);

        var fetched = await _repo.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(preset.Name, fetched.Name);
        Assert.Equal(preset.FitMode, fetched.FitMode);
        Assert.Equal(preset.Format, fetched.Format);
        Assert.Equal(preset.Quality, fetched.Quality);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrderedByName()
    {
        await _repo.CreateAsync(TestData.CreateExportPreset(name: "Zebra"));
        await _repo.CreateAsync(TestData.CreateExportPreset(name: "Alpha"));
        await _repo.CreateAsync(TestData.CreateExportPreset(name: "Middle"));

        var all = await _repo.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.Equal("Alpha", all[0].Name);
        Assert.Equal("Middle", all[1].Name);
        Assert.Equal("Zebra", all[2].Name);
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsOnlyMatching()
    {
        var p1 = await _repo.CreateAsync(TestData.CreateExportPreset(name: "One"));
        var p2 = await _repo.CreateAsync(TestData.CreateExportPreset(name: "Two"));
        await _repo.CreateAsync(TestData.CreateExportPreset(name: "Three"));

        var result = await _repo.GetByIdsAsync(new[] { p1.Id, p2.Id });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ExistsByNameAsync_ReturnsTrueForDuplicate()
    {
        await _repo.CreateAsync(TestData.CreateExportPreset(name: "Unique Name"));

        var exists = await _repo.ExistsByNameAsync("Unique Name");

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsByNameAsync_ExcludesById()
    {
        var preset = await _repo.CreateAsync(TestData.CreateExportPreset(name: "Self"));

        var exists = await _repo.ExistsByNameAsync("Self", excludeId: preset.Id);

        Assert.False(exists);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var preset = await _repo.CreateAsync(TestData.CreateExportPreset(name: "Before"));
        preset.Name = "After";
        preset.Quality = 95;

        await _repo.UpdateAsync(preset);

        var fetched = await _repo.GetByIdAsync(preset.Id);
        Assert.NotNull(fetched);
        Assert.Equal("After", fetched.Name);
        Assert.Equal(95, fetched.Quality);
    }

    [Fact]
    public async Task DeleteAsync_RemovesPreset()
    {
        var preset = await _repo.CreateAsync(TestData.CreateExportPreset());

        await _repo.DeleteAsync(preset.Id);

        var fetched = await _repo.GetByIdAsync(preset.Id);
        Assert.Null(fetched);
    }
}
