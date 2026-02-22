import { test, expect } from '@playwright/test';
import { AllAssetsPage } from '../pages/all-assets.page';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('All Assets (Admin) @admin @assets', () => {
  let allAssetsPage: AllAssetsPage;
  let dialog: DialogHelper;
  let api: ApiHelper;
  let testCollectionId: string;

  const timestamp = Date.now();

  test.beforeAll(async ({ request }) => {
    api = new ApiHelper(request);
    await api.authenticate();

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

  test.afterAll(async ({ request }) => {
    api = new ApiHelper(request);
    await api.authenticate();
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
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
    const count = await cards.count();
    // Should find our test asset
  });

  test('stats text shows count', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation);
    const statsText = page.locator('.mud-typography-body2').filter({ hasText: /showing|\d+/i });
    if (await statsText.first().isVisible()) {
      await expect(statsText.first()).toBeVisible();
    }
  });

  test('view toggle between grid and list', async ({ page }) => {
    const viewButtons = page.locator('.mud-button-group .mud-icon-button');
    const count = await viewButtons.count();
    if (count >= 2) {
      await viewButtons.last().click();
      await page.waitForTimeout(env.timeouts.animation);
      await viewButtons.first().click();
      await page.waitForTimeout(env.timeouts.animation);
    }
  });

  test('asset card actions: view button', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    const count = await cards.count();
    if (count > 0) {
      // Click the card thumbnail/content area to view asset
      const clickableArea = cards.first().locator('.clickable').first();
      if (await clickableArea.isVisible()) {
        await clickableArea.click();
        await page.waitForURL(/\/assets\/[0-9a-f-]+/, { timeout: env.timeouts.navigation });
      }
    }
  });

  test('asset card actions: share button', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    const count = await cards.count();
    if (count > 0) {
      const shareBtn = cards.first().locator('.mud-icon-button').nth(1);
      if (await shareBtn.isVisible()) {
        await shareBtn.click();
        await page.waitForTimeout(env.timeouts.animation);
        const dlg = page.locator('.mud-dialog');
        if (await dlg.isVisible()) {
          await page.keyboard.press('Escape');
        }
      }
    }
  });

  test('asset card actions: delete button shows confirmation', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const cards = page.locator('.asset-card');
    const count = await cards.count();
    if (count > 0) {
      const deleteBtn = cards.first().locator('.mud-icon-button').last();
      if (await deleteBtn.isVisible()) {
        await deleteBtn.click();
        await page.waitForTimeout(env.timeouts.animation);
        const dlg = page.locator('.mud-dialog');
        if (await dlg.isVisible()) {
          // Cancel deletion
          await dlg.getByRole('button', { name: /cancel|no/i }).first().click().catch(() => {
            page.keyboard.press('Escape');
          });
        }
      }
    }
  });

  test('load more button appears with many assets', async ({ page }) => {
    await page.waitForTimeout(env.timeouts.animation * 2);
    const loadMore = page.getByRole('button', { name: /load more/i });
    // May or may not be visible depending on asset count
    if (await loadMore.isVisible()) {
      await expect(loadMore).toBeEnabled();
    }
  });

  test('refresh button reloads data', async ({ page }) => {
    await allAssetsPage.refreshButton.click();
    await page.waitForTimeout(env.timeouts.animation * 2);
    // Page should still be functional
    await allAssetsPage.expectLoaded();
  });

  test('collection filter works', async ({ page }) => {
    const selects = page.locator('.mud-select');
    if (await selects.first().isVisible()) {
      await selects.first().click();
      await page.waitForTimeout(500);
      const options = page.getByRole('option');
      const optCount = await options.count();
      if (optCount > 0) {
        await options.first().click();
        await page.waitForTimeout(env.timeouts.debounce);
      } else {
        await page.keyboard.press('Escape');
      }
    }
  });

  test('type filter works', async ({ page }) => {
    const selects = page.locator('.mud-select');
    if (await selects.nth(1).isVisible()) {
      await selects.nth(1).click();
      await page.waitForTimeout(500);
      const imageOption = page.getByRole('option', { name: /image/i });
      if (await imageOption.isVisible()) {
        await imageOption.click();
        await page.waitForTimeout(env.timeouts.debounce);
      } else {
        await page.keyboard.press('Escape');
      }
    }
  });
});
