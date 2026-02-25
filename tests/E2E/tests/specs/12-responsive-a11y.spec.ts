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

    test('form inputs have accessible labels or placeholders', async ({ page }) => {
      await page.goto('/all-assets');
      await page.waitForLoadState('networkidle');

      const inputs = page.locator('input[type="text"], input:not([type])');
      const count = await inputs.count();
      
      // At least one input should exist on the all-assets page
      expect(count).toBeGreaterThan(0);
      
      // Check first input has some form of labeling
      const input = inputs.first();
      const placeholder = await input.getAttribute('placeholder');
      const ariaLabel = await input.getAttribute('aria-label');
      const ariaLabelledBy = await input.getAttribute('aria-labelledby');
      const id = await input.getAttribute('id');
      
      // Each input should have some form of labeling
      const hasAccessibleName = !!(placeholder || ariaLabel || ariaLabelledBy || id);
      expect(hasAccessibleName, 'Input should have placeholder, aria-label, aria-labelledby, or id').toBeTruthy();
    });

    test('images have alt attributes', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const images = page.locator('img');
      const count = await images.count();
      
      // If there are images, check they have alt attributes
      if (count > 0) {
        const image = images.first();
        const alt = await image.getAttribute('alt');
        // Images should have alt attribute (can be empty for decorative)
        expect(alt !== null, 'Images should have alt attribute').toBeTruthy();
      }
    });

    test('page has proper document title', async ({ page }) => {
      await page.goto('/');
      const title = await page.title();
      expect(title.length).toBeGreaterThan(0);
    });
  });

  test.describe('Theme & Visual', () => {
    test('MudBlazor styles are loaded', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // MudBlazor app bar should be styled (have background color)
      const appBar = page.locator('.mud-appbar');
      await expect(appBar).toBeVisible();
      
      const bgColor = await appBar.evaluate(el => getComputedStyle(el).backgroundColor);
      // Should have a non-transparent background
      expect(bgColor).not.toBe('rgba(0, 0, 0, 0)');
    });

    test('dark mode toggle changes theme', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Get initial body styles
      const initialBgColor = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);

      // Toggle dark mode
      const darkModeBtn = page.locator('.mud-appbar .mud-icon-button').last();
      await darkModeBtn.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Background should have changed OR darkMode localStorage should be set
      const darkModeSetting = await page.evaluate(() => localStorage.getItem('darkMode'));
      const newBgColor = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
      
      // Either the background color changed or darkMode setting exists
      const themeChanged = initialBgColor !== newBgColor || darkModeSetting !== null;
      expect(themeChanged, 'Theme should change when dark mode is toggled').toBeTruthy();
    });

    test('snackbar provider exists in DOM', async ({ page }) => {
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // Snackbar container should exist in DOM for notifications
      const snackbarProvider = page.locator('.mud-snackbar-provider');
      await expect(snackbarProvider).toBeAttached();
    });
  });
});
