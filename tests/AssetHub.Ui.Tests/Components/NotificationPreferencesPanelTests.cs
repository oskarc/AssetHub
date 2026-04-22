using AssetHub.Ui.Tests.Helpers;

namespace AssetHub.Ui.Tests.Components;

public class NotificationPreferencesPanelTests : BunitTestBase
{
    public NotificationPreferencesPanelTests()
    {
        Services.AddSingleton<IStringLocalizer<NotificationsResource>>(new StubStringLocalizer<NotificationsResource>());
    }

    private static NotificationPreferencesDto SamplePrefs() => new()
    {
        Categories = new Dictionary<string, NotificationCategoryPrefsDto>
        {
            [NotificationConstants.Categories.Mention] = new()
            {
                InApp = true, Email = true, EmailCadence = "instant"
            },
            [NotificationConstants.Categories.SavedSearchDigest] = new()
            {
                InApp = true, Email = true, EmailCadence = "daily"
            },
            [NotificationConstants.Categories.MigrationCompleted] = new()
            {
                InApp = true, Email = false, EmailCadence = "instant"
            },
            [NotificationConstants.Categories.WorkflowTransition] = new()
            {
                InApp = true, Email = true, EmailCadence = "instant"
            },
            [NotificationConstants.Categories.WebhookFailure] = new()
            {
                InApp = true, Email = true, EmailCadence = "instant"
            }
        }
    };

    [Fact]
    public void Shows_LoadingIndicator_Initially()
    {
        MockApi.Setup(a => a.GetNotificationPreferencesAsync(It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<NotificationPreferencesDto>().Task);

        var cut = Render<NotificationPreferencesPanel>();

        Assert.Contains("mud-progress-circular", cut.Markup);
    }

    [Fact]
    public void RendersRowForEachKnownCategory()
    {
        MockApi.Setup(a => a.GetNotificationPreferencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePrefs());

        var cut = Render<NotificationPreferencesPanel>();

        foreach (var category in NotificationConstants.Categories.All)
            Assert.Contains($"Category_{category}", cut.Markup);
    }

    [Fact]
    public void RendersSectionTitleAndDescription()
    {
        MockApi.Setup(a => a.GetNotificationPreferencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SamplePrefs());

        var cut = Render<NotificationPreferencesPanel>();

        Assert.Contains("Prefs_SectionTitle", cut.Markup);
        Assert.Contains("Prefs_SectionDescription", cut.Markup);
        Assert.Contains("Prefs_Btn_Save", cut.Markup);
    }

    [Fact]
    public async Task Save_PersistsViaApiAndShowsSuccess()
    {
        var prefs = SamplePrefs();
        MockApi.Setup(a => a.GetNotificationPreferencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);
        MockApi.Setup(a => a.UpdateNotificationPreferencesAsync(
                It.IsAny<UpdateNotificationPreferencesDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);

        var cut = Render<NotificationPreferencesPanel>();

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Prefs_Btn_Save"));
        await cut.InvokeAsync(() => saveButton.Click());

        MockApi.Verify(a => a.UpdateNotificationPreferencesAsync(
            It.Is<UpdateNotificationPreferencesDto>(d => d.Categories.Count == NotificationConstants.Categories.All.Count),
            It.IsAny<CancellationToken>()), Times.Once);
        VerifySuccessShown("Prefs_Saved");
    }

    [Fact]
    public async Task Save_ApiError_ShowsHandleError()
    {
        var prefs = SamplePrefs();
        MockApi.Setup(a => a.GetNotificationPreferencesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);
        MockApi.Setup(a => a.UpdateNotificationPreferencesAsync(
                It.IsAny<UpdateNotificationPreferencesDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var cut = Render<NotificationPreferencesPanel>();

        var saveButton = cut.FindAll("button").First(b => b.TextContent.Contains("Prefs_Btn_Save"));
        await cut.InvokeAsync(() => saveButton.Click());

        VerifyHandleErrorCalled();
    }
}
