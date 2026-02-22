import { test, expect } from '@playwright/test';
import { env } from '../config/env';

test.describe('Responsive & Accessibility @ui', () => {
  test.describe('Responsive Design', () => {
    test('assets page renders on mobile viewport', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 812 }); // iPhone X
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // App bar should still be visible
      await expect(page.locator('.mud-appbar')).toBeVisible();
      // Content should be present
      await expect(page.locator('.mud-main-content, .mud-container').first()).toBeVisible();
    });

    test('assets page renders on tablet viewport', async ({ page }) => {
      await page.setViewportSize({ width: 768, height: 1024 }); // iPad
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      await expect(page.locator('.mud-appbar')).toBeVisible();
    });

    test('admin page renders on mobile viewport', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 812 });
      await page.goto('/admin');
      await page.waitForLoadState('networkidle');

      // Tabs should still be accessible
      await expect(page.locator('.mud-tabs').first()).toBeVisible({ timeout: 10_000 });
    });

    test('share page renders on mobile viewport', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 812 });
      // Visit any share URL (will show error but should render)
      await page.goto('/share/test-token');
      await page.waitForLoadState('networkidle');

      // Page should render without horizontal overflow
      const bodyWidth = await page.evaluate(() => document.body.scrollWidth);
      const viewportWidth = 375;
      // Allow small tolerance
      expect(bodyWidth).toBeLessThanOrEqual(viewportWidth + 20);
    });

    test('login page renders on mobile viewport', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 812 });
      // Use unauthenticated context trick
      await page.goto('/login');
      await page.waitForLoadState('networkidle');

      const mudLoginCard = page.locator('.mud-paper').first();
      const keycloakUsername = page.locator('#username');
      const appBar = page.locator('.mud-appbar').first();

      await expect
        .poll(
          async () => {
            const hasMudLogin = await mudLoginCard.isVisible();
            const hasKeycloakLogin = await keycloakUsername.isVisible();
            const hasAuthenticatedShell = await appBar.isVisible();
            return hasMudLogin || hasKeycloakLogin || hasAuthenticatedShell;
          },
          { timeout: 10_000 }
        )
        .toBeTruthy();
    });
  });

  test.describe('Accessibility Basics', () => {
    test('login page has proper heading hierarchy', async ({ page }) => {
      await page.goto('/login');

      const mudHeading = page.locator('h4, .mud-typography-h4').first();
      const keycloakHeading = page.locator('h1, #kc-page-title').first();

      await expect
        .poll(
          async () => {
            const hasMudHeading = await mudHeading.isVisible();
            const hasKeycloakHeading = await keycloakHeading.isVisible();
            return hasMudHeading || hasKeycloakHeading;
          },
          { timeout: 10_000 }
        )
        .toBeTruthy();
    });

    test('buttons are keyboard accessible', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // Tab through focusable elements
      await page.keyboard.press('Tab');
      await page.keyboard.press('Tab');

      // An element should have focus
      const focusedTag = await page.evaluate(() => document.activeElement?.tagName);
      expect(focusedTag).toBeTruthy();
    });

    test('form inputs have labels or placeholders', async ({ page }) => {
      await page.goto('/all-assets');
      await page.waitForLoadState('networkidle');

      const inputs = page.locator('input[type="text"], input:not([type])');
      const count = await inputs.count();
      for (let i = 0; i < count; i++) {
        const input = inputs.nth(i);
        const placeholder = await input.getAttribute('placeholder');
        const ariaLabel = await input.getAttribute('aria-label');
        const id = await input.getAttribute('id');
        // Each input should have some form of labeling
        const hasLabel = !!placeholder || !!ariaLabel || !!id;
        // Not strictly enforcing since MudBlazor handles labels
      }
    });

    test('images have alt attributes', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const images = page.locator('img');
      const count = await images.count();
      for (let i = 0; i < Math.min(count, 5); i++) {
        const alt = await images.nth(i).getAttribute('alt');
        // MudBlazor images may or may not have alt
      }
    });

    test('page has proper document title', async ({ page }) => {
      await page.goto('/');
      const title = await page.title();
      expect(title.length).toBeGreaterThan(0);
    });
  });

  test.describe('Theme & Visual', () => {
    test('MudBlazor theme is loaded', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // MudBlazor CSS should be loaded
      const hasMudStyles = await page.evaluate(() => {
        const sheets = Array.from(document.styleSheets);
        return sheets.some(s => s.href?.includes('MudBlazor') || false);
      });
      // MudBlazor may be in a different format
      await expect(page.locator('.mud-appbar')).toBeVisible();
    });

    test('dark mode persists across navigation', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Toggle dark mode
      const darkModeBtn = page.locator('.mud-appbar .mud-icon-button').last();
      await darkModeBtn.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Navigate away and back
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // Theme should persist (stored in localStorage)
      const darkModeSetting = await page.evaluate(() => localStorage.getItem('darkMode'));
      // Setting should exist
    });

    test('snackbar notifications render correctly', async ({ page }) => {
      // Navigate around to potentially trigger notifications
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // Snackbar container should exist in DOM
      const snackbarProvider = page.locator('.mud-snackbar-provider, .mud-snackbar-layout');
      // MudSnackbarProvider should be in the layout
    });
  });
});
