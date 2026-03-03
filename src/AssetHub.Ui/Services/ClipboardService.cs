using Microsoft.JSInterop;

namespace AssetHub.Ui.Services;

/// <summary>
/// Service for clipboard operations via JS interop.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Copies text to the clipboard.
    /// </summary>
    /// <returns>True if successful, false if the operation failed.</returns>
    Task<bool> CopyTextAsync(string text);
}

/// <summary>
/// Implementation of <see cref="IClipboardService"/> using browser clipboard API.
/// </summary>
public class ClipboardService : IClipboardService
{
    private const string ClipboardWriteMethod = "navigator.clipboard.writeText";
    private readonly IJSRuntime _js;

    public ClipboardService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<bool> CopyTextAsync(string text)
    {
        try
        {
            await _js.InvokeVoidAsync(ClipboardWriteMethod, text);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
