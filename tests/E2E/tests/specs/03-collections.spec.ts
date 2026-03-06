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
    const createBtn = page.getByRole('button', { name: /create collection/i });
    await dialog.clickAndWaitForDialog(createBtn);
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
    // Find a collection card with Manager or Admin role (displayed in the chip)
    const collectionCards = page.locator('.mud-card');
    await expect(collectionCards.first()).toBeVisible({ timeout: 10_000 });
    
    // Look for a card with Manager or Administrator role chip
    const managerCard = collectionCards.filter({ hasText: /Manager|Administrator/i }).first();
    
    // If no manager+ collection exists, skip this test
    const hasManagerCard = await managerCard.count() > 0;
    if (!hasManagerCard) {
      test.skip(true, 'No collection with Manager/Admin role found for test user');
      return;
    }
    
    await managerCard.click();
    
    // Wait for the Manage Access button to appear (proves collection was selected with proper role)
    await expect(assetsPage.manageAccessButton).toBeVisible({ timeout: 15_000 });
    
    // Open manage access dialog
    await dialog.clickAndWaitForDialog(assetsPage.manageAccessButton);
    
    // Dialog should have user search input
    await expect(dialog.dialog.locator('input').first()).toBeVisible();
    await dialog.closeDialog();
  });

  test('collection breadcrumbs show collection name', async ({ page }) => {
    const firstCol = page.locator('.mud-card').first();
    await expect(firstCol).toBeVisible({ timeout: 10_000 });
    
    // Extract just the collection name from the card - look for the paragraph with collection name
    const nameEl = firstCol.locator('p').first()
      .or(firstCol.locator('.mud-typography-subtitle2').first());
    const colName = (await nameEl.textContent())?.trim();
    expect(colName).toBeTruthy();
    
    // Click the collection card and wait for breadcrumbs to update
    await firstCol.click();
    
    // Wait for breadcrumbs to contain the collection name (proves click worked)
    const breadcrumbs = page.locator('.mud-breadcrumbs');
    await expect(breadcrumbs).toContainText(colName!, { timeout: 15_000 });
  });
});
