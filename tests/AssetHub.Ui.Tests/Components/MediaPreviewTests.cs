using AssetHub.Application;
using AssetHub.Ui.Components;
using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

/// <summary>
/// Smoke tests for the asset-type fork inside <see cref="MediaPreview"/>.
/// T5-AUDIO-01 added the audio branch — without these tests a future refactor
/// of the if-else chain could silently regress audio playback to "download
/// prompt" without anything failing in CI.
/// </summary>
public class MediaPreviewTests : BunitTestBase
{
    [Fact]
    public void Renders_AudioElement_For_Audio_AssetType()
    {
        var cut = Render<MediaPreview>(p => p
            .Add(x => x.AssetType, Constants.AssetTypeFilters.Audio)
            .Add(x => x.ContentType, "audio/mpeg")
            .Add(x => x.Title, "Test track")
            .Add(x => x.PreviewUrl, "/api/v1/assets/abc/preview")
            .Add(x => x.BrowserNoAudioText, "no audio support"));

        var audio = cut.Find("audio");
        Assert.NotNull(audio);
        Assert.True(audio.HasAttribute("controls"));
        Assert.Equal("Test track", audio.GetAttribute("aria-label"));

        var source = cut.Find("audio > source");
        Assert.Equal("/api/v1/assets/abc/preview", source.GetAttribute("src"));
        Assert.Equal("audio/mpeg", source.GetAttribute("type"));

        // Should NOT also render a video element
        Assert.Empty(cut.FindAll("video"));
    }

    [Fact]
    public void Renders_VideoElement_For_Video_AssetType()
    {
        var cut = Render<MediaPreview>(p => p
            .Add(x => x.AssetType, Constants.AssetTypeFilters.Video)
            .Add(x => x.ContentType, "video/mp4")
            .Add(x => x.Title, "clip")
            .Add(x => x.PreviewUrl, "/api/v1/assets/abc/preview")
            .Add(x => x.BrowserNoVideoText, "no video support"));

        Assert.NotNull(cut.Find("video"));
        Assert.Empty(cut.FindAll("audio"));
    }

    [Fact]
    public void Renders_DownloadFallback_For_Document_AssetType_NonPdf()
    {
        var cut = Render<MediaPreview>(p => p
            .Add(x => x.AssetType, Constants.AssetTypeFilters.Document)
            .Add(x => x.ContentType, "application/zip")
            .Add(x => x.Title, "bundle.zip")
            .Add(x => x.PreviewUrl, "/api/v1/assets/abc/preview")
            .Add(x => x.DownloadUrl, "/api/v1/assets/abc/download")
            .Add(x => x.PreviewNotAvailableText, "preview unavailable")
            .Add(x => x.DownloadToViewText, "Download"));

        Assert.Empty(cut.FindAll("audio"));
        Assert.Empty(cut.FindAll("video"));
        Assert.Contains("preview unavailable", cut.Markup);
    }
}
