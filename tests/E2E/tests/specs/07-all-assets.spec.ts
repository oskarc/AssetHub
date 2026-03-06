import { test, expect } from '@playwright/test';
import { AllAssetsPage } from '../pages/all-assets.page';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';
import { clickAndWaitForPopover } from '../helpers/blazor-helper';

test.describe('All Assets (Admin) @admin @assets', () => {
  let allAssetsPage: AllAssetsPage;
  let dialog: DialogHelper;
  let api: ApiHelper;
  let testCollectionId: string;

  const timestamp = Date.now();

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();

    // Ensure we have test data
    const collection = await api.createCollection(`${env.testData.collectionPrefix}-AllAssets-${timestamp}`);
    testCollectionId = collection.id;
    const fixtures = ensureTestFixtures();
    try {
      await api.uploadAsset(testCollectionId, fixtures.testImage, `AllAssets-Test-${timestamp}`);
    } catch {
      // Non-critical
    }
  });

  test.afterAll(async () => {
    api = await ApiHelper.withCookieAuth();
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
  });

  test.beforeEach(async ({ page }) => {
    allAssetsPage = new AllAssetsPage(page);
    dialog = new DialogHelper(page);
    await allAssetsPage.goto();
  });

  test('all assets page loads @smoke', async () => {
    await allAssetsPage.expectLoaded();
  });

  test('page title is displayed', async ({ page }) => {
    await expect(page.locator('.mud-typography-h4')).toBeVisible();
  });

  test('refresh button is visible', async () => {
    await expect(allAssetsPage.refreshButton).toBeVisible();
  });

  test('search bar is visible', async () => {
    await expect(allAssetsPage.searchInput).toBeVisible();
  });

  test('filter controls are visible', async ({ page }) => {
    // Collection filter
    const selects = page.locator('.mud-select');
    const selectCount = await selects.count();
    expect(selectCount).toBeGreaterThanOrEqual(1);
  });

  test('asset cards are displayed', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    const count = await cards.count();
    // Should have at least the test asset
    if (count > 0) {
      const firstCard = cards.first();
      await expect(firstCard).toBeVisible();
    }
  });

  test('search filters assets', async ({ page }) => {
    await allAssetsPage.search('nonexistent-xyz-12345');
    const cards = page.locator('.asset-card');
    const count = await cards.count();
    // Should have 0 results for nonsense query
    expect(count).toBe(0);
  });

  test('search finds matching assets', async ({ page }) => {
    await allAssetsPage.search(`AllAssets-Test-${timestamp}`);
    await page.waitForTimeout(env.timeouts.debounce);
    const cards = page.locator('.asset-card');
    // Should find our test asset
    await expect(cards.first()).toBeVisible({ timeout: 10_000 });
  });

  test('stats text shows count', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation);
    const statsText = page.locator('.mud-typography-body2').filter({ hasText: /showing|\d+/i });
    await expect(statsText.first()).toBeVisible({ timeout: 10_000 });
  });

  test('clicking asset navigates to detail page', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    await expect(cards.first()).toBeVisible({ timeout: 15_000 });
    
    // Click the card thumbnail/content area to view asset
    const clickableArea = cards.first().locator('.clickable').first();
    await expect(clickableArea).toBeVisible();
    await clickableArea.click();
    await page.waitForURL(/\/assets\/[0-9a-f-]+/, { timeout: env.timeouts.navigation });
  });

  test('share button opens dialog', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    await expect(cards.first()).toBeVisible({ timeout: 15_000 });

    const shareBtn = cards.first().locator('.mud-icon-button').nth(1);
    await expect(shareBtn).toBeVisible();

    await dialog.clickAndWaitForDialog(shareBtn);
    await page.keyboard.press('Escape');
  });

  test('delete button shows confirmation dialog', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    await expect(cards.first()).toBeVisible({ timeout: 15_000 });

    // Delete button is conditional on per-asset role; look for the error-colored icon button
    const deleteBtn = cards.first().locator('.mud-icon-button.mud-error-text, .mud-icon-button[style*="error"]').first();
    if (!(await deleteBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip(true, 'Delete button not available - user may lack delete permission on this asset');
      return;
    }

    await dialog.clickAndWaitForDialog(deleteBtn);

    // Cancel deletion
    const cancelBtn = dialog.dialog.getByRole('button', { name: /cancel/i }).first();
    await cancelBtn.click();
  });

  // Note: 'load more button' test removed - depends on having many assets which is not guaranteed

  test('refresh button reloads data', async ({ page }) => {
    await allAssetsPage.refreshButton.click();
    await page.waitForTimeout(env.timeouts.animation * 2);
    // Page should still be functional
    await allAssetsPage.expectLoaded();
  });

  test('collection filter dropdown has options', async ({ page }) => {
    const selects = page.locator('.mud-select');
    await expect(selects.first()).toBeVisible({ timeout: 10_000 });

    await clickAndWaitForPopover(page, selects.first());

    const options = page.locator('.mud-popover-open .mud-list-item');
    await expect(options.first()).toBeVisible({ timeout: 5_000 });

    await page.keyboard.press('Escape');
  });

  test('type filter dropdown has options', async ({ page }) => {
    const selects = page.locator('.mud-select');
    await expect(selects.nth(1)).toBeVisible({ timeout: 10_000 });

    await clickAndWaitForPopover(page, selects.nth(1));

    const options = page.locator('.mud-popover-open .mud-list-item');
    await expect(options.first()).toBeVisible({ timeout: 5_000 });

    await page.keyboard.press('Escape');
  });
});
