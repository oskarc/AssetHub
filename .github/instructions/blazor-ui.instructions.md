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
