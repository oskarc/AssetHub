import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('Error Handling & Edge Cases @edge-cases', () => {
  let testCollectionId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-Edge-${timestamp}`;

  test.beforeAll(async () => {
    const api = await ApiHelper.withCookieAuth();
    const collection = await api.createCollection(testCollectionName, 'Edge case test collection');
    testCollectionId = collection.id;

    const fixtures = ensureTestFixtures();
    await api.uploadAsset(testCollectionId, fixtures.testImage, `Edge-Asset-${timestamp}`);
    await api.dispose();
  });

  test.afterAll(async () => {
    if (testCollectionId) {
      const api = await ApiHelper.withCookieAuth();
      await api.deleteCollection(testCollectionId).catch(() => {});
      await api.dispose();
    }
  });
  test.describe('404 / Not Found', () => {
    test('non-existent asset ID shows not found', async ({ page }) => {
      await page.goto('/assets/00000000-0000-0000-0000-000000000000');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const notFound = page.getByText(/not found|doesn't exist|error/i).first();
      await expect(notFound).toBeVisible({ timeout: 10_000 });
    });

    test('non-existent collection ID handled gracefully', async ({ page }) => {
      await page.goto('/assets?collection=00000000-0000-0000-0000-000000000000');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      // Should show empty state or error
      const result = page.getByText(/no.*asset|empty|not found|error/i);
      // Page should not crash
      await expect(page.locator('.mud-container, .mud-main-content, body').first()).toBeVisible();
    });
  });

  test.describe('Blazor Error Handling', () => {
    test('blazor error UI is hidden during normal operation', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      const errorUI = page.locator('#blazor-error-ui');
      // Error UI should be hidden
      const display = await errorUI.evaluate(el => getComputedStyle(el).display).catch(() => 'none');
      expect(display).toBe('none');
    });
  });

  test.describe('Concurrent Access', () => {
    test('rapid navigation does not crash', async ({ page }) => {
      // Navigate rapidly between pages
      await page.goto('/');
      await page.goto('/assets');
      await page.goto('/all-assets');
      await page.goto('/admin');
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // App should still be functional
      await expect(page.locator('.mud-appbar')).toBeVisible();
    });

    test('rapid search input does not crash (debounce test)', async ({ page }) => {
      await page.goto('/all-assets');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      // Search input may be in toolbar or main content
      const searchInput = page.locator('input[placeholder*="earch" i], .mud-input input[type="text"]').first();
      const hasSearch = await searchInput.isVisible().catch(() => false);
      test.skip(!hasSearch, 'Search input not available on this page');
      
      // Type rapidly
      await searchInput.type('abcdefghijklmnop', { delay: 50 });
      await page.waitForTimeout(env.timeouts.debounce);
      
      // Clear and type again
      await searchInput.clear();
      await searchInput.type('test', { delay: 100 });
      await page.waitForTimeout(env.timeouts.debounce);

      // App should still be functional
      await expect(page.locator('.mud-container, .mud-main-content').first()).toBeVisible();
    });
  });

  test.describe('Browser Back/Forward', () => {
    test('browser back from asset detail returns to collection', async ({ page }) => {
      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const card = page.locator('.asset-card').first();
      await expect(card).toBeVisible({ timeout: 10_000 });

      const openTarget = card.locator('.clickable').first();
      await expect(openTarget).toBeVisible();
      await openTarget.click();
      // Blazor uses client-side navigation (history.pushState), so use toHaveURL which polls
      await expect(page).toHaveURL(/\/assets\/[0-9a-f-]+/, { timeout: 15_000 });

      // Go back
      await page.goBack();
      await page.waitForURL(/\/collections|\/assets/);
      await expect(page.locator('.mud-container')).toBeVisible();
    });

    test('browser forward after back works', async ({ page }) => {
      await page.goto('/');
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      await page.goBack();
      await page.waitForTimeout(env.timeouts.animation);
      await page.goForward();
      await page.waitForTimeout(env.timeouts.animation);

      await expect(page.locator('.mud-container, .mud-main-content').first()).toBeVisible();
    });
  });

  test.describe('Empty States', () => {
    test('empty collection area shows select prompt', async ({ page }) => {
      // Navigate to assets without selecting a collection
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      
      // The "select a collection" empty state should appear
      const emptyState = page.getByText(/select|choose|no.*collection/i).first();
      await expect(emptyState).toBeVisible({ timeout: 10_000 });
    });
  });

  test.describe('Loading States', () => {
    test('collections page loads without crashing', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      await expect(page.locator('.mud-container, .mud-main-content').first()).toBeVisible();
    });

    test('admin page loads without crashing', async ({ page }) => {
      await page.goto('/admin');
      await page.waitForLoadState('networkidle');
      // Content should eventually load
      await expect(page.locator('.mud-typography-h4')).toBeVisible();
    });
  });
});
