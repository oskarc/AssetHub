---
description: "Review Blazor UI against Nielsen's 10 heuristics and AssetHub house rules. Use when checking usability, reviewing UI changes, or preparing a UI-focused PR."
mode: "agent"
---
# AssetHub Usability Check (Nielsen's 10 + house rules)

Deep usability review of Blazor UI changes. Nielsen's 10 heuristics with AssetHub-specific conventions layered on top (optimistic UI, localization parity, MudBlazor-only, button naming).

## Scope

Determine what to review:
- If the user specifies files/components, use those.
- Otherwise, check recently changed files under `src/AssetHub.Ui/**`.
- If nothing is changed, ask the user which file/folder to review.

Read each file in full.

## Heuristics

### 1. Visibility of system status
- Long-running operations show progress — spinner, progress bar, stepper. Never a frozen button.
- Background jobs (media processing, zip builds) surface completion to the user.
- Every mutation outcome fires an `IUserFeedbackService` snackbar (success or error).

### 2. Match between system and the real world
- Terminology matches the glossary: Asset, Collection, Share, Download.
- Dates/numbers/file sizes go through culture-aware formatting.
- No raw string literals for user-visible text — all localized.

### 3. User control & freedom
- Destructive mutations route through `ConfirmDialog`.
- Bulk permanent delete: second confirm with explicit count.
- Delete actions offer an Undo snackbar where feasible.
- Edit dialogs with non-trivial input track dirty state and warn on discard.
- Navigation away from ImageEditor with unsaved changes is guarded.
- Every dialog has a visible Cancel button.

### 4. Consistency & standards
- Dialog button order: primary/destructive right, Cancel left.
- Button naming: **Delete** = permanent, **Remove** = unlink from parent, **Discard** = cancel changes.
- Icon choices match existing conventions for the same concept.

### 5. Error prevention
- File uploads pre-validate type and size client-side; server re-validates.
- Submit buttons disable while pending.
- Forms that would lose work on navigation track dirty state.

### 6. Recognition rather than recall
- Breadcrumbs on nested pages.
- Tag/collection/user pickers use `MudAutocomplete` with suggestions.
- Multi-select grids show visible selection state and selected-count.

### 7. Flexibility & efficiency of use
- Keyboard shortcuts on power-user pages (Admin, AssetDetail, ImageEditor).
- Bulk actions available when lists support multi-select.
- ImageEditor exposes shortcut hints in tooltips.

### 8. Aesthetic & minimalist design
- No redundant info.
- Information density is appropriate — dense data grids paginate.
- Empty states don't carry visual weight they don't earn.

### 9. Help users recognize, diagnose, and recover from errors
- User-facing error text is localized and action-oriented — never raw `ServiceError.Message`.
- Field-level validation errors bind back to the input.
- `EmptyState` includes a primary CTA, not just a headline.

### 10. Help & documentation
- Every icon-only button has a `MudTooltip`.
- ImageEditor tool buttons show tool name and keyboard shortcut.
- First-time empty states explain the feature before asking the user to create something.

## House-Specific Rules (verify explicitly)

- **Optimistic UI** — list mutations update local state first, roll back + error toast on failure. Edit/rename count.
- **Localization parity** — every new key in `*.resx` exists in `*.sv.resx`.
- **Most-specific localizer** — don't inject `CommonResource` if a domain-specific resource exists.
- **MudBlazor only** — no raw HTML form elements where a MudBlazor equivalent exists.
- **Layout separation** — public share pages use `ShareLayout`; authenticated pages use `MainLayout`.
- **`AssetHubApiClient` only** — UI code never uses raw `HttpClient`.
- **`HybridCache`** — never `localStorage`/`sessionStorage` for app data.

## Report Format

```
## Usability review — {scope}

### Critical
- [BulkAssetActionsDialog.razor:61](…) — **Heuristic 5** — "Permanent delete" toggle arms destructive action without second confirm.

### Major / Minor / Cosmetic
…
```

If more than 5 findings, end with a prioritized top-N list. Offer to apply safe fixes.

## Scope Notes
- Only review files under `src/AssetHub.Ui/`.
- Findings that repeat quality guardrail items are worth calling out — the guardrail isn't being internalized.
