---
name: a11y-check
description: Audit staged or recently-changed Blazor UI files against WCAG 2.2 Level AA. Use when the user asks to check accessibility, reviews UI changes, or prepares a UI-focused PR.
---

# AssetHub Accessibility Check (WCAG 2.2 AA)

Deep accessibility audit of Blazor UI changes. The on-the-fly guardrails in CLAUDE.md cover common cases; this skill goes further — full WCAG 2.2 AA walkthrough of changed files with concrete fixes.

## How to run

1. **Scope** — determine what to audit:
   - If args provided, use them as the scope (path, glob, component name).
   - Otherwise: `git diff --name-only HEAD` (unstaged) + `git diff --cached --name-only` (staged), narrowed to `src/AssetHub.Ui/**/*.razor{,.cs,.css}`.
   - If nothing is changed, ask the user which file/folder to audit.
2. **Read** each in-scope file in full. Do not skim.
3. **Walk the POUR checklist** below against every file. Cite `file:line` for every finding.
4. **Check consistency** — when a pattern is introduced (image, dialog, status message), compare with neighbors in the same folder. Diverging patterns are findings.
5. **Report** grouped by POUR with severity (Critical / Major / Minor), WCAG criterion number + name, and a concrete fix (not generic advice).
6. **Offer to apply** safe, in-scope fixes. Do not apply without asking.

## POUR checklist

### Perceivable
- **1.1.1 Non-text Content** — every `MudCardMedia`, `MudImage`, `<img>` has meaningful `alt` or `aria-hidden="true"` if decorative. Meaningful `MudIcon` has `aria-label`; decorative `MudIcon` has `aria-hidden="true"`.
- **1.3.1 Info and Relationships** — headings follow hierarchy (no h2→h4 jumps). `MudSimpleTable` renders `<thead>`/`<tbody>`, header cells use `<th scope="col">`. Form labels tied to inputs via `Label=` / `For=`.
- **1.3.2 Meaningful Sequence** — DOM order matches visual reading order. Flex/grid reordering (`order:`, `flex-direction: row-reverse`) does not scramble tab order.
- **1.4.1 Use of Color** — state is never conveyed by color alone. Status chips, validation indicators, and alerts include icon or text.
- **1.4.3 Contrast (Minimum)** — ≥ 4.5:1 body, ≥ 3:1 large text. Flag any new custom theme color or `.razor.css` override for manual contrast check.
- **1.4.10 Reflow** — content reflows at 320 px. Flag fixed widths / min-widths in `.razor.css`.
- **1.4.11 Non-text Contrast** — UI component boundaries and state indicators meet 3:1 against adjacent colors.
- **1.4.13 Content on Hover or Focus** — hover popovers/tooltips are dismissible, hoverable, and persistent.

### Operable
- **2.1.1 Keyboard** — every mouse interaction has a keyboard equivalent. Canvas / drag-based UI (`ImageEditor.razor`) needs arrow-key moves, +/- scale, Delete, Escape.
- **2.1.2 No Keyboard Trap** — custom focus management inside dialogs/popovers returns focus to the trigger on close.
- **2.4.1 Bypass Blocks** — skip-to-main-content link present in both `MainLayout` and `ShareLayout`.
- **2.4.2 Page Titled** — `<PageTitle>` set on every routable page.
- **2.4.3 Focus Order** — tab order is logical and matches visual order.
- **2.4.4 Link Purpose** — link text is self-describing. No "click here", "more", "read on".
- **2.4.7 Focus Visible** — outlines not stripped without a `:focus-visible` replacement.
- **2.4.11 Focus Not Obscured** — focused element is not hidden by sticky headers, toolbars, snackbars, or dialogs.
- **2.5.7 Dragging Movements** — any drag-to-reorder / drag-to-crop / drag-to-move has a non-drag alternative (buttons, keyboard).
- **2.5.8 Target Size (Minimum)** — interactive targets ≥ 24×24 CSS px. `Size.Small` MudIconButtons inside dense toolbars are borderline; verify.

### Understandable
- **3.1.1 Language of Page** — `App.razor` `<html lang>` is bound to current culture, not hardcoded.
- **3.1.2 Language of Parts** — regions rendered in a different language use `lang="sv"` / `lang="en"` on the wrapping element.
- **3.2.1 On Focus** — focusing a control never triggers a context change (no auto-submit on focus/blur).
- **3.2.2 On Input** — changing a form value never submits or navigates without the user asking.
- **3.3.1 Error Identification** — errors identified in text, not color alone.
- **3.3.2 Labels or Instructions** — every input has a visible label (`Label=` on MudBlazor fields).
- **3.3.3 Error Suggestion** — validation messages tell the user how to fix the problem.
- **3.3.7 Redundant Entry** — previously-entered info is pre-filled or selectable in multi-step flows.
- **3.3.8 Accessible Authentication** — password/share-PIN visibility toggle has `aria-label`; browser autofill is not broken by custom controls.

### Robust
- **4.1.2 Name, Role, Value** — custom components expose name/role/value. `MudDialog` has an accessible name via `TitleContent` tied to `aria-labelledby`, or an explicit `aria-label`. Custom div-buttons have `role="button"` + `tabindex="0"` + keyboard handler.
- **4.1.3 Status Messages** — status messages are inside `role="status" aria-live="polite"` (or `role="alert"` + `aria-live="assertive"` for errors). Confirm `IUserFeedbackService` / MudSnackbar uses the correct live region for the message severity.

## Report format

```
## Accessibility review — {scope}

### Critical
- [AssetCardGrid.razor:25](src/AssetHub.Ui/Components/AssetCardGrid.razor#L25) — **1.1.1 Non-text Content** — `MudCardMedia` has no `alt`. Fix: add `alt="@($"{asset.Title} ({asset.Type})")"`.

### Major
…

### Minor
…

### Clean
- 2.4.2 Page Titled — all pages set `<PageTitle>`. ✓
- 3.1.1 Language of Page — `App.razor` binds `lang` to culture. ✓
```

If more than 3 findings, end with a prioritized top-N fix list ordered by severity and effort.

## Scope notes

- Do not audit files outside `src/AssetHub.Ui/`.
- Generated files, `_Imports.razor`, and pure layout primitives are low-signal — note briefly and move on.
- If a finding is a re-occurrence of something in CLAUDE.md's Quality Guardrails, say so — it's a signal the guardrail is being missed and should be reinforced.
