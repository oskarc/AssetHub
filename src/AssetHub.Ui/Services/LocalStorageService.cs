using Microsoft.JSInterop;

namespace AssetHub.Ui.Services;

/// <summary>
/// Service for accessing browser localStorage with optional cookie synchronization.
/// Consolidates JS interop for localStorage across the application.
/// </summary>
public sealed class LocalStorageService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _jsModule;
    private bool _initialized;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
        {
            _jsModule = await _js.InvokeAsync<IJSObjectReference>("import", "./_content/AssetHub.Ui/js/helpers.js");
            _initialized = true;
        }
    }

    /// <summary>
    /// Gets a string value from localStorage.
    /// </summary>
    public async Task<string?> GetAsync(string key)
    {
        await EnsureInitializedAsync();
        return await _jsModule!.InvokeAsync<string?>("getLocalStorage", key);
    }

    /// <summary>
    /// Sets a string value in localStorage.
    /// </summary>
    public async Task SetAsync(string key, string value)
    {
        await EnsureInitializedAsync();
        await _jsModule!.InvokeVoidAsync("setLocalStorage", key, value);
    }

    /// <summary>
    /// Gets a boolean value from localStorage.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="defaultValue">Value to return if key doesn't exist.</param>
    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var stored = await GetAsync(key);
        return stored != null ? stored == "true" : defaultValue;
    }

    /// <summary>
    /// Sets a boolean value in localStorage.
    /// </summary>
    public async Task SetBoolAsync(string key, bool value)
    {
        await SetAsync(key, value.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Sets a value in localStorage and syncs to a cookie for SSR support.
    /// Useful for theme preferences that need to be available on initial page load.
    /// </summary>
    /// <param name="key">The storage key (used for both localStorage and cookie).</param>
    /// <param name="value">The value to store.</param>
    /// <param name="cookieMaxAgeSeconds">Cookie expiry in seconds (default: 1 year).</param>
    public async Task SetWithCookieAsync(string key, string value, int cookieMaxAgeSeconds = 31536000)
    {
        await EnsureInitializedAsync();
        await _jsModule!.InvokeVoidAsync("setLocalStorage", key, value);
        await _jsModule!.InvokeVoidAsync("setCookie", key, value, cookieMaxAgeSeconds);
    }

    /// <summary>
    /// Sets a boolean value in localStorage and syncs to a cookie for SSR support.
    /// </summary>
    public async Task SetBoolWithCookieAsync(string key, bool value, int cookieMaxAgeSeconds = 31536000)
    {
        await SetWithCookieAsync(key, value.ToString().ToLowerInvariant(), cookieMaxAgeSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null)
        {
            await _jsModule.DisposeAsync();
        }
    }
}
