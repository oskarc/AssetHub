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

/// <summary>
/// Unit tests for ExportPresetService (command operations).
/// </summary>
public class ExportPresetServiceTests
{
    private readonly Mock<IExportPresetRepository> _repoMock = new();
    private readonly Mock<IAuditService> _auditMock = new();

    private ExportPresetService CreateService(string userId = "admin-001", bool isAdmin = true)
    {
        var currentUser = new CurrentUser(userId, isAdmin);
        return new ExportPresetService(
            _repoMock.Object,
            currentUser,
            _auditMock.Object,
            NullLogger<ExportPresetService>.Instance);
    }

    // ── CreateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidDto_ReturnsPresetDto()
    {
        var svc = CreateService();
        _repoMock.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<ExportPreset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportPreset p, CancellationToken _) => p);

        var dto = new CreateExportPresetDto
        {
            Name = "Web Large",
            FitMode = "contain",
            Format = "jpeg",
            Quality = 80,
            Width = 1920,
            Height = 1080
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Web Large", result.Value!.Name);
        Assert.Equal("contain", result.Value.FitMode);
        Assert.Equal("jpeg", result.Value.Format);
        Assert.Equal(80, result.Value.Quality);
    }

    [Fact]
    public async Task CreateAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var dto = new CreateExportPresetDto
        {
            Name = "Web Large",
            FitMode = "contain",
            Format = "jpeg"
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        var svc = CreateService();
        _repoMock.Setup(r => r.ExistsByNameAsync("Web Large", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var dto = new CreateExportPresetDto
        {
            Name = "Web Large",
            FitMode = "contain",
            Format = "jpeg"
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidFitMode_ReturnsBadRequest()
    {
        var svc = CreateService();

        var dto = new CreateExportPresetDto
        {
            Name = "Test",
            FitMode = "invalid",
            Format = "jpeg"
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidFormat_ReturnsBadRequest()
    {
        var svc = CreateService();

        var dto = new CreateExportPresetDto
        {
            Name = "Test",
            FitMode = "contain",
            Format = "bmp"
        };

        var result = await svc.CreateAsync(dto, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    // ── UpdateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ValidDto_ReturnsUpdatedPreset()
    {
        var preset = TestData.CreateExportPreset();
        var svc = CreateService();
        _repoMock.Setup(r => r.GetByIdForUpdateAsync(preset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preset);
        _repoMock.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), preset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<ExportPreset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportPreset p, CancellationToken _) => p);

        var dto = new UpdateExportPresetDto { Name = "Updated Name" };

        var result = await svc.UpdateAsync(preset.Id, dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Name", result.Value!.Name);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportPreset?)null);

        var result = await svc.UpdateAsync(Guid.NewGuid(), new UpdateExportPresetDto(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.UpdateAsync(Guid.NewGuid(), new UpdateExportPresetDto { Name = "X" }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingPreset_ReturnsSuccess()
    {
        var preset = TestData.CreateExportPreset();
        var svc = CreateService();
        _repoMock.Setup(r => r.GetByIdAsync(preset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preset);

        var result = await svc.DeleteAsync(preset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repoMock.Verify(r => r.DeleteAsync(preset.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsNotFound()
    {
        var svc = CreateService();
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportPreset?)null);

        var result = await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_NotAdmin_ReturnsForbidden()
    {
        var svc = CreateService(isAdmin: false);

        var result = await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    // ── Audit logging ───────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Success_LogsAuditEvent()
    {
        var svc = CreateService();
        _repoMock.Setup(r => r.ExistsByNameAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<ExportPreset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportPreset p, CancellationToken _) => p);

        var dto = new CreateExportPresetDto
        {
            Name = "Test",
            FitMode = "contain",
            Format = "jpeg"
        };

        await svc.CreateAsync(dto, CancellationToken.None);

        _auditMock.Verify(a => a.LogAsync(
            "exportpreset.created", "exportpreset", It.IsAny<Guid>(),
            "admin-001", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
