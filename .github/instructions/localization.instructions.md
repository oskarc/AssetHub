---
applyTo: "src/AssetHub.Ui/**"
description: "Use when adding or editing localized strings, resource files, or IStringLocalizer usage in AssetHub Blazor components."
---
# Localization Conventions

AssetHub supports English (default) and Swedish (`sv`). Every user-visible string in the UI must be localized.

## Resource Structure

```
src/AssetHub.Ui/
  Resources/
    ResourceMarkers.cs          ← empty marker classes for IStringLocalizer<T>
    CommonResource.resx         ← English (default)
    CommonResource.sv.resx      ← Swedish
    AssetsResource.resx
    AssetsResource.sv.resx
    CollectionsResource.resx
    CollectionsResource.sv.resx
    ...
```

## Key Naming Convention

Pattern: **`Area_Context_Element`**

| Key | Meaning |
|-----|---------|
| `Assets_Upload_Title` | Assets area → Upload dialog → Title text |
| `Common_Btn_Cancel` | Shared → Button → Cancel label |
| `Collections_Form_NameLabel` | Collections area → Form → Name field label |
| `Admin_Users_DeleteConfirm` | Admin area → Users tab → Delete confirmation |

### Rules
- **PascalCase** with underscores as separators.
- **Common_** prefix for strings reused across areas (buttons, errors, status labels).
- Keep keys **descriptive enough** to locate usage without searching code.

## Adding a New String

1. Add the key + English value to the appropriate `.resx` file.
2. Add the key + Swedish value to the matching `.sv.resx` file.
3. **Both files must be updated together** — a missing key falls back to English silently, which creates a broken Swedish UI.

## Usage in Components

```razor
@inject IStringLocalizer<CommonResource> CommonLoc
@inject IStringLocalizer<AssetsResource> AssetsLoc

<MudText Typo="Typo.h5">@AssetsLoc["Assets_Upload_Title"]</MudText>
<MudButton OnClick="Cancel">@CommonLoc["Common_Btn_Cancel"]</MudButton>
```

### Rules
- Inject the **most specific** localizer — use `AssetsResource` for asset strings, not `CommonResource`.
- Only use `CommonResource` for genuinely shared strings (buttons, generic labels, status text).
- **Never use raw string literals** for user-visible text in `.razor` files.
- Error messages from services (`ServiceResult` errors) are **not localized** — they are API-level English strings. The UI translates them into user-friendly messages.

## Resource Markers

Marker classes in `ResourceMarkers.cs` are empty — they exist only as type arguments:

```csharp
public class CommonResource { }
public class AssetsResource { }
public class CollectionsResource { }
```

When adding a new resource domain (rare), add both the marker class and the `.resx`/`.sv.resx` file pair.
