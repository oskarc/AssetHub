using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Ui.Components;
using MudBlazor;

namespace AssetHub.Ui.Services;

/// <summary>
/// Extension methods for IDialogService that encapsulate common dialog flows.
/// Reduces boilerplate at call sites by handling parameter construction, invocation,
/// and result checking in one call.
/// </summary>
public static class DialogExtensions
{
    /// <summary>
    /// Shows a ConfirmDialog and returns true if the user confirmed.
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(
        this IDialogService dialogService,
        string title,
        string message,
        string confirmText,
        string? icon = null,
        Color iconColor = Color.Warning,
        Color confirmColor = Color.Primary)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Title, title },
            { x => x.Message, message },
            { x => x.TitleIcon, icon ?? Icons.Material.Filled.Warning },
            { x => x.IconColor, iconColor },
            { x => x.ConfirmText, confirmText },
            { x => x.ConfirmColor, confirmColor }
        };

        var dialog = await dialogService.ShowAsync<ConfirmDialog>(null, parameters);
        var result = await dialog.Result;

        return result is { Canceled: false };
    }

    /// <summary>
    /// Shows the CreateShareDialog followed by ShareLinkDialog if the user completes the share.
    /// Returns the created share DTO, or null if cancelled.
    /// </summary>
    public static async Task<ShareResponseDto?> ShowShareFlowAsync(
        this IDialogService dialogService,
        Guid scopeId,
        string scopeType,
        string contentName,
        string createTitle,
        string successTitle)
    {
        var createParams = new DialogParameters<CreateShareDialog>
        {
            { x => x.ScopeId, scopeId },
            { x => x.ScopeType, scopeType },
            { x => x.ContentName, contentName }
        };

        var createDialog = await dialogService.ShowAsync<CreateShareDialog>(createTitle, createParams);
        var result = await createDialog.Result;

        if (result is not { Canceled: false } || result.Data is not ShareResponseDto share)
            return null;

        var successParams = new DialogParameters<ShareLinkDialog>
        {
            { x => x.ShareUrl, share.ShareUrl },
            { x => x.Password, share.Password ?? "" },
            { x => x.ExpiresAt, share.ExpiresAt }
        };

        await dialogService.ShowAsync<ShareLinkDialog>(successTitle, successParams);

        return share;
    }
}
