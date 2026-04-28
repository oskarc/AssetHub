using AssetHub.Application;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetHub.Tests.Services;

/// <summary>
/// Tests for verifying metadata extraction and persistence work correctly.
/// </summary>
[Collection("Database")]
public class MediaProcessingServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private AssetRepository _repo = null!;

    public MediaProcessingServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _repo = new AssetRepository(_fixture.CreateDbContextProvider(dbName), TestCacheHelper.CreateHybridCache(), NullLogger<AssetRepository>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    /// <summary>
    /// Verifies that MetadataJson with complex data survives database round-trip.
    /// This tests the EF Core JSON column configuration.
    /// </summary>
    [Fact]
    public async Task MetadataJson_PersistsComplexData_AfterDatabaseRoundTrip()
    {
        // Arrange: Create an asset with advanced metadata (like what extraction would produce)
        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            Title = "Test Image With Metadata",
            AssetType = AssetType.Image,
            Status = AssetStatus.Processing,
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            OriginalObjectKey = $"originals/{assetId}.jpg",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = new Dictionary<string, object>
            {
                ["artist"] = "John Photographer",
                ["copyright"] = "© 2026 Test Corp",
                ["cameraMake"] = "Canon",
                ["cameraModel"] = "EOS R5",
                ["dateTaken"] = "2026-01-15 14:30:22",
                ["exposureTime"] = "1/250s",
                ["aperture"] = "f/2.8",
                ["iso"] = 400,
                ["focalLength"] = "85mm",
                ["imageWidth"] = 6720,
                ["imageHeight"] = 4480,
                ["flash"] = "Did not fire",
                ["gpsLatitude"] = 59.3293,
                ["gpsLongitude"] = 18.0686,
                ["keywords"] = "landscape, nature, sweden"
            }
        };

        // Act: Save and reload
        await _repo.CreateAsync(asset, CancellationToken.None);

        // Use a new tracking context to ensure we're reading from DB
        _db.ChangeTracker.Clear();
        var loaded = await _repo.GetByIdAsync(assetId);

        // Assert: All metadata should be preserved
        Assert.NotNull(loaded);
        Assert.True(loaded.MetadataJson.Count >= 14, 
            $"Expected at least 14 metadata fields, got {loaded.MetadataJson.Count}. Fields: {string.Join(", ", loaded.MetadataJson.Keys)}");
        Assert.Equal("John Photographer", loaded.MetadataJson["artist"]?.ToString());
        Assert.Equal("© 2026 Test Corp", loaded.MetadataJson["copyright"]?.ToString());
        Assert.Equal("Canon", loaded.MetadataJson["cameraMake"]?.ToString());
        Assert.Equal("EOS R5", loaded.MetadataJson["cameraModel"]?.ToString());
        Assert.Equal("2026-01-15 14:30:22", loaded.MetadataJson["dateTaken"]?.ToString());
        Assert.Equal("1/250s", loaded.MetadataJson["exposureTime"]?.ToString());
        Assert.Equal("f/2.8", loaded.MetadataJson["aperture"]?.ToString());
        Assert.Equal("85mm", loaded.MetadataJson["focalLength"]?.ToString());
        Assert.Equal("Did not fire", loaded.MetadataJson["flash"]?.ToString());
        Assert.Equal("landscape, nature, sweden", loaded.MetadataJson["keywords"]?.ToString());
        
        // Numeric values may come back as JsonElement - check they convert correctly
        var isoValue = loaded.MetadataJson["iso"];
        Assert.True(int.TryParse(isoValue?.ToString(), out var iso));
        Assert.Equal(400, iso);
    }

    /// <summary>
    /// Verifies that updating an existing asset's MetadataJson works correctly.
    /// This simulates what ProcessImageAsync does after extracting metadata.
    /// </summary>
    [Fact]
    public async Task MetadataJson_CanBeUpdated_AfterInitialSave()
    {
        // Arrange: Create an asset with empty metadata (like after upload init)
        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            Title = "Test Image",
            AssetType = AssetType.Image,
            Status = AssetStatus.Processing,
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            OriginalObjectKey = $"originals/{assetId}.jpg",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = new Dictionary<string, object>() // Empty initially
        };

        await _repo.CreateAsync(asset, CancellationToken.None);
        _db.ChangeTracker.Clear();

        // Act: Load, add metadata (simulating extraction), and save
        var loadedAsset = await _repo.GetByIdAsync(assetId);
        Assert.NotNull(loadedAsset);
        Assert.Empty(loadedAsset.MetadataJson);

        // Simulate metadata extraction adding fields
        loadedAsset.MetadataJson["artist"] = "Jane Doe";
        loadedAsset.MetadataJson["cameraMake"] = "Nikon";
        loadedAsset.MetadataJson["iso"] = 800;
        loadedAsset.Status = AssetStatus.Ready;
        loadedAsset.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(loadedAsset, CancellationToken.None);
        _db.ChangeTracker.Clear();

        // Assert: Reload and verify metadata was persisted
        var reloaded = await _repo.GetByIdAsync(assetId);
        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded.MetadataJson.Count);
        Assert.Equal("Jane Doe", reloaded.MetadataJson["artist"]?.ToString());
        Assert.Equal("Nikon", reloaded.MetadataJson["cameraMake"]?.ToString());
        Assert.Equal(AssetStatus.Ready, reloaded.Status);
    }

    /// <summary>
    /// Verifies empty metadata doesn't cause issues.
    /// </summary>
    [Fact]
    public async Task MetadataJson_EmptyDictionary_PersistsCorrectly()
    {
        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            Title = "No Metadata Image",
            AssetType = AssetType.Image,
            Status = AssetStatus.Ready,
            ContentType = "image/jpeg",
            SizeBytes = 512,
            OriginalObjectKey = $"originals/{assetId}.jpg",
            ThumbObjectKey = $"thumbs/{assetId}.jpg",
            MediumObjectKey = $"medium/{assetId}.jpg",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "test-user",
            UpdatedAt = DateTime.UtcNow,
            MetadataJson = new Dictionary<string, object>()
        };

        await _repo.CreateAsync(asset, CancellationToken.None);
        _db.ChangeTracker.Clear();

        var loaded = await _repo.GetByIdAsync(assetId);
        
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.MetadataJson);
        Assert.Empty(loaded.MetadataJson);
    }
}
