using AssetHub.Api.Handlers;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Handlers;

/// <summary>
/// Verifies that audio metadata flows from the Wolverine completion event onto
/// the persisted Asset row. Without this test a future refactor of the audio
/// fork could silently drop the new fields and the integration test (which
/// requires ffmpeg in the box) wouldn't catch it on every CI run.
/// </summary>
public class AssetProcessingCompletedHandlerTests
{
    [Fact]
    public async Task HandleAsync_AudioEvent_PopulatesAudioFieldsAndMarksReady()
    {
        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            Title = "track.mp3",
            AssetType = AssetType.Audio,
            Status = AssetStatus.Processing,
            ContentType = "audio/mpeg",
            SizeBytes = 1024,
            OriginalObjectKey = $"originals/{assetId}",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "u",
            UpdatedAt = DateTime.UtcNow
        };

        var repo = new Mock<IAssetRepository>();
        repo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Asset>(), It.IsAny<CancellationToken>()))
            .Returns<Asset, CancellationToken>((a, _) => Task.FromResult(a));

        var webhooks = new Mock<IWebhookEventPublisher>();
        webhooks.Setup(w => w.PublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new AssetProcessingCompletedHandler(
            repo.Object, webhooks.Object,
            NullLogger<AssetProcessingCompletedHandler>.Instance);

        var evt = new AssetProcessingCompletedEvent
        {
            AssetId = assetId,
            DurationSeconds = 183,
            AudioBitrateKbps = 192,
            AudioSampleRateHz = 44100,
            AudioChannels = 2,
            WaveformPeaksPath = $"peaks/{assetId}.json"
        };

        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(AssetStatus.Ready, asset.Status);
        Assert.Equal(183, asset.DurationSeconds);
        Assert.Equal(192, asset.AudioBitrateKbps);
        Assert.Equal(44100, asset.AudioSampleRateHz);
        Assert.Equal(2, asset.AudioChannels);
        Assert.Equal($"peaks/{assetId}.json", asset.WaveformPeaksPath);
        repo.Verify(r => r.UpdateAsync(asset, It.IsAny<CancellationToken>()), Times.Once);
        webhooks.Verify(w => w.PublishAsync("asset.created", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ImageEventWithNoAudioFields_LeavesAudioFieldsNull()
    {
        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            Title = "photo.jpg",
            AssetType = AssetType.Image,
            Status = AssetStatus.Processing,
            ContentType = "image/jpeg",
            SizeBytes = 1024,
            OriginalObjectKey = $"originals/{assetId}",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "u",
            UpdatedAt = DateTime.UtcNow
        };

        var repo = new Mock<IAssetRepository>();
        repo.Setup(r => r.GetByIdAsync(assetId, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var webhooks = new Mock<IWebhookEventPublisher>();
        var handler = new AssetProcessingCompletedHandler(
            repo.Object, webhooks.Object,
            NullLogger<AssetProcessingCompletedHandler>.Instance);

        var evt = new AssetProcessingCompletedEvent
        {
            AssetId = assetId,
            ThumbObjectKey = $"thumbs/{assetId}.jpg",
            MediumObjectKey = $"medium/{assetId}.jpg"
        };

        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(AssetStatus.Ready, asset.Status);
        Assert.Null(asset.DurationSeconds);
        Assert.Null(asset.AudioBitrateKbps);
        Assert.Null(asset.WaveformPeaksPath);
        Assert.Equal($"thumbs/{assetId}.jpg", asset.ThumbObjectKey);
    }
}
