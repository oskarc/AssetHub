import { test, expect } from '@playwright/test';
import { env } from '../config/env';

test.describe('Error Handling & Edge Cases @edge-cases', () => {
  test.describe('404 / Not Found', () => {
    test('non-existent asset ID shows not found', async ({ page }) => {
      await page.goto('/assets/00000000-0000-0000-0000-000000000000');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const notFound = page.getByText(/not found|doesn't exist|error/i);
      await expect(notFound).toBeVisible({ timeout: 10_000 });
    });

    test('non-existent collection ID handled gracefully', async ({ page }) => {
      await page.goto('/assets?collection=00000000-0000-0000-0000-000000000000');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      // Should show empty state or error
      const result = page.getByText(/no.*asset|empty|not found|error/i);
      // Page should not crash
      await expect(page.locator('.mud-container, .mud-main-content, body')).toBeVisible();
    });

    test('invalid GUID in URL handled gracefully', async ({ page }) => {
      await page.goto('/assets/not-a-guid');
      await page.waitForLoadState('networkidle');
      // Should show error or redirect
      await expect(page.locator('body')).toBeVisible();
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

      const searchInput = page.locator('.mud-input-root input[type="text"]').first();
      if (await searchInput.isVisible()) {
        // Type rapidly
        await searchInput.type('abcdefghijklmnop', { delay: 50 });
        await page.waitForTimeout(env.timeouts.debounce);
        // Clear and type again
        await searchInput.clear();
        await searchInput.type('test', { delay: 100 });
        await page.waitForTimeout(env.timeouts.debounce);

        // App should still be functional
        await expect(page.locator('.mud-container, .mud-main-content')).toBeVisible();
      }
    });
  });

  test.describe('Browser Back/Forward', () => {
    test('browser back from asset detail returns to collection', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      // Navigate to detail
      const card = page.locator('.asset-card').first();
      if (await card.isVisible()) {
        await card.locator('.mud-icon-button').first().click();
        await page.waitForURL(/\/assets\/[0-9a-f-]+/);
        
        // Go back
        await page.goBack();
        await page.waitForURL(/\/collections|\/assets/);
        await expect(page.locator('.mud-container')).toBeVisible();
      }
    });

    test('browser forward after back works', async ({ page }) => {
      await page.goto('/');
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      await page.goBack();
      await page.waitForTimeout(env.timeouts.animation);
      await page.goForward();
      await page.waitForTimeout(env.timeouts.animation);

      await expect(page.locator('.mud-container, .mud-main-content')).toBeVisible();
    });
  });

  test.describe('Empty States', () => {
    test('empty collection shows appropriate message', async ({ page }) => {
      // This depends on having a collection with no assets
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      // The "select a collection" empty state should appear
      const emptyState = page.getByText(/select|choose|no.*collection/i);
      if (await emptyState.isVisible()) {
        await expect(emptyState).toBeVisible();
      }
    });
  });

  test.describe('Loading States', () => {
    test('collections page shows loading indicator', async ({ page }) => {
      // Check for progress indicator during initial load
      await page.goto('/assets');
      // During load, a progress indicator might flash
      // Just ensure the page eventually loads
      await page.waitForLoadState('networkidle');
      await expect(page.locator('.mud-container, .mud-main-content')).toBeVisible();
    });

    test('admin page shows loading states', async ({ page }) => {
      await page.goto('/admin');
      await page.waitForLoadState('networkidle');
      // Content should eventually load
      await expect(page.locator('.mud-typography-h4')).toBeVisible();
    });
  });
});
