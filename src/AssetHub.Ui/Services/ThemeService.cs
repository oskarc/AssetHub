using Microsoft.AspNetCore.Http;

namespace AssetHub.Ui.Services;

/// <summary>
/// Centralized dark mode state management. Reads from SSR cookies on initial load,
/// syncs with localStorage after first render, and notifies subscribers on change.
/// </summary>
public sealed class ThemeService
{
    private readonly LocalStorageService _localStorage;
    private bool _isDarkMode = true;
    private bool _initialized;

    public ThemeService(LocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public bool IsDarkMode => _isDarkMode;

    public event Action? OnChange;

    /// <summary>
    /// Read the dark mode cookie during SSR so the first render uses the correct theme.
    /// Call this from OnInitialized (synchronous, no JS interop).
    /// </summary>
    public void InitializeFromCookies(IHttpContextAccessor httpContextAccessor)
    {
        var cookies = httpContextAccessor.HttpContext?.Request.Cookies;
        if (cookies != null && cookies.TryGetValue("darkMode", out var dm))
        {
            _isDarkMode = !string.Equals(dm, "false", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Restore the authoritative value from localStorage after the first interactive render.
    /// Call this from OnAfterRenderAsync(firstRender: true).
    /// Returns true if the value changed (caller should call StateHasChanged).
    /// </summary>
    public async Task<bool> InitializeFromLocalStorageAsync()
    {
        if (_initialized) return false;
        _initialized = true;

        var stored = await _localStorage.GetAsync("darkMode");
        if (stored != null)
        {
            var newValue = stored == "true";
            if (newValue != _isDarkMode)
            {
                _isDarkMode = newValue;
                await _localStorage.SetWithCookieAsync("darkMode", stored);
                OnChange?.Invoke();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Toggle dark mode and persist to localStorage + cookie.
    /// </summary>
    public async Task ToggleAsync()
    {
        _isDarkMode = !_isDarkMode;
        await _localStorage.SetBoolWithCookieAsync("darkMode", _isDarkMode);
        OnChange?.Invoke();
    }
}
