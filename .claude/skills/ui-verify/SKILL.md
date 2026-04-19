---
name: ui-verify
description: Smoke-test recently-changed Blazor UI against the existing Playwright E2E harness — map changed components to covering specs, flag gaps, generate spec/page-object stubs, and run targeted specs. Use after UI changes or before merging a UI-focused PR.
---

# AssetHub UI Verify

CLAUDE.md requires Blazor changes to be exercised in a browser, but the main-loop feedback is "does it build?" — not "does the form submit?". This skill closes that loop using the existing Playwright harness at `tests/E2E/` so UI changes get real coverage instead of faith-based sign-off.

## How to run

1. **Scope the change** — determine which Blazor components are in play:
   - `git diff --name-only HEAD` + `git diff --cached --name-only`, narrowed to `src/AssetHub.Ui/**/*.razor{,.cs,.css}`.
   - If args provided (e.g., a component name or page), use those.
   - If nothing matches, ask which component/page to verify.
2. **Map components to spec coverage** by reading existing specs and page objects:
   - `tests/E2E/tests/specs/*.spec.ts`
   - `tests/E2E/tests/pages/*.page.ts`
   - Build a mapping: component → (covering spec, page object, selectors used).
3. **Classify each changed component**:
   - **Covered** — an existing spec drives it meaningfully. Good.
   - **Partially covered** — spec opens the page but doesn't exercise the new control.
   - **Uncovered** — no spec drives this path.
4. **For uncovered / partial** — propose:
   - Which existing spec file to extend, **or** a new spec file name following the numbered pattern (`NN-<topic>.spec.ts`, preserving the existing sequence).
   - Which page object to extend or add under `tests/E2E/tests/pages/`.
   - The Given/When/Then lines for 1–3 smoke scenarios (happy path, validation failure, cancel).
5. **Generate stubs** when the user confirms:
   - Add locators + action methods to the page object (follow the Page Object pattern already in `admin.page.ts`).
   - Add a scenario to the relevant spec using existing fixtures and helpers (`tests/E2E/tests/fixtures/`, `tests/E2E/tests/helpers/`).
   - Never duplicate selectors — use the page object.
6. **Run targeted specs**:
   - Confirm prerequisites are up: dev stack (`https://assethub.local:7252`), Keycloak, Postgres, MinIO, RabbitMQ. If the user hasn't started them, report that and stop — don't try to start infra.
   - Verify `.env` provides `KEYCLOAK_CLIENT_SECRET`, `ADMIN_USERNAME`, `ADMIN_PASSWORD` (see `tests/E2E/tests/config/env.ts`). If missing, report and stop.
   - Run: `cd tests/E2E && npx playwright test <spec-filter> --project=chromium` (chromium first; expand if the change might affect layout).
7. **Report** what was verified, what was gap-filled, and what still needs human eyes (animations, pixel-level layout, interactions Playwright can't fake).

## Mapping rules

| Changed component class | Expected spec coverage |
|---|---|
| `Pages/Admin.razor` + a new `Admin*Tab.razor` | `06-admin.spec.ts` — extend with tab-visible + empty-state + create-flow scenarios |
| `Pages/Assets.razor` / `AssetCard.razor` / `AssetGrid.razor` | `04-assets.spec.ts` or `07-all-assets.spec.ts` |
| `Pages/Collections*.razor` / `CollectionCard.razor` | `03-collections.spec.ts` |
| Share-related pages / dialogs | `05-shares.spec.ts` |
| ACL / permission UI | `09-acl.spec.ts` |
| New dialog (`*Dialog.razor`) | The spec that opens it — drive submit with valid and invalid inputs |
| Layout changes (`MainLayout`, `ShareLayout`, `NavMenu`) | `02-navigation.spec.ts`, `12-responsive-a11y.spec.ts` |
| Localization-heavy change | `14-language.spec.ts` |
| Workflow across pages | `13-workflows.spec.ts` |

If the change doesn't fit any existing spec, default to creating a new numbered spec that groups related coverage (`16-metadata.spec.ts`, etc.).

## Stubs: what good coverage looks like

For a new admin dialog (e.g., `MetadataSchemaDialog.razor`), minimum viable coverage:

1. **Opens + renders** — click the tab, click "Create", assert the title and the required fields render.
2. **Validation fail** — leave required field empty, click Save, assert error message and that the dialog stays open.
3. **Happy path** — fill all required fields, click Save, assert the new row appears in the list and the dialog closes.
4. **Cancel with unsaved changes** — start typing, click Cancel, assert a confirm dialog appears (UX house rule).

Delete/edit flows should each get a scenario if the feature supports them.

## Guardrails

- **No auto-starting infra.** If the dev stack isn't up, report and stop — E2E is heavy and starting containers without user consent is a footgun.
- **No accepting `test.skip` / `test.fixme`** in generated stubs. If a scenario can't be written cleanly, report the blocker instead of shipping a skipped test.
- **Never hand-edit `playwright.config.ts`** or `global.setup.ts` from inside this skill — ask the user.
- **Chromium first.** Only expand to Firefox/WebKit if the change touches browser-specific rendering (canvas, CSS masks, drag behavior).
- **Don't add selectors to components for tests.** Prefer visible text + roles + labels. If the DOM is untestable as-is, propose a minimal `data-testid` rather than scraping CSS class names.

## Output

Report in this shape:

```
Changed components (3):
  src/AssetHub.Ui/Components/AdminMetadataSchemasTab.razor  → uncovered
  src/AssetHub.Ui/Components/MetadataSchemaDialog.razor     → uncovered
  src/AssetHub.Ui/Pages/Admin.razor                          → covered (06-admin.spec.ts)

Proposed coverage:
  06-admin.spec.ts — add 4 scenarios for Metadata Schemas tab
  tests/E2E/tests/pages/admin.page.ts — add MetadataSchemasTab sub-page-object

After generation:
  Ran: npx playwright test 06-admin.spec.ts --project=chromium
  Result: 12 passed, 0 failed, 1m 04s

Human-eyes items:
  - Verify the schema dialog's field-list reorder animation
  - Check Swedish label rendering at 320px viewport
```

## Abort conditions

- Dev stack not running — report the URLs that responded with errors, stop.
- Missing env vars — list them, stop.
- Playwright install missing (`npx playwright test` fails because browsers not installed) — suggest `npx playwright install chromium` and stop.
- The change is pure style / CSS with no behavioural surface — report "no E2E coverage needed, visual review instead" and stop.
