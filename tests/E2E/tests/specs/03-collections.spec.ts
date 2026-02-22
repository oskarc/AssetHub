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

  test('select a collection and view assets', async ({ page }) => {
    // Wait for collections to load
    await page.waitForTimeout(env.timeouts.animation);

    // Click on first collection card
    const firstCollection = page.locator('.mud-card').first();
    if (await firstCollection.isVisible()) {
      await firstCollection.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Search/filter bar should now be visible
      await expect(page.locator('input.mud-input-root').first()).toBeVisible();
    }
  });

  test('create a sub-collection', async ({ page }) => {
    // First select a parent collection
    const parentCollection = page.getByText(testCollectionName);
    if (await parentCollection.isVisible()) {
      await parentCollection.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Find create sub-collection button (often in the action area)
      const createSubBtn = page.getByRole('button', { name: /create/i }).first();
      if (await createSubBtn.isVisible()) {
        await createSubBtn.click();
        await dialog.waitForDialog();
        await dialog.fillInput(0, subCollectionName);
        await dialog.confirmDialog(/create|save|ok/i);
        await page.waitForTimeout(env.timeouts.animation);
      }
    }
  });

  test('rename a collection via context menu', async ({ page }) => {
    // Find collection in tree
    const collectionItem = page.locator('.collection-item').filter({ hasText: testCollectionName }).first();
    if (await collectionItem.isVisible()) {
      // Click context menu (more icon)
      const contextMenu = collectionItem.locator('.mud-menu button, .mud-icon-button').last();
      await contextMenu.click();
      await page.waitForTimeout(500);

      // Click rename
      const renameOption = page.getByText(/rename/i).first();
      if (await renameOption.isVisible()) {
        await renameOption.click();
        await dialog.waitForDialog();
        const renamedName = `${testCollectionName}-Renamed`;
        await dialog.fillInput(0, renamedName);
        await dialog.confirmDialog(/save|rename|ok/i);
        await page.waitForTimeout(env.timeouts.animation);
      }
    }
  });

  test('deselect collection shows empty state', async ({ page }) => {
    // Select a collection first
    const firstCol = page.locator('.mud-card').first();
    if (await firstCol.isVisible()) {
      await firstCol.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Click deselect/close
      const deselectBtn = page.locator('[title*="lose"], .mud-icon-button').filter({ has: page.locator('svg') }).first();
      if (await deselectBtn.isVisible()) {
        await deselectBtn.click();
        await page.waitForTimeout(env.timeouts.animation);
      }
    }
  });

  test('manage access dialog opens for manager+ role', async ({ page }) => {
    // Select a collection
    const firstCol = page.locator('.mud-card').first();
    if (await firstCol.isVisible()) {
      await firstCol.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Click manage access
      if (await assetsPage.manageAccessButton.isVisible()) {
        await assetsPage.manageAccessButton.click();
        await dialog.waitForDialog();
        // Dialog should have user search
        await expect(dialog.dialog.locator('input').first()).toBeVisible();
        await dialog.closeDialog();
      }
    }
  });

  test('collection breadcrumbs show correct hierarchy', async ({ page }) => {
    const firstCol = page.locator('.mud-card').first();
    if (await firstCol.isVisible()) {
      // Extract just the collection name (subtitle2 typography, not the full card text)
      const nameEl = firstCol.locator('.mud-typography-subtitle2').first()
        .or(firstCol.locator('.mud-card-content .text-truncate').first());
      const colName = (await nameEl.textContent())?.trim();
      await firstCol.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Breadcrumbs should contain collection name
      const breadcrumbs = page.locator('.mud-breadcrumbs');
      if (await breadcrumbs.isVisible()) {
        await expect(breadcrumbs).toContainText(colName?.trim() || '');
      }
    }
  });
});
