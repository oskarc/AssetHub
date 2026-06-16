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
    protected Mock<IAssetHubApiClient> MockApi { get; }
    protected Mock<IUserFeedbackService> MockFeedback { get; }
    protected Mock<IDialogService> MockDialogService { get; }

    protected BunitTestBase()
    {
        // Create mocks. AssetHubApiClient was an HTTP-backed class; it's now an
        // in-process facade with ~36 service deps. We mock it via the protected
        // parameterless constructor exposed for Castle DynamicProxy.
        MockApi = new Mock<IAssetHubApiClient>(MockBehavior.Loose);
        MockFeedback = new Mock<IUserFeedbackService>();
        MockDialogService = new Mock<IDialogService>();

        // Register MudBlazor services
        Services.AddMudServices();

        // Register mocked services. The feedback service is wrapped in a pass-through
        // that genuinely runs ExecuteWithFeedbackAsync (for any T) and delegates the
        // verifiable Show*/Handle* calls to MockFeedback — so tests can both observe
        // the inner API call and verify error handling, without per-type Moq setup.
        Services.AddSingleton(MockApi.Object);
        Services.AddSingleton<IUserFeedbackService>(new PassThroughFeedbackService(MockFeedback.Object));

        // Register a stub IStringLocalizer<T> for EVERY resource type via an open
        // generic — returns the key as the value. An open generic (vs. one line per
        // resource marker) means adding or splitting a resource never requires a
        // matching edit here; previously the per-type list silently went stale when
        // AdminResource was split into per-area resources, breaking every component
        // that injected one of the new markers.
        Services.AddSingleton(typeof(IStringLocalizer<>), typeof(StubStringLocalizer<>));

        // Register LocalizedDisplayService (used by dialogs/components that display localized labels)
        Services.AddSingleton<LocalizedDisplayService>();

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

    /// <summary>
    /// Pass-through <see cref="IUserFeedbackService"/> for component tests. Delegates the
    /// observable Show*/Handle* calls to the supplied <see cref="MockFeedback"/> (so the
    /// existing Verify helpers keep working) while implementing the wrapper overloads
    /// concretely — generic over any <c>T</c>, mirroring the real service's semantics
    /// (success snackbar, benign cancellation, ApiException vs. generic error routing).
    /// </summary>
    private sealed class PassThroughFeedbackService(IUserFeedbackService inner) : IUserFeedbackService
    {
        public void ShowSuccess(string message) => inner.ShowSuccess(message);
        public void ShowActionableInfo(string message, string actionLabel, Func<Task> onAction, int durationMs = 10000)
            => inner.ShowActionableInfo(message, actionLabel, onAction, durationMs);
        public void ShowInfo(string message) => inner.ShowInfo(message);
        public void ShowWarning(string message) => inner.ShowWarning(message);
        public void ShowError(string message) => inner.ShowError(message);
        public void HandleError(Exception ex, string operationName) => inner.HandleError(ex, operationName);
        public void HandleApiError(ApiException ex, string operationName) => inner.HandleApiError(ex, operationName);

        public async Task<bool> ExecuteWithFeedbackAsync(Func<Task> operation, string operationName, string? successMessage = null)
        {
            try
            {
                await operation();
                if (successMessage is not null) inner.ShowSuccess(successMessage);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (ApiException ex) { inner.HandleApiError(ex, operationName); return false; }
            catch (Exception ex) { inner.HandleError(ex, operationName); return false; }
        }

        public async Task<(bool Success, T? Result)> ExecuteWithFeedbackAsync<T>(Func<Task<T>> operation, string operationName, string? successMessage = null)
        {
            try
            {
                var result = await operation();
                if (successMessage is not null) inner.ShowSuccess(successMessage);
                return (true, result);
            }
            catch (OperationCanceledException) { return (false, default); }
            catch (ApiException ex) { inner.HandleApiError(ex, operationName); return (false, default); }
            catch (Exception ex) { inner.HandleError(ex, operationName); return (false, default); }
        }
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
