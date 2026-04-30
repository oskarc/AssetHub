using AssetHub.Application.Helpers;
using AssetHub.Domain.Entities;

namespace AssetHub.Tests.Domain;

/// <summary>
/// Round-trip coverage for <see cref="AssetType.Audio"/> — the enum value, its
/// db-string mapping, and the helper that classifies uploads. T5-AUDIO-01 added
/// the audio fork; without these tests a future enum reorder could silently
/// break the migration / dispatch / classification chain.
/// </summary>
public class AssetTypeAudioMappingTests
{
    [Fact]
    public void ToDbString_Audio_ReturnsLowercaseAudio()
    {
        Assert.Equal("audio", AssetType.Audio.ToDbString());
    }

    [Fact]
    public void ToAssetType_AudioString_ReturnsAudioEnum()
    {
        Assert.Equal(AssetType.Audio, "audio".ToAssetType());
    }

    [Fact]
    public void RoundTrip_AudioSurvivesToStringAndBack()
    {
        Assert.Equal(AssetType.Audio, AssetType.Audio.ToDbString().ToAssetType());
    }

    [Theory]
    [InlineData("audio/mpeg", null, AssetType.Audio)]
    [InlineData("audio/wav", null, AssetType.Audio)]
    [InlineData("audio/flac", null, AssetType.Audio)]
    [InlineData("audio/x-m4a", null, AssetType.Audio)]
    [InlineData(null, ".mp3", AssetType.Audio)]
    [InlineData(null, ".wav", AssetType.Audio)]
    [InlineData(null, ".flac", AssetType.Audio)]
    [InlineData(null, ".m4a", AssetType.Audio)]
    [InlineData(null, ".ogg", AssetType.Audio)]
    [InlineData(null, ".opus", AssetType.Audio)]
    public void DetermineAssetType_RecognisesAudioByContentTypeOrExtension(string? contentType, string? extension, AssetType expected)
    {
        Assert.Equal(expected, AssetTypeHelper.DetermineAssetType(contentType, extension));
    }

    [Fact]
    public void IsValidContentType_AudioWithAudioContentType_ReturnsTrue()
    {
        var asset = new Asset { AssetType = AssetType.Audio, ContentType = "audio/mpeg", OriginalObjectKey = "x", CreatedByUserId = "u" };
        Assert.True(asset.IsValidContentType());
    }

    [Fact]
    public void IsValidContentType_AudioWithImageContentType_ReturnsFalse()
    {
        var asset = new Asset { AssetType = AssetType.Audio, ContentType = "image/jpeg", OriginalObjectKey = "x", CreatedByUserId = "u" };
        Assert.False(asset.IsValidContentType());
    }
}
