using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Helpers;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for ExportPresetQueryService (read operations).
/// </summary>
public class ExportPresetQueryServiceTests
{
    private readonly Mock<IExportPresetRepository> _repoMock = new();

    private ExportPresetQueryService CreateService()
    {
        return new ExportPresetQueryService(_repoMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllPresets()
    {
        var presets = new List<ExportPreset>
        {
            TestData.CreateExportPreset(name: "Small"),
            TestData.CreateExportPreset(name: "Large")
        };
        _repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(presets);

        var svc = CreateService();
        var result = await svc.GetAllAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsPreset()
    {
        var preset = TestData.CreateExportPreset();
        _repoMock.Setup(r => r.GetByIdAsync(preset.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preset);

        var svc = CreateService();
        var result = await svc.GetByIdAsync(preset.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(preset.Name, result.Value!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExportPreset?)null);

        var svc = CreateService();
        var result = await svc.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }
}
