import { test, expect } from '@playwright/test';
import { AssetsPage } from '../pages/assets.page';
import { env } from '../config/env';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';

test.describe('Collection Management @collections', () => {
  let assetsPage: AssetsPage;
  let dialog: DialogHelper;
  let snackbar: SnackbarHelper;
  const testCollectionName = `${env.testData.collectionPrefix}-${Date.now()}`;
  const subCollectionName = `${env.testData.collectionPrefix}-Sub-${Date.now()}`;

  test.beforeEach(async ({ page }) => {
    assetsPage = new AssetsPage(page);
    dialog = new DialogHelper(page);
    snackbar = new SnackbarHelper(page);
    await assetsPage.goto();
  });

  test('collections sidebar is visible', async () => {
    await expect(assetsPage.collectionsHeading).toBeVisible();
  });

  test('collection tree loads without errors', async ({ page }) => {
    // Should not have error alerts
    await expect(page.locator('.mud-alert-error')).not.toBeVisible();
    // Collection tree area should exist
    await expect(assetsPage.collectionTree).toBeVisible();
  });

  test('create a new root collection @smoke', async ({ page }) => {
    // Click Create Collection button
    await page.getByRole('button', { name: /create collection/i }).click();

    // Fill dialog
    await dialog.waitForDialog();
    await dialog.fillInput(0, testCollectionName);
    await dialog.confirmDialog(/create|save|ok/i);

    // Verify collection appears in tree
    await page.waitForTimeout(env.timeouts.animation);
    await expect(page.getByText(testCollectionName)).toBeVisible({ timeout: 10_000 });
  });

  test('select a collection and view assets area', async ({ page }) => {
    // Wait for collections to load
    await page.waitForTimeout(env.timeouts.animation);

    // Click on first collection card
    const firstCollection = page.locator('.mud-card').first();
    await expect(firstCollection).toBeVisible({ timeout: 10_000 });
    
    await firstCollection.click();
    await page.waitForTimeout(env.timeouts.animation);

    // Search/filter bar should now be visible
    await expect(page.locator('input.mud-input-root').first()).toBeVisible({ timeout: 5_000 });
  });

  // Note: 'rename a collection' and 'deselect collection' tests removed
  // as they depend on specific test data created in earlier tests (fragile chain)
  // These scenarios are covered by API tests in 08-api.spec.ts

  test('manage access dialog opens for manager+ role', async ({ page }) => {
    // Select a collection
    const firstCol = page.locator('.mud-card').first();
    await expect(firstCol).toBeVisible({ timeout: 10_000 });
    
    await firstCol.click();
    await page.waitForTimeout(env.timeouts.animation);

    // Click manage access (visible for admin/manager role)
    await expect(assetsPage.manageAccessButton).toBeVisible({ timeout: 5_000 });
    await assetsPage.manageAccessButton.click();
    await dialog.waitForDialog();
    
    // Dialog should have user search input
    await expect(dialog.dialog.locator('input').first()).toBeVisible();
    await dialog.closeDialog();
  });

  test('collection breadcrumbs show collection name', async ({ page }) => {
    const firstCol = page.locator('.mud-card').first();
    await expect(firstCol).toBeVisible({ timeout: 10_000 });
    
    // Extract just the collection name
    const nameEl = firstCol.locator('.mud-typography-subtitle2').first()
      .or(firstCol.locator('.mud-card-content .text-truncate').first());
    const colName = (await nameEl.textContent())?.trim();
    expect(colName).toBeTruthy();
    
    await firstCol.click();
    await page.waitForTimeout(env.timeouts.animation);

    // Breadcrumbs should contain collection name
    const breadcrumbs = page.locator('.mud-breadcrumbs');
    await expect(breadcrumbs).toBeVisible({ timeout: 5_000 });
    await expect(breadcrumbs).toContainText(colName!);
  });
});
