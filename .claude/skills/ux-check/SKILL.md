---
name: ux-check
description: Review staged or recently-changed Blazor UI against Nielsen's 10 heuristics and AssetHub house rules. Use when the user asks for a usability review, checks UI changes, or prepares a UI-focused PR.
---

# AssetHub Usability Check (Nielsen's 10 + house rules)

Deep usability review of Blazor UI changes. Nielsen's 10 heuristics with AssetHub-specific conventions layered on top (optimistic UI, localization parity, MudBlazor-only, button naming).

## How to run

1. **Scope** — determine what to review:
   - If args provided, use them (path, glob, component name).
   - Otherwise: `git diff --name-only HEAD` + `git diff --cached --name-only`, narrowed to `src/AssetHub.Ui/**`.
   - If nothing is changed, ask the user which file/folder.
2. **Read** each file in full.
3. **Walk the heuristics** below. Cite `file:line` for every finding and give a concrete fix.
4. **Verify house rules** explicitly — they are where past reviews keep finding regressions.
5. **Report** grouped by heuristic with severity (Critical / Major / Minor / Cosmetic).
6. **Offer to apply** safe, in-scope fixes. Don't apply without asking.

## Heuristics

### 1. Visibility of system status
- Long-running operations show progress — spinner, progress bar, stepper. Never a frozen button.
- Background jobs (media processing, zip builds) surface completion to the user (toast, badge, polling UI that ends).
- Every mutation outcome fires an `IUserFeedbackService` snackbar (success or error).

### 2. Match between system and the real world
- Terminology matches the glossary: Asset, Collection, Share, Download. No unannounced synonyms.
- Dates / numbers / file sizes go through culture-aware formatting, not hardcoded `.ToString("g")`.
- No raw string literals for user-visible text — all localized.

### 3. User control & freedom
- Destructive mutations route through `ConfirmDialog`.
- Bulk permanent delete: second confirm with an explicit count ("Delete 5 assets permanently?").
- Delete actions offer an Undo snackbar where feasible.
- Edit dialogs with non-trivial input track dirty state and warn on discard.
- Navigation away from `ImageEditor.razor` with unsaved changes is guarded by `OnLocationChanging`.
- Every dialog has a visible Cancel button.

### 4. Consistency & standards
- Dialog button order: primary/destructive right, Cancel left (MudDialog convention).
- Button naming: **Delete** = permanent, **Remove** = unlink from parent, **Discard** = cancel changes. Apply consistently.
- Route/URL structure is canonical — no two routes for the same page without a documented reason.
- `MaxWidth` on dialogs follows house sizing (don't introduce a one-off `MaxWidth.ExtraLarge`).
- Icon choices match existing conventions for the same concept (folder, asset, share, user).

### 5. Error prevention
- File uploads pre-validate type and size client-side; server re-validates.
- Submit buttons disable while pending.
- Toggles arming destructive state (e.g., "permanent delete" switch) require a follow-up confirmation before firing.
- Forms that would lose work on navigation track dirty state.

### 6. Recognition rather than recall
- Breadcrumbs on nested pages.
- Tag / collection / user pickers use `MudAutocomplete` with suggestions, not free-text recall.
- Recent searches / filters persist where the flow is repeated (admin tables, asset search).
- Multi-select grids show visible selection state and a selected-count affordance.

### 7. Flexibility & efficiency of use
- Keyboard shortcuts on power-user pages (Admin, AssetDetail, ImageEditor): Ctrl+S to save, Delete on selected, Esc to cancel/close.
- Bulk actions available when lists support multi-select.
- Admin tables support saved filter presets on long-lived views.
- ImageEditor exposes shortcut hints in tooltips (e.g., `Crop (C)`, `Redact (R)`).

### 8. Aesthetic & minimalist design
- No redundant info (e.g., breadcrumb + page title saying the same thing in a single-level view).
- Tab/nav order matches frequency of use (Admin tabs ordered by what admins do most).
- Information density is appropriate — dense data grids paginate and use row virtualization.
- Empty states don't carry visual weight they don't earn.

### 9. Help users recognize, diagnose, and recover from errors
- User-facing error text is localized and action-oriented — never raw `ServiceError.Message`.
- Field-level validation errors bind back to the input, not only a top-of-form banner (`ServiceError.Validation(msg, details)` consumed).
- Upload / processing failures persist long enough to read, with copy-to-clipboard on detailed error messages.
- `EmptyState` includes a primary CTA, not just a headline. "No collections yet → Create your first collection".
- Errors suggest next steps ("Search for the asset in Collections", not just "Asset not found").

### 10. Help & documentation
- Every icon-only button has a `MudTooltip`.
- `ImageEditor` tool buttons have tooltips that name the tool and show the keyboard shortcut.
- Non-obvious admin tabs have a one-line tooltip explaining what they manage.
- First-time empty states explain what the feature is before asking the user to create something.

## House-specific rules (verify explicitly)

- **Optimistic UI** (CLAUDE.md § Blazor UI) — list mutations update local state first, roll back + error toast on failure. Edit dialogs (rename, change title) count.
- **Localization parity** — every new key in `*.resx` exists in `*.sv.resx` of the same domain. Count keys if needed: same file names, same key sets.
- **Most-specific localizer** — don't inject `CommonResource` if a domain-specific resource exists. `Common_` prefix only for truly shared strings.
- **MudBlazor only** — no raw HTML form elements where a MudBlazor equivalent exists.
- **Layout separation** — public share pages use `ShareLayout`; authenticated pages use `MainLayout`.
- **`AssetHubApiClient` only** — UI code never uses raw `HttpClient`.
- **`HybridCache`** — UI-side caching goes through `HybridCache`; never `localStorage`/`sessionStorage` for app data.

## Report format

```
## Usability review — {scope}

### Critical
- [BulkAssetActionsDialog.razor:61-73](…) — **Heuristic 5 Error Prevention** — "Permanent delete" toggle arms a destructive action without a second confirm. Fix: after clicking delete with the toggle on, show a `ConfirmDialog` with the asset count.

### Major
…

### Minor
…

### Cosmetic
…
```

If more than 5 findings, end with a prioritized top-N list ordered by severity × user impact.

## Scope notes

- Do not review files outside `src/AssetHub.Ui/`.
- Re-occurrences of CLAUDE.md Quality Guardrail items are worth calling out — they indicate the guardrail isn't being internalized.
- Don't invent heuristic violations. A finding needs a concrete user-impact story.
