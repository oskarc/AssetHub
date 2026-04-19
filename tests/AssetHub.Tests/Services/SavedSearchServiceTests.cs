using System.Text.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class SavedSearchServiceTests
{
    private readonly Mock<ISavedSearchRepository> _repo = new();

    private SavedSearchService CreateService(string userId = "user-001")
    {
        var currentUser = new CurrentUser(userId, isSystemAdmin: false);
        return new SavedSearchService(_repo.Object, currentUser, NullLogger<SavedSearchService>.Instance);
    }

    private SavedSearchService CreateAnonymous()
    {
        return new SavedSearchService(_repo.Object, CurrentUser.Anonymous, NullLogger<SavedSearchService>.Instance);
    }

    private static SavedSearch MakeSaved(string ownerUserId, string name = "Mine", AssetSearchRequest? request = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = name,
            RequestJson = JsonSerializer.Serialize(request ?? new AssetSearchRequest()),
            Notify = SavedSearchNotifyCadence.None,
            CreatedAt = DateTime.UtcNow
        };

    // ── GetMineAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetMineAsync_Anonymous_ReturnsForbidden()
    {
        var svc = CreateAnonymous();

        var result = await svc.GetMineAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetMineAsync_ReturnsOwnerScopedList()
    {
        var svc = CreateService("alice");
        _repo.Setup(r => r.GetByOwnerAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SavedSearch> { MakeSaved("alice", "One"), MakeSaved("alice", "Two") });

        var result = await svc.GetMineAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    // ── GetByIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_NotOwnedByCaller_ReturnsNotFound()
    {
        // Repo already scopes by owner, so a not-owned id resolves to null.
        var svc = CreateService("alice");
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedSearch?)null);

        var result = await svc.GetByIdAsync(id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task GetByIdAsync_Owned_ReturnsDtoWithDeserializedRequest()
    {
        var svc = CreateService("alice");
        var saved = MakeSaved("alice", request: new AssetSearchRequest { Text = "hello", Sort = "relevance" });
        _repo.Setup(r => r.GetByIdAsync(saved.Id, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(saved);

        var result = await svc.GetByIdAsync(saved.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value!.Request.Text);
        Assert.Equal("relevance", result.Value.Request.Sort);
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Anonymous_ReturnsForbidden()
    {
        var svc = CreateAnonymous();
        var dto = new CreateSavedSearchDto { Name = "X", Request = new AssetSearchRequest(), Notify = "none" };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidNotifyCadence_ReturnsBadRequest()
    {
        var svc = CreateService();
        var dto = new CreateSavedSearchDto { Name = "X", Request = new AssetSearchRequest(), Notify = "hourly" };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Contains("Unknown notify cadence", result.Error.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameForSameOwner_ReturnsConflict()
    {
        var svc = CreateService("alice");
        _repo.Setup(r => r.ExistsByNameAsync("alice", "Brand photos", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await svc.CreateAsync(
            new CreateSavedSearchDto { Name = "Brand photos", Request = new AssetSearchRequest(), Notify = "none" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_ValidDto_SerializesRequestAsJsonAndReturnsDto()
    {
        var svc = CreateService("alice");
        SavedSearch? captured = null;
        _repo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.CreateAsync(It.IsAny<SavedSearch>(), It.IsAny<CancellationToken>()))
            .Callback<SavedSearch, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync((SavedSearch s, CancellationToken _) => s);

        var request = new AssetSearchRequest { Text = "marketing", AssetTypes = new() { "image" } };
        var result = await svc.CreateAsync(
            new CreateSavedSearchDto { Name = "Marketing images", Request = request, Notify = "daily" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", captured!.OwnerUserId);
        Assert.Equal(SavedSearchNotifyCadence.Daily, captured.Notify);
        Assert.Contains("\"marketing\"", captured.RequestJson);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NotOwned_ReturnsNotFound()
    {
        var svc = CreateService("alice");
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, "alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedSearch?)null);

        var result = await svc.UpdateAsync(id, new UpdateSavedSearchDto { Name = "New" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ReturnsConflict()
    {
        var svc = CreateService("alice");
        var existing = MakeSaved("alice", "Old");
        _repo.Setup(r => r.GetByIdAsync(existing.Id, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repo.Setup(r => r.ExistsByNameAsync("alice", "Taken", existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await svc.UpdateAsync(existing.Id, new UpdateSavedSearchDto { Name = "Taken" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_InvalidNotify_ReturnsBadRequest()
    {
        var svc = CreateService("alice");
        var existing = MakeSaved("alice");
        _repo.Setup(r => r.GetByIdAsync(existing.Id, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await svc.UpdateAsync(existing.Id, new UpdateSavedSearchDto { Notify = "eternity" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_Valid_UpdatesAndReturnsDto()
    {
        var svc = CreateService("alice");
        var existing = MakeSaved("alice", "Old");
        _repo.Setup(r => r.GetByIdAsync(existing.Id, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<SavedSearch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedSearch s, CancellationToken _) => s);

        var result = await svc.UpdateAsync(existing.Id,
            new UpdateSavedSearchDto { Name = "Renamed", Notify = "weekly" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", result.Value!.Name);
        Assert.Equal("weekly", result.Value.Notify);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Anonymous_ReturnsForbidden()
    {
        var svc = CreateAnonymous();

        var result = await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_ScopesToOwnerInRepoCall()
    {
        var svc = CreateService("alice");
        var id = Guid.NewGuid();

        var result = await svc.DeleteAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.DeleteAsync(id, "alice", It.IsAny<CancellationToken>()), Times.Once);
    }
}
