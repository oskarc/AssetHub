using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AssetHub.Ui.Tests.Helpers;

/// <summary>
/// Base class for bUnit component tests.
/// Provides a pre-configured BunitContext with all common services registered.
/// Implements IAsyncLifetime so xUnit uses async disposal (required by MudBlazor services).
/// </summary>
public abstract class BunitTestBase : BunitContext, IAsyncLifetime
{
    protected Mock<AssetHubApiClient> MockApi { get; }
    protected Mock<IUserFeedbackService> MockFeedback { get; }
    protected Mock<IDialogService> MockDialogService { get; }

    protected BunitTestBase()
    {
        // Create mocks
        MockApi = new Mock<AssetHubApiClient>(MockBehavior.Loose, new HttpClient());
        MockFeedback = new Mock<IUserFeedbackService>();
        MockDialogService = new Mock<IDialogService>();

        // Register MudBlazor services
        Services.AddMudServices();

        // Register mocked services
        Services.AddSingleton(MockApi.Object);
        Services.AddSingleton(MockFeedback.Object);

        // Register stub IStringLocalizer<T> for all resource types — returns the key as the value
        Services.AddSingleton<IStringLocalizer<CommonResource>>(new StubStringLocalizer<CommonResource>());
        Services.AddSingleton<IStringLocalizer<AssetsResource>>(new StubStringLocalizer<AssetsResource>());
        Services.AddSingleton<IStringLocalizer<CollectionsResource>>(new StubStringLocalizer<CollectionsResource>());
        Services.AddSingleton<IStringLocalizer<SharesResource>>(new StubStringLocalizer<SharesResource>());
        Services.AddSingleton<IStringLocalizer<AdminResource>>(new StubStringLocalizer<AdminResource>());

        // bUnit Loose mode — any un-stubbed JS interop call returns a default value
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// The pre-rendered MudPopoverProvider. Popover content (e.g. MudSelect items)
    /// renders here rather than inline in the component under test.
    /// </summary>
    protected IRenderedComponent<MudPopoverProvider> PopoverProvider { get; private set; } = default!;

    /// <summary>
    /// xUnit IAsyncLifetime — called before each test.
    /// Pre-renders MudPopoverProvider so MudBlazor components that use popovers work.
    /// </summary>
    public Task InitializeAsync()
    {
        PopoverProvider = Render<MudPopoverProvider>();
        return Task.CompletedTask;
    }

    /// <summary>
    /// xUnit IAsyncLifetime — called after each test for async disposal.
    /// MudBlazor services (KeyInterceptorService, PointerEventsNoneService) require async disposal.
    /// </summary>
    Task IAsyncLifetime.DisposeAsync()
    {
        var vt = ((IAsyncDisposable)this).DisposeAsync();
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }

    /// <summary>
    /// Renders a MudBlazor dialog component with the given parameters inside a MudDialogProvider.
    /// </summary>
    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        return Render<MudDialogProvider>();
    }

    /// <summary>
    /// Shows a MudBlazor dialog via IDialogService inside a MudDialogProvider.
    /// Returns the dialog provider whose Markup contains the rendered dialog content.
    /// In MudBlazor 8 dialogs MUST be shown through the provider — inline rendering is no longer supported.
    /// </summary>
    protected async Task<IRenderedComponent<MudDialogProvider>> ShowDialogAsync<TDialog>(
        DialogParameters<TDialog> parameters,
        string title = "Test Dialog")
        where TDialog : Microsoft.AspNetCore.Components.ComponentBase
    {
        var provider = RenderDialogProvider();
        var dialogService = Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(async () =>
        {
            await dialogService.ShowAsync<TDialog>(title, parameters);
        });

        return provider;
    }

    /// <summary>
    /// Verifies that the feedback service received a ShowSuccess call with the specified message substring.
    /// </summary>
    protected void VerifySuccessShown(string messageContains)
    {
        MockFeedback.Verify(f => f.ShowSuccess(It.Is<string>(m => m.Contains(messageContains))), Times.AtLeastOnce());
    }

    /// <summary>
    /// Verifies that the feedback service received a ShowError call.
    /// </summary>
    protected void VerifyErrorShown()
    {
        MockFeedback.Verify(f => f.ShowError(It.IsAny<string>()), Times.AtLeastOnce());
    }

    /// <summary>
    /// Verifies that HandleError was called on the feedback service.
    /// </summary>
    protected void VerifyHandleErrorCalled()
    {
        MockFeedback.Verify(f => f.HandleError(It.IsAny<Exception>(), It.IsAny<string>()), Times.AtLeastOnce());
    }
}

/// <summary>
/// Stub IStringLocalizer that returns the key as the value.
/// Simplifies testing without real .resx files — we just verify the correct keys are used.
/// </summary>
public class StubStringLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name, false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(name, arguments), false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        Enumerable.Empty<LocalizedString>();
}
