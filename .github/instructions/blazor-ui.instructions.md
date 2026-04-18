---
applyTo: "src/AssetHub.Ui/**"
description: "Use when editing Blazor Server UI components, pages, dialogs, or layouts in the AssetHub.Ui project."
---
# Blazor UI Conventions (AssetHub.Ui)

AssetHub.Ui is a Razor Class Library that depends **only** on AssetHub.Application. Never reference Infrastructure or Api.

## Component Library
- MudBlazor 8 exclusively — use `MudCard`, `MudGrid`, `MudDialog`, `MudButton`, `MudChip`, `MudDataGrid`, etc.
- No raw HTML form elements when a MudBlazor equivalent exists.

## Pages
- `@attribute [Authorize]` on all pages (except public share pages).
- `@implements IAsyncDisposable` when using event subscriptions or timers.
- Inject services via `@inject` directives — common set:
  ```razor
  @inject AssetHubApiClient Api
  @inject NavigationManager Nav
  @inject IUserFeedbackService Feedback
  @inject IDialogService DialogService
  @inject IStringLocalizer<CommonResource> CommonLoc
  ```

## API Communication
- Use `AssetHubApiClient` for all HTTP calls — never call `HttpClient` directly.
- Handle `ServiceResult<T>` errors by showing user feedback via `IUserFeedbackService`.

## Dialogs
- Name dialog components `*Dialog.razor`.
- Use `MudDialog` with `[CascadingParameter] IMudDialogInstance` pattern.
- Return results via `MudDialog.Close(DialogResult.Ok(value))`.

## Localization
- Every user-visible string must use `IStringLocalizer<T>`.
- Resource marker classes in `Resources/ResourceMarkers.cs`.
- Key naming: `Area_Context_Element` (e.g., `Assets_Upload_Title`, `Common_Btn_Cancel`).
- Two languages: English (default `.resx`) and Swedish (`.sv.resx`).
- Add keys to **both** language files when creating new strings.

## Layouts
- `MainLayout.razor` — authenticated app shell with nav menu.
- `ShareLayout.razor` — separate layout for public share pages (no nav).

## Optimistic UI

Prefer optimistic UI updates for user-initiated mutations (delete, rename, add to collection, toggle, reorder). The goal is to make the UI feel instant — update local state first, then confirm with the server.

**Pattern:**
```razor
@code {
    private async Task DeleteItemAsync(Guid id)
    {
        // 1. Optimistically update local state
        var removedItem = _items.First(i => i.Id == id);
        _items.Remove(removedItem);
        StateHasChanged();

        // 2. Call the API
        var result = await Api.DeleteAsync(id);

        // 3. On failure: roll back and notify
        if (!result.IsSuccess)
        {
            _items.Add(removedItem);
            StateHasChanged();
            Feedback.ShowError(CommonLoc["Error_DeleteFailed"].Value);
        }
    }
}
```

**When to use optimistic UI:**
- Deleting items from lists (assets, collections, ACLs)
- Toggling boolean state (active/inactive, favorite)
- Adding/removing items from collections
- Renaming or updating single fields
- Reordering items

**When NOT to use optimistic UI:**
- File uploads (progress is real, can't fake it)
- Complex multi-step operations (creation wizards, bulk operations)
- Operations where failure is common (validation-heavy forms)
- Navigation after mutation (just await and navigate)

**Rules:**
- Always roll back local state on `ServiceResult` failure and show an error via `IUserFeedbackService`.
- Keep a reference to the removed/changed item before mutating so rollback is trivial.
- Don't optimistically update data that other components depend on (e.g., counts in the sidebar) — let those refresh naturally or refresh after confirmation.
- Success feedback (snackbar) can fire immediately with the optimistic update.

## State Management
- No third-party libraries (no Fluxor, BlazorState, Blazored.LocalStorage).
- Use scoped services, `CascadingAuthenticationState`, and `MudDialogService`.
- Caching via `HybridCache` — never `localStorage`/`sessionStorage` for app data.

## Error Handling
- Use `ErrorBoundary` for UI errors.
- User-visible error text is localized and action-oriented — never raw `ServiceError.Message`.
