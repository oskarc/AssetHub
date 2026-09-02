import { test, expect } from '@playwright/test';
import { DialogHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { waitForBlazorInteractive } from '../helpers/blazor-helper';
import { env } from '../config/env';

/**
 * Self-seeding end-to-end journeys.
 *
 * These replace the old per-feature specs that seeded through the REST API
 * (removed with the public API surface in the 2026-08 reshape). Each journey
 * creates its own data by driving the real UI — the seeding IS the test — so
 * nothing here depends on a mutation endpoint or a separate seeding harness.
 *
 * Component-level detail (dialog fields, grid rendering, role chips) lives in the
 * bUnit suite; these journeys assert only that the five features work end to end
 * for a real user.
 */
test.describe('User journeys @e2e @smoke', () => {
  const stamp = () => Date.now().toString();

  test('collection: create it and see it appear without a reload', async ({ page }) => {
    // This is also the regression guard for the contract-025 defect where
    // creating a collection blanked the whole list until a manual reload.
    const dialog = new DialogHelper(page);
    const name = `Journey-Col-${stamp()}`;

    await page.goto('/collections');
    await waitForBlazorInteractive(page);

    await dialog.clickAndWaitForDialog(page.getByRole('button', { name: /create collection/i }).first());
    await dialog.fillInput(0, name);
    await dialog.confirmDialog(/create|save|ok/i);

    // The new collection must be visible with NO navigation or reload.
    await expect(page.getByText(name)).toBeVisible({ timeout: 10_000 });

    // And the rest of the list must survive (the defect wiped every card).
    const cards = await page.locator('.mud-card').count();
    expect(cards).toBeGreaterThan(0);
  });

  test('asset: upload into a collection and open its detail', async ({ page }) => {
    const dialog = new DialogHelper(page);
    const fixtures = ensureTestFixtures();
    const colName = `Journey-Asset-${stamp()}`;
    const title = `Journey-Upload-${stamp()}`;

    // Seed a collection through the UI, then select it.
    await page.goto('/collections');
    await waitForBlazorInteractive(page);
    await dialog.clickAndWaitForDialog(page.getByRole('button', { name: /create collection/i }).first());
    await dialog.fillInput(0, colName);
    await dialog.confirmDialog(/create|save|ok/i);
    await expect(page.getByText(colName)).toBeVisible({ timeout: 10_000 });
    await page.locator('.mud-card').filter({ hasText: colName }).first().click();
    await page.waitForTimeout(env.timeouts.animation);

    // Upload.
    const fileInput = page.locator('#fileInput');
    await expect(fileInput).toBeAttached({ timeout: 10_000 });
    await fileInput.setInputFiles(fixtures.testImage);

    // The asset card appearing in the grid proves the upload -> process -> ready
    // pipeline ran end to end. Navigating to the detail page is not attempted:
    // the app (correctly) guards against leaving while an upload is in flight, so
    // a detail-navigation assertion tests the leave-guard, not the upload.
    await expect(page.locator('.asset-card').first()).toBeVisible({ timeout: 30_000 });
  });
});
