# AssetHub Styleguide

A reference for design tokens, component patterns, rulesets, and information architecture used across AssetHub.

All visible surfaces are built on **MudBlazor 8** — no other component libraries, and no raw HTML form elements where a Mud equivalent exists. The UI layer lives in the [AssetHub.Ui](../src/AssetHub.Ui/) Razor Class Library and depends only on `AssetHub.Application`.

---

## 1. Design Principles

1. **Clarity over cleverness.** Every page answers three questions at a glance: *where am I, what can I do here, what is the status of the things I care about*.
2. **Lift, not flatten.** Cards and elevated surfaces signal "this is a thing" — hover lifts reinforce interactivity.
3. **Optimistic by default.** User-initiated mutations update the UI immediately; failures roll back with a clear error. See [CLAUDE.md — Optimistic UI](../CLAUDE.md).
4. **Localized everywhere.** No hardcoded user-visible strings. Every label goes through `IStringLocalizer<T>`.
5. **Accessible by construction.** Skip links, `aria-label` on icon buttons, `role="main"`, keyboard-navigable tabs and menus. Dark mode is a first-class theme, not a filter.
6. **Consistent error surface.** Services return `ServiceResult` — the UI maps them to snackbars via `IUserFeedbackService`. Users never see a stack trace.

---

## 2. Design Tokens

### 2.1 Color Palette

Defined inline in [MainLayout.razor](../src/AssetHub.Ui/Layout/MainLayout.razor#L216-L275). Consume via MudBlazor CSS variables (`var(--mud-palette-primary)` etc.) — never hard-code hex values in CSS or components.

#### Light palette

| Token | Hex | Purpose |
|---|---|---|
| `Primary` | `#4a6fa5` | Actions, focus, brand accents (slate blue) |
| `PrimaryDarken` | `#3b5a87` | Hover/pressed primary |
| `PrimaryLighten` | `#6b8ebf` | Gradients, subtle primary backgrounds |
| `Secondary` | `#607d8b` | Supporting actions, muted emphasis |
| `Tertiary` | `#7e57c2` | Accent chrome (purple), storage chart |
| `Info` | `#2196f3` | Video asset type, informational chips |
| `Success` | `#43a047` | Image asset type, active shares, confirmations |
| `Warning` | `#fb8c00` | Document asset type, expired shares, non-destructive caution |
| `Error` | `#e53935` | Destructive actions, revoked shares, admin role |
| `Background` | `#f5f6f8` | Page background |
| `Surface` | `#ffffff` | Cards, dialogs, elevated surfaces |
| `AppbarBackground` | `#ffffff` | Top bar |
| `DrawerBackground` | `#fafbfc` | Side navigation |
| `TextPrimary` | `#1a2332` | Primary body text |
| `TextSecondary` | `#5f6b7a` | Subtitles, captions, muted text |
| `LinesDefault` | `#dfe3e8` | Borders, dividers |

#### Dark palette

Same semantic slots, tuned for contrast on `#171923` background: `Primary #7ba3d4`, `Surface #1e2030`, `TextPrimary #d0d1dc`, `TextSecondary #9496a8`, `LinesDefault #2d2f42`.

### 2.2 Typography

Font stack: **Inter** (primary, self-hosted woff2) → Roboto → Helvetica → Arial → sans-serif. Subsets: `latin` + `latin-ext` (covers English and Swedish). See [fonts.css](../src/AssetHub.Ui/wwwroot/css/fonts.css).

Typography scale is configured in [MainLayout.razor:107-168](../src/AssetHub.Ui/Layout/MainLayout.razor#L107-L168). Use MudBlazor `Typo.*` values — never set `font-size` inline.

| Variant | Weight | Letter-spacing | Line-height | Use |
|---|---|---|---|---|
| `Typo.h1` | 700 | `-0.02em` | 1.2 | Rare — hero only |
| `Typo.h2` | 700 | `-0.015em` | 1.25 | Major landing headings |
| `Typo.h3` | 600 | `-0.01em` | 1.3 | Section headings |
| `Typo.h4` | 600 | `-0.01em` | 1.35 | Page title (most pages use this) |
| `Typo.h5` | 600 | `-0.005em` | 1.4 | Subsection title |
| `Typo.h6` | 600 | `0` | 1.45 | Card/dialog title, panel heading |
| `Typo.subtitle1` | 500 | `0.005em` | — | Emphasized body |
| `Typo.subtitle2` | — | — | — | Label for a region (e.g. upload report header) |
| `Typo.body1` | 400 | `0.01em` | 1.65 | Primary reading text |
| `Typo.body2` | 400 | `0.01em` | 1.6 | Secondary body, dialog intros |
| `Typo.caption` | 400 | `0.02em` | 1.5 | Metadata, timestamps, hints |

Special classes:
- `.welcome-heading` — gradient-clipped primary text for the Home hero.
- `.page-subtitle` — secondary text under page headings.
- `.text-muted` — 0.8125rem secondary helper text.
- `.appbar-title` — 700 weight, `-0.01em` letter-spacing, app bar only.
- `.section-overline` — 0.6875rem uppercase label above sections (`"ADMINISTRATION"`).

### 2.3 Spacing

Use MudBlazor's spacing system: `ma-*`, `pa-*`, `mt-*`, `gap-*`, etc., on the 4px grid (e.g. `pa-3` = 12px, `pa-4` = 16px, `pa-6` = 24px).

Custom rhythm tokens in [app.css](../src/AssetHub.Api/wwwroot/css/app.css):
- `.section-block` — `2rem` bottom margin between page sections.
- `.section-header` — flex row, title left / action right, 1px bottom border.
- Dialog padding is overridden: title `14px 24px 0`, content `16px 24px`, actions `10px 24px` with a top divider.

### 2.4 Elevation & Radius

| Surface | Elevation | Radius | Notes |
|---|---|---|---|
| `MudAppBar` | `0` | — | With `.appbar-border` bottom line |
| `MudDrawer` | `0` | — | With `.drawer-border` right line |
| Outlined card | `0` + `Outlined=true` | `10px` (via global rule) | Forms, filter toolbars |
| Content card | `1` | `10-12px` | List containers, sidebars |
| Asset card | `2` | `8px` | Grid cards; lifts `-3px` on hover |
| Stat card | `1` | `12px` | Dashboard tiles; lifts `-2px` on hover |
| Dialog | default | default | Compact padding overrides |
| Login card | `4` | `16px` | Max-width 420px |

Hover shadows: cards use `0 4px 16px rgba(0,0,0,0.08)` (stat / collection) or `0 8px 24px rgba(0,0,0,0.12)` (asset). Transitions are `transform 0.15-0.2s ease, box-shadow 0.2s ease`.

### 2.5 Motion

- Card lifts: `transform: translateY(-2px or -3px)` + shadow grow, 150-200ms.
- Upload area: `border-color` + `background-color` transition on hover/drag, 200ms.
- Skip link: `top: -40px` → `0` on focus, 200ms.
- **No custom keyframe animations** — stick to MudBlazor defaults (ripples, skeletons, progress).

### 2.6 Icons

Material Icons via `Icons.Material.Filled.*` (default), `Icons.Material.Outlined.*` (dark-mode toggle for visual distinction), `Icons.Material.Rounded.*` (light-mode toggle).

**Canonical icon vocabulary:**

| Concept | Icon |
|---|---|
| Home / Dashboard | `Home` |
| Collections (nav) | `PhotoLibrary` |
| Collection (tree) | `Folder` / `FolderOpen` / `FolderShared` |
| All assets | `Collections` |
| Admin | `AdminPanelSettings` |
| Share | `Share` |
| Download | `Download` |
| Upload | `CloudUpload` / `AttachFile` |
| Edit | `Edit` |
| Delete | `Delete` |
| Image asset | `Image` |
| Video asset | `VideoFile` |
| Document asset | `Description` / `PictureAsPdf` |
| Generic file | `InsertDriveFile` |
| Success | `CheckCircle` |
| Error | `Error` |
| Warning | `Warning` |
| Info | `Info` |
| Audit / history | `History` |
| User (account) | `AccountCircle` |
| User management | `ManageAccounts` |
| Export preset | `Tune` |
| Password | `Lock` / `Visibility` / `VisibilityOff` |
| Menu toggle | `Menu` |
| Light/dark | `LightMode` / `DarkMode` |
| Refresh | `Refresh` |
| Copy | `ContentCopy` |
| Rotate | `RotateRight` / `RotateLeft` |
| Crop | `Crop` |

### 2.7 Semantic color mapping

Use the `AssetDisplayHelpers.Get*Color()` family — never pick colors ad-hoc. Defined in [AssetDisplayHelpers.cs](../src/AssetHub.Ui/Services/AssetDisplayHelpers.cs).

| Domain | Value → Color |
|---|---|
| Asset type | image → `Success`, video → `Info`, document → `Warning`, other → `Default` |
| Asset status | ready → `Success`, processing → `Info`, failed → `Error` |
| Share status | active → `Success`, expired → `Warning`, revoked → `Error` |
| Role | admin → `Error`, manager → `Warning`, contributor → `Info`, viewer → `Default` |
| Audit event | create/upload → `Success`; delete/revoke/cleanup → `Error`; update → `Warning`; download/share → `Info`; access/acl → `Secondary`; malware/processing_failed → `Error` |

---

## 3. Component Patterns

### 3.1 Page Scaffold

Every authenticated page follows this shape. Canonical example: [Home.razor](../src/AssetHub.Ui/Pages/Home.razor), [Admin.razor](../src/AssetHub.Ui/Pages/Admin.razor).

```razor
@page "/route"
@attribute [Authorize]                @* or [Authorize(Policy = "RequireAdmin")] *@
@implements IAsyncDisposable          @* if using CancellationToken or timers *@

@inject AssetHubApiClient Api
@inject IStringLocalizer<CommonResource> CommonLoc
@inject IStringLocalizer<XxxResource> Loc

<PageTitle>@Loc["PageTitle"] - @CommonLoc["AppName"]</PageTitle>

<MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="py-4">
    @* Header: Typo.h4 with optional leading icon *@
    @* Body: MudGrid / MudStack of content cards *@
</MudContainer>
```

- **MaxWidth:** `ExtraLarge` for main pages; `Small` for focused flows (login).
- **Outer padding:** `py-4` on the container; sections use `mb-4`–`mb-6` between them.
- **Always** render a `<PageTitle>` with `{Page} - {AppName}` format.
- **Always** use `CancellationTokenSource` for API calls and cancel in `DisposeAsync` when the page loads its own data.

### 3.2 Layouts

Two layouts only — do not add a third without discussion.

| Layout | Path | Purpose |
|---|---|---|
| `MainLayout` | [Layout/MainLayout.razor](../src/AssetHub.Ui/Layout/MainLayout.razor) | Authenticated app shell: app bar, drawer nav, skip link, theme + dialog + snackbar providers |
| `ShareLayout` | [Layout/ShareLayout.razor](../src/AssetHub.Ui/Layout/ShareLayout.razor) | Public share pages — minimal chrome, no nav drawer |

`MainLayout` wires up:
- `MudThemeProvider` with light + dark palettes and typography config
- Skip link (`#main-content`)
- App bar: drawer toggle, app title, user menu (change password, sign out), `LanguageSwitcher`, dark-mode toggle
- Drawer: `<NavMenu />` inside a `<nav aria-label>`
- Main content: `<MudMainContent Class="pt-16 px-5 py-6" id="main-content" role="main">`

### 3.3 Navigation

[NavMenu.razor](../src/AssetHub.Ui/Layout/NavMenu.razor) uses `<MudNavMenu>` + `<MudNavLink>`. Active state uses the global `.mud-nav-link.active` rule — a 3px primary left border.

Order:
1. Home (`/`)
2. Collections (`/collections`)
3. **Admin-only divider**
4. All Assets (`/all-assets`)
5. Admin (`/admin`)

Admin-only items are wrapped in `<AuthorizeView Policy="RequireAdmin">`. Never gate on role strings — always use an authorization policy or `RoleHierarchy` helpers.

### 3.4 Cards

| Class | Use | Characteristics |
|---|---|---|
| `.asset-card` | Grid asset cards | 8px radius, 160px thumb, hover `-3px`, `Elevation=2` |
| `.collection-card` | Collection grid | 8px radius, 132px min-height, hover `-2px` |
| `.stat-card` | Dashboard tiles | 12px radius, centered layout, `.stat-value` (1.75rem/700) + `.stat-label` (0.75rem/500 uppercase-ish) |
| `.content-card` | Section containers (sidebars) | 12px radius, `Elevation=1` |
| `.dialog-section` | Grouped controls inside a dialog | 10px radius, `Outlined=true`, primary focus ring |

### 3.5 Dialogs

Canonical: [ConfirmDialog.razor](../src/AssetHub.Ui/Components/ConfirmDialog.razor), [CreateShareDialog.razor](../src/AssetHub.Ui/Components/CreateShareDialog.razor), [CreateCollectionDialog.razor](../src/AssetHub.Ui/Components/CreateCollectionDialog.razor).

**Rules:**
- File name ends in `*Dialog.razor`.
- Root element is `<MudDialog>` with `TitleContent`, `DialogContent`, `DialogActions`.
- Title row: `<MudStack Row AlignItems="Center" Spacing="2" Class="dialog-title">` containing a colored `<MudIcon>` + `<MudText Typo="Typo.h6">`.
- Title accent bar class: `.dialog-title` (primary), `.dialog-title-success`, `.dialog-title-danger`.
- Content wrapper: `<MudStack Spacing="2" Class="dialog-min-width">` — `dialog-min-width` = `min(500px, 90vw)`.
- First child of content is usually a short `.dialog-intro` callout (body2, left-border primary, 8×12 padding).
- Grouped controls use `<MudPaper Class="pa-3 dialog-section" Elevation="0" Outlined="true">` with a `.dialog-section-title` header (icon + uppercase 0.75rem label + bottom border).
- Actions: `<MudButton>` Cancel first (text), primary action second (`Variant.Filled`, `Color.Primary`).
- **Loading state on primary action**: show `<MudProgressCircular Size="Small" Indeterminate="true" Class="mr-2" />` and set `Disabled="_isCreating"`.
- Accept via `MudDialog.Close(DialogResult.Ok(value))`, cancel via `MudDialog.Cancel()`.
- **`[CascadingParameter] IMudDialogInstance MudDialog` is required** — do not inject `IDialogService` inside a dialog.
- Open dialogs with `IDialogService.ShowAsync<T>()`. Return values via `DialogResult`.

### 3.6 Forms & Inputs

- Default variant: `Variant.Outlined`. Use `Margin.Dense` on toolbar/filter inputs.
- Validation: **DataAnnotations only** (per [CLAUDE.md](../CLAUDE.md)). No FluentValidation. Per-field `Validation` lambdas are acceptable for small inline checks.
- Required fields: `Required="true"` + `RequiredError` set to a localized string.
- `AutoFocus="true"` on the first field in a dialog form.
- `Immediate="true"` for fields that need live validation.
- `DebounceInterval="500"` for search fields (see `AssetToolbar`).
- Password inputs: toggle between `InputType.Password` and `InputType.Text` via an adornment (`Visibility` / `VisibilityOff`).
- Helper text goes in `HelperText`; errors go in `Error="..." ErrorText="..."`.

### 3.7 Tables

Canonical: [AssetTable.razor](../src/AssetHub.Ui/Components/AssetTable.razor).

- `<MudTable Hover="true" Dense="true" Elevation="2" Breakpoint="Breakpoint.Sm">`.
- `<MudTableSortLabel>` on sortable columns; set `InitialDirection="SortDirection.Descending"` on default sort.
- Use `DataLabel` on every `MudTd` for responsive mobile layout (label shows below the breakpoint).
- Action column: `Class="col-text-right"`, `<MudIconButton Size="Size.Small">` wrapped in `<MudTooltip>`.
- Thumbnail column: fixed `.col-icon` width (50px), 40×40 avatar.
- Selection checkbox column: `.col-w-42`, `@onclick:stopPropagation="true"` on the cell.
- Loading: swap table body for `<MudSkeleton>` rows, or render a skeleton grid for the enclosing panel.

### 3.8 Grids

- `<MudGrid>` with `<MudItem>`; standard asset grid breakpoints: `xs="12" sm="6" md="4" lg="3" xl="2"`.
- Use gutter spacing defaults — don't override `Spacing` unless you have a specific reason.

### 3.9 Toolbars

Canonical: [AssetToolbar.razor](../src/AssetHub.Ui/Components/AssetToolbar.razor).

- `<MudPaper Class="pa-3 my-3" Elevation="0" Outlined="true">` wrapping a `<MudGrid>`.
- Search / filter / view switcher order, left to right.
- View toggle: `<MudButtonGroup Size="Size.Small">` of two `<MudIconButton>` (`GridView` / `ViewList`), `Color.Primary` for the active one.

### 3.10 Empty states

Use the shared [EmptyState.razor](../src/AssetHub.Ui/Components/EmptyState.razor) component. Never inline an empty message.

- `Icon` (default `Inbox`) in `Size.Large`, `Color.Tertiary`.
- `Title` in `Typo.h6`, `Color.Secondary`.
- Optional `Description` in `Typo.body2`, wrapped to `.empty-state-max-width` (400px).
- Optional action button — `Variant.Filled`, `Color.Primary`, `StartIcon`.

### 3.11 Skeletons / loading

- Use `<MudSkeleton>` matched to the eventual shape.
- `SkeletonType.Text` for lines (specify `Width` and `Height`).
- `SkeletonType.Circle` for avatars/icons (matched dimensions).
- `SkeletonType.Rectangle` for media/thumbnails.
- For stat cards and dashboards, render a full skeleton layout matching the final card dimensions — prevents layout shift.

### 3.12 Breadcrumbs

`<MudBreadcrumbs Items="_breadcrumbs" Class="pa-0">` with a custom `ItemTemplate` that uses `<MudLink OnClick Color="Color.Primary" Class="cursor-pointer">`. See [Assets.razor:27-31](../src/AssetHub.Ui/Pages/Assets.razor#L27-L31).

### 3.13 Chips

- Always pair a `Color` with `Variant.Outlined` for status/type chips — filled variants are reserved for high-emphasis states.
- `Size.Small` in lists, tables, and stat tiles; default size in headers.
- Chip for "clickable filter": add `.chip-clickable` (cursor pointer).
- Global refinement: `font-weight: 500; letter-spacing: 0.01em` (app.css).

### 3.14 Buttons

| Variant | Use |
|---|---|
| `Variant.Filled` + `Color.Primary` | Primary CTA (Create, Save, Share, Apply) |
| `Variant.Filled` + `Color.Success` | Confirmatory action in an editor (Apply Crop) |
| `Variant.Filled` + `Color.Error` | Destructive confirm (Delete) |
| `Variant.Outlined` | Secondary actions in a toolbar row |
| `Variant.Text` | Inline / tertiary actions, toolbar items without borders |
| `MudIconButton` | Single-icon actions — always wrap in `MudTooltip` and provide `aria-label` |

- Primary dialog action always has a loading state (spinner + disabled).
- Toolbar buttons: `Size="Size.Small"`.
- Do not use raw `<button>` elements.

### 3.15 Snackbars

Dispatched via [IUserFeedbackService](../src/AssetHub.Ui/Services/IUserFeedbackService.cs). Never call `ISnackbar` directly.

| Severity | Duration | Icon | Interaction |
|---|---|---|---|
| Success | 3000ms | `CheckCircle` | Auto-dismiss |
| Info | 4000ms | `Info` | Auto-dismiss |
| Warning | 5000ms | `Warning` | Auto-dismiss |
| Error | 6000ms | `Error` | **Requires user dismissal** |

- Use `Feedback.ExecuteWithFeedbackAsync(...)` to wrap API calls — it handles transient retries and maps `ApiException` to localized messages by status code.
- Service error messages (from `ServiceResult`) are **not** localized server-side. The UI translates them — either use the API-returned message as-is or fall back to a status-code-based string.

### 3.16 Upload area

Canonical: [AssetUpload.razor](../src/AssetHub.Ui/Components/AssetUpload.razor).

- `.upload-area` class: 2px dashed border, 12px radius, hover/drag changes border to primary and background to `--mud-palette-primary-hover`.
- Use `<InputFile hidden>` + a `<MudButton HtmlTag="label" for="fileInput">` trigger.
- Always include drag-and-drop handlers with `@ondragover:preventDefault` + `@ondrop:preventDefault`.
- After upload, render an `.upload-report` `MudPaper` with total / succeeded / failed counts and a per-file list (icon + name + size + optional error).

### 3.17 Image Editor (scoped styles)

The image editor uses a scoped stylesheet ([ImageEditor.razor.css](../src/AssetHub.Ui/Pages/ImageEditor.razor.css)) — the **only** place where CSS grid/flex layout is used at page scale. Tokens:

- `.editor-page` — fixed position under app bar, takes remaining viewport height.
- `.editor-layer-panel` — 240px left panel, collapses below 900px.
- `.editor-inspector-panel` — 260px right panel, collapses below 900px.
- `.editor-canvas-area` — 1fr center with `background: var(--mud-palette-background)`.

Do not copy this pattern for non-editor pages.

---

## 4. Theming

### 4.1 Theme service

`ThemeService` (scoped) owns dark/light mode state. Bound to `<MudThemeProvider IsDarkMode="Theme.IsDarkMode" />` in `MainLayout`. The service:

1. Reads a cookie on first server render (`InitializeFromCookies`).
2. Reads `localStorage` after first client render (`InitializeFromLocalStorageAsync`), overriding the cookie if set.
3. Toggle via `Theme.ToggleAsync()` — persists to both cookie and localStorage.

### 4.2 Writing CSS that works in both modes

- Always reference `var(--mud-palette-*)` — never hard-code colors.
- Test every new CSS rule in both themes before shipping.
- Avoid raw `rgba(0,0,0,*)` shadows — MudBlazor shadow tokens already adapt; the few custom hover shadows in `app.css` use subtle opacity that reads fine in dark mode.
- Gradient text (e.g. `.welcome-heading`) uses two palette tokens so it reskins automatically.

---

## 5. Localization

Two languages: English (default `.resx`) and Swedish (`.sv.resx`). See [CLAUDE.md — Localization](../CLAUDE.md) for full rules.

### 5.1 Resource files

Located in [src/AssetHub.Ui/Resources/](../src/AssetHub.Ui/Resources/):

| File pair | Scope |
|---|---|
| `CommonResource` | App-wide labels, buttons, validation messages, role/type names |
| `AssetsResource` | Asset list + detail |
| `CollectionsResource` | Collection browser + forms |
| `AdminResource` | Admin tabs and dialogs |
| `ImageEditorResource` | Image editor toolbar and inspector |
| `SharesResource` | Share dialogs and share landing pages |

### 5.2 Key naming

**Pattern:** `Area_Context_Element` in PascalCase with underscores.

| Prefix | Purpose | Example |
|---|---|---|
| `Common_` or bare in `CommonResource` | Shared | `Btn_Cancel`, `Label_Password` |
| `Btn_` | Buttons | `Btn_Share`, `Btn_SignIn` |
| `Label_` | Input labels | `Label_Type`, `Label_Password` |
| `Validation_` | Validation messages | `Validation_InvalidEmail` |
| `Nav_` | Navigation items | `Nav_Home`, `Nav_Admin` |
| `Aria_` | `aria-label` values | `Aria_ToggleNavigation`, `Aria_GridView` |
| `Dashboard_` | Dashboard strings | `Dashboard_Welcome` |
| `Filter_` | Filter choices | `Filter_AllTypes` |
| `Role_` | Role names | `Role_Viewer` |
| `AssetType_` | Asset type labels | `AssetType_Image` |
| `AssetStatus_` | Processing status | `AssetStatus_Ready` |
| `ContentType_` | MIME labels | `ContentType_JPEG` |
| `Feedback_` | Snackbar messages | `Feedback_RequestTimedOut` |
| `Text_` | Longer prose | `Text_SignInDescription` |
| `Tab_` | Tab labels | `Tab_UserManagement` |

### 5.3 Rules

- Add keys to **both** `.resx` and `.sv.resx` together — missing keys fall back to English silently.
- Inject the most specific localizer (e.g. `AssetsResource` for asset strings, not `CommonResource`).
- Never use raw string literals for user-visible text.
- Service `ServiceResult` error messages are not localized — the UI maps them.
- When adding a new resource domain: add a marker class to [ResourceMarkers.cs](../src/AssetHub.Ui/Resources/ResourceMarkers.cs) and the `.resx` / `.sv.resx` file pair together.

---

## 6. Utility Classes

Defined in [app.css](../src/AssetHub.Api/wwwroot/css/app.css). Prefer MudBlazor utilities (`ma-*`, `pa-*`, `d-flex`, `align-center`, `gap-*`) first; use these when a MudBlazor equivalent does not exist.

### Layout
- `.h-100`, `.w-100` — full height / width
- `.flex-1`, `.flex-shrink-0`, `.flex-1-min-0` — flex helpers
- `.overflow-hidden`
- `.d-block`

### Text
- `.text-truncate`, `.text-truncate-120`, `.text-truncate-180`, `.text-truncate-200`
- `.text-secondary` — `var(--mud-palette-text-secondary)`
- `.text-muted` — secondary, 0.8125rem, 1.5 line-height
- `.font-semibold` — weight 600
- `.font-italic`
- `.word-break-all`

### Interactivity
- `.clickable`, `.cursor-pointer`, `.chip-clickable`

### Structure / columns
- `.col-w-42`, `.col-w-50`, `.col-w-100`, `.col-w-120`, `.col-w-200`
- `.col-icon` — 50px width, 4px padding
- `.col-text-right`
- `.max-w-300`, `.max-w-400`

### Accents
- `.accent-border-left` — 3px primary left border + 12px padding
- `.section-overline` — uppercase micro-label

### Checkbox compactors
- `.checkbox-compact` — `-4px` margin for grid/table placement
- `.checkbox-compact-card` — card-specific negative margins

**Adding a new utility:** only do so if it's used in ≥3 places. One-off layout tweaks go in component markup as MudBlazor classes. Name the class after its effect, not its location (`.text-truncate-200`, not `.asset-title`).

---

## 7. Information Architecture

### 7.1 Route map

| Route | Page | Auth | Purpose |
|---|---|---|---|
| `/` | [Home.razor](../src/AssetHub.Ui/Pages/Home.razor) | `[Authorize]` | Dashboard: welcome, stats (admin), recent assets, quick-access collections, shares + activity sidebar (manager+) |
| `/collections`, `/assets` | [Assets.razor](../src/AssetHub.Ui/Pages/Assets.razor) | `[Authorize]` | Collection browser + asset grid/list. Collection selected via `?collection=<guid>` |
| `/assets/{id}` | [AssetDetail.razor](../src/AssetHub.Ui/Pages/AssetDetail.razor) | `[Authorize]` | Single-asset viewer: media preview, metadata, derivatives, actions |
| `/all-assets` | [AllAssets.razor](../src/AssetHub.Ui/Pages/AllAssets.razor) | `RequireAdmin` | Flat admin view of every asset |
| `/admin` | [Admin.razor](../src/AssetHub.Ui/Pages/Admin.razor) | `RequireAdmin` | Tabs: Shares, Collection Access, Users, Export Presets, Audit. Tab index in `?tab=N` |
| `/image-editor/{id}` | [ImageEditor.razor](../src/AssetHub.Ui/Pages/ImageEditor.razor) | `[Authorize]` | Crop, rotate, resize, layer, save-as-copy |
| `/share/{token}` | [Share.razor](../src/AssetHub.Ui/Pages/Share.razor) | `AllowAnonymous` (uses ShareLayout) | Public share landing |
| `/login` | [Login.razor](../src/AssetHub.Ui/Pages/Login.razor) | `AllowAnonymous` | OIDC redirect entrypoint |
| `/error` | [Error.razor](../src/AssetHub.Ui/Pages/Error.razor) | `AllowAnonymous` | Global error surface |

### 7.2 Role model

| Role | Level | Capabilities (cumulative) |
|---|---|---|
| Viewer | 1 | Read assets/collections they have ACL on |
| Contributor | 2 | + Upload, share, edit metadata, manage collection membership |
| Manager | 3 | + Delete assets, edit collection properties, manage ACLs |
| Admin | 4 | + Platform admin: users, audit, export presets, all assets |

- Check role via `RoleHierarchy` predicates (`CanUpload`, `CanDelete`, `HasSufficientLevel`) or `RolePermissions` (UI wrapper). **Never hardcode role strings or integer levels**.
- Per-collection permissions come from `CollectionAcl` via `ICollectionAuthorizationService` — check collection access before entity access.
- Admin bypasses ACL checks.
- In Razor, gate components with `<AuthorizeView Policy="RequireViewer|RequireContributor|RequireManager|RequireAdmin">`.

### 7.3 Dashboard composition

Home renders conditionally by role:

1. **Welcome hero** — everyone.
2. **Platform stats** — admin only (stats are null for non-admins).
3. **Recent assets grid** — everyone.
4. **Quick-access collections** — everyone.
5. **Sidebar: Active shares + Activity timeline** — manager+, or anyone who has shares/activity.

The main column collapses to `md=12` when the sidebar is hidden.

### 7.4 Collections page layout

Two-panel:
1. Left: `CollectionTree` (scrollable, `.collection-tree-scroll` max-height 60vh).
2. Right: breadcrumbs + action toolbar + `AssetToolbar` (search, filter, view mode) + `AssetGrid` or `AssetTable`.

Collection selection is a URL param (`?collection=<id>`). View mode (grid/list) is persisted in `LocalStorageService`.

### 7.5 Admin tabs

Tab index is stored in the `?tab=N` query string and restored on navigation.

| Index | Tab | Component |
|---|---|---|
| 0 | Share Management | `AdminSharesTab` |
| 1 | Collection Access | `AdminCollectionAccessTab` |
| 2 | User Management | `AdminUsersTab` |
| 3 | Export Presets | `AdminExportPresetsTab` |
| 4 | Audit Log | `AdminAuditTab` |

Each tab is self-loading — do not load admin data in `Admin.razor` itself.

### 7.6 Asset detail layout

Vertical stack:
1. Back breadcrumb.
2. `MediaPreview` (image/video/pdf iframe — max-height 600px).
3. Metadata panel (title, type chip, size, created, collection, tags).
4. `DerivativesPanel` (thumb/medium/original download links).
5. Actions (share, edit, download, delete, open in editor for images).

---

## 8. State Management Rulesets

1. **No third-party state libraries.** No Fluxor, BlazorState, Blazored.LocalStorage. Use scoped services, `CascadingAuthenticationState`, and `MudDialogService`.
2. **Caching: HybridCache (L1 memory + L2 Redis), not `IMemoryCache` or browser storage.** Cache keys centralized in [Application/CacheKeys.cs](../src/AssetHub.Application/CacheKeys.cs).
3. **Do not cache authorization data.** Roles and ACL lookups use request-scoped dictionaries (`PreloadUserRolesAsync`).
4. **Page-local state** lives in the page's `@code` block as private fields.
5. **Cross-component state** goes in a scoped service in [Services/](../src/AssetHub.Ui/Services/). Existing examples: `ThemeService`, `LocalStorageService`, `IUserFeedbackService`.
6. **HTTP** only through `AssetHubApiClient` — never inject `HttpClient` directly into a component.
7. **Persist UI preferences** (view mode, dark mode) through `LocalStorageService`.
8. **Cancel work on dispose.** Long-running pages implement `IAsyncDisposable`, hold a `CancellationTokenSource`, and pass its token to every API call.

---

## 9. Accessibility Rulesets

1. Every icon-only button has an `aria-label` (localized via `Aria_*` resource keys).
2. The main content landmark is `<MudMainContent role="main" id="main-content">`; the skip link targets `#main-content`.
3. Navigation is wrapped in `<nav aria-label>`.
4. Color is never the sole signal — pair with icon + text (success green + check, error red + error icon).
5. Dialogs trap focus (MudBlazor default); confirm buttons keep their color semantics (primary/error/success).
6. Keyboard: `Enter` submits forms; `Esc` closes dialogs; `Tab` navigates focus ring (2px outline offset 2px on `.mud-card:focus-visible`).
7. Respect `prefers-reduced-motion` — keep custom animations short and avoid parallax.

---

## 10. Responsiveness

- Default container: `MaxWidth.ExtraLarge` with `Breakpoint.Sm` on tables.
- Drawer: `Breakpoint.Md` — becomes temporary below md.
- Asset grid: `xs=12 sm=6 md=4 lg=3 xl=2`.
- Dashboard sidebar: collapses under md (main content becomes `xs=12`).
- Image editor panels: hidden below 900px via media query.
- Always verify new layouts at 375px (mobile), 768px (tablet), 1440px (desktop).

---

## 11. Performance

- Images: `loading="lazy"` on every `<MudCardMedia>` / `<MudImage>` in a list.
- Thumbnails: use `AssetDisplayHelpers.GetThumbnailUrl()` — falls back from `/thumb` → `/poster` (for videos) → inline SVG placeholder. Placeholder SVGs are cached in a `ConcurrentDictionary`.
- Debounce search: `DebounceInterval="500"` on search fields.
- Pagination: `.Skip().Take()` + count-first in repositories. UI shows a `MudPagination` or `MudTablePager`.
- Skeletons for any load > ~200ms to avoid layout shift.

---

## 12. What NOT to do

- ❌ Import any component library other than MudBlazor.
- ❌ Add domain events, FluentValidation, specification pattern, or ASP.NET Identity. (See [CLAUDE.md](../CLAUDE.md).)
- ❌ Hard-code hex colors in CSS or inline styles — always `var(--mud-palette-*)`.
- ❌ Hard-code role strings (`"admin"`) or level integers (`4`) — use `RoleHierarchy` / `RolePermissions`.
- ❌ Call `ISnackbar` or `HttpClient` directly from components — go through `IUserFeedbackService` / `AssetHubApiClient`.
- ❌ Reference `Infrastructure` or `Api` from `AssetHub.Ui`.
- ❌ Write user-visible strings without `IStringLocalizer`.
- ❌ Create a new layout — extend `MainLayout` or `ShareLayout`.
- ❌ Cache authorization data globally.
- ❌ Throw exceptions from services for business errors — return `ServiceResult`.
- ❌ Copy the image-editor scoped CSS pattern for other pages without design review.
- ❌ Add a utility class used in only one place — inline it.
