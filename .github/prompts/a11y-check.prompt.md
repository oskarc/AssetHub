---
description: "Audit Blazor UI files against WCAG 2.2 Level AA. Use when checking accessibility, reviewing UI changes, or preparing a UI-focused PR."
mode: "agent"
---
# AssetHub Accessibility Check (WCAG 2.2 AA)

Deep accessibility audit of Blazor UI changes. The quality guardrails in the instructions cover common cases; this prompt goes further — full WCAG 2.2 AA walkthrough with concrete fixes.

## Scope

Determine what to audit:
- If the user specifies files/components, use those.
- Otherwise, check recently changed files under `src/AssetHub.Ui/**/*.razor`, `*.razor.cs`, `*.razor.css`.
- If nothing is changed, ask the user which file/folder to audit.

Read each in-scope file in full. Do not skim.

## POUR Checklist

Walk every file against this checklist. Cite `file:line` for every finding.

### Perceivable
- **1.1.1 Non-text Content** — every `MudCardMedia`, `MudImage`, `<img>` has meaningful `alt` or `aria-hidden="true"` if decorative. Meaningful `MudIcon` has `aria-label`; decorative `MudIcon` has `aria-hidden="true"`.
- **1.3.1 Info and Relationships** — headings follow hierarchy (no h2→h4 jumps). Tables use `<th scope="col">`. Form labels tied to inputs via `Label=` / `For=`.
- **1.3.2 Meaningful Sequence** — DOM order matches visual reading order. Flex/grid reordering doesn't scramble tab order.
- **1.4.1 Use of Color** — state is never conveyed by color alone. Status chips, validation indicators, and alerts include icon or text.
- **1.4.3 Contrast (Minimum)** — ≥ 4.5:1 body, ≥ 3:1 large text. Flag any custom theme color or `.razor.css` override.
- **1.4.10 Reflow** — content reflows at 320 px. Flag fixed widths in `.razor.css`.
- **1.4.11 Non-text Contrast** — UI component boundaries and state indicators meet 3:1.
- **1.4.13 Content on Hover or Focus** — hover popovers/tooltips are dismissible, hoverable, and persistent.

### Operable
- **2.1.1 Keyboard** — every mouse interaction has a keyboard equivalent. Canvas/drag-based UI needs arrow-key moves, +/- scale, Delete, Escape.
- **2.1.2 No Keyboard Trap** — custom focus management returns focus to trigger on close.
- **2.4.1 Bypass Blocks** — skip-to-main-content link in both `MainLayout` and `ShareLayout`.
- **2.4.2 Page Titled** — `<PageTitle>` set on every routable page.
- **2.4.3 Focus Order** — tab order is logical and matches visual order.
- **2.4.7 Focus Visible** — outlines not stripped without a `:focus-visible` replacement.
- **2.4.11 Focus Not Obscured** — focused element not hidden by sticky headers/toolbars/snackbars/dialogs.
- **2.5.7 Dragging Movements** — any drag-to-reorder/crop/move has a non-drag alternative.
- **2.5.8 Target Size (Minimum)** — interactive targets ≥ 24×24 CSS px.

### Understandable
- **3.1.1 Language of Page** — `App.razor` `<html lang>` bound to current culture, not hardcoded.
- **3.2.1 On Focus** — focusing a control never triggers a context change.
- **3.2.2 On Input** — changing a form value never submits or navigates without user action.
- **3.3.1 Error Identification** — errors identified in text, not color alone.
- **3.3.2 Labels or Instructions** — every input has a visible label.
- **3.3.3 Error Suggestion** — validation messages tell the user how to fix the problem.

### Robust
- **4.1.2 Name, Role, Value** — custom components expose name/role/value. `MudDialog` has an accessible name. Custom div-buttons have `role="button"` + `tabindex="0"` + keyboard handler.
- **4.1.3 Status Messages** — status messages inside `role="status" aria-live="polite"` or `role="alert"`.

## Report Format

Group findings by severity (Critical / Major / Minor), cite WCAG criterion, and give concrete fixes:

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
```

If more than 3 findings, end with a prioritized top-N fix list. Offer to apply safe fixes.

## Scope Notes
- Only audit files under `src/AssetHub.Ui/`.
- If a finding is a re-occurrence of something in the quality guardrails, flag it — the guardrail is being missed.
