---
name: i18n-check
description: Audit AssetHub localization resources for key parity between English and Swedish, flag hardcoded user-facing strings in Razor/C#, and verify key-naming conventions. Use when adding UI, adding resource keys, or preparing a UI-focused PR.
---

# AssetHub Localization Parity Check

Localization regressions are silent. Missing Swedish keys fall back to English with no error. Hardcoded strings skip the translator entirely. This skill catches both before they ship.

## How to run

1. **Scope** — default to all resources; narrow only if the user asks:
   - `src/AssetHub.Ui/Resources/*.resx` — the English set.
   - `src/AssetHub.Ui/Resources/*.sv.resx` — the Swedish set.
   - If args provided (e.g., `AdminResource`), narrow to that pair.
2. **Read** each `.resx` pair and every Razor / C# file under `src/AssetHub.Ui/` that the changed components live in.
3. **Run the four checks** below.
4. **Report** grouped by check, with `file:line` for hardcoded-string findings and `key` for parity/naming findings.
5. **Offer to apply** safe fixes — add missing SV keys with `TODO(sv)` placeholders, replace hardcoded strings with `@Loc["..."]`, rename keys to the convention. Ask before applying.

## Checks

### 1. Key parity

For every `{Name}.resx` / `{Name}.sv.resx` pair:
- List keys present in one file but missing in the other.
- Flag any SV value that is literally identical to the EN value — likely an untranslated placeholder that was forgotten (exceptions: product names, numerics, URLs).
- Flag any empty `<value></value>`.

### 2. Key naming

Per CLAUDE.md § Localization:
- Pattern: **`Area_Context_Element`** in PascalCase with underscores — e.g., `Assets_Upload_Title`, `Common_Btn_Cancel`.
- `Common_` prefix only for genuinely shared strings — flag `Common_Admin_*` or other leaky names.
- `Btn_*` / `Label_*` / `Error_*` / `Event_*` / `Validation_*` prefix usage should match neighbors in the same file.
- Avoid `.` in key names (reserved for audit event keys like `Event_asset.created` that mirror the event-type string — those are fine).

### 3. Hardcoded user-facing strings

Grep Razor and C# under `src/AssetHub.Ui/` for strings that look user-facing but bypass `IStringLocalizer<T>`:

- **Razor prop values** containing prose: `Label=`, `Text=`, `Title=`, `HelperText=`, `Placeholder=`, `TitleContent=`, `aria-label=`, `alt=` where the value is a literal string (not an `@Loc[...]`, `@...` expression, or a variable).
- **`MudAlert` / `MudText` children** — the text between the tags, when it's not inside an `@` expression.
- **`Feedback.ShowSuccess("…")`, `Feedback.ShowError("…")`, `Feedback.ShowWarning("…")`** — the first arg should be `SomeLoc["..."].Value`, not a literal.
- **`DialogService.ShowConfirmAsync` arguments** — title, message, confirm text should all be localized.
- **`<PageTitle>` children** — must be localized on every routable page.
- **C# code-behind strings returned to the UI** in `@code { … }` blocks.

Exceptions to skip:
- Icon names (`Icons.Material.Filled.Foo`).
- CSS class names.
- Data-test attributes.
- Route templates (`@page "/assets"`).
- `nameof(...)` usages.
- Short tokens (≤ 2 chars, no letters) that are plainly not prose.

For each finding, cite `file:line` and suggest a key in the correct Area per the file's folder (e.g., a new `Admin_*` key for `Pages/Admin.razor`).

### 4. Localizer selection

Per CLAUDE.md: *Inject the most specific `IStringLocalizer<T>` — don't default to `CommonResource`.*

- For every Razor file that injects `IStringLocalizer<CommonResource>` **only**, verify the strings used really are common. Specific area strings accessed via `CommonLoc[...]` should move to the area's localizer.
- Flag files in `Components/Admin*.razor` that don't inject `IStringLocalizer<AdminResource>`.

## Output

Report structure:

```
Parity
  AdminResource: 2 missing SV keys
    - MetadataSchemas_Field_UnsavedChangesTitle
    - MetadataSchemas_Field_UnsavedChangesText

Naming
  CommonResource: Common_Admin_Sidebar — leaky prefix, move to AdminResource

Hardcoded strings
  src/AssetHub.Ui/Pages/Foo.razor:42 — <MudText>Welcome back</MudText>
    → propose key Foo_Welcome

Localizer selection
  src/AssetHub.Ui/Components/AdminBarTab.razor:5 — only CommonResource injected; Admin_* keys used
```

Finish with a total count per category. If nothing found, say so — short green report beats silence.

## Abort conditions

- A key appears in a resource file but has no matching injection point anywhere in code — flag as possibly dead, do not auto-delete.
- Resource file is malformed XML — report and stop; don't attempt to fix parser errors.
