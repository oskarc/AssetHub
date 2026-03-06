import { test, expect } from '@playwright/test';
import { LayoutPage } from '../pages/layout.page';
import { env } from '../config/env';
import { waitForBlazorInteractive } from '../helpers/blazor-helper';

test.describe('Navigation & Layout @navigation @smoke', () => {
  let layout: LayoutPage;

  test.beforeEach(async ({ page }) => {
    layout = new LayoutPage(page);
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await waitForBlazorInteractive(page);
  });

  test('app bar displays branding and user info', async ({ page }) => {
    await expect(layout.appBar).toBeVisible();
    await expect(layout.appName).toBeVisible();
    await expect(layout.userDisplayName).toBeVisible();
  });

  test('navigation drawer contains expected links', async () => {
    await expect(layout.drawer).toBeVisible();
    await expect(layout.navHome).toBeVisible();
    await expect(layout.navCollections).toBeVisible();
  });

  test('navigate to Home page', async ({ page }) => {
    await layout.navigateToCollections();
    await layout.navigateHome();
    await expect(page).toHaveURL(/\/$/);
  });

  test('navigate to Collections page', async ({ page }) => {
    await layout.navigateToCollections();
    await expect(page).toHaveURL(/\/collections/);
  });

  test('navigate to All Assets page (admin)', async ({ page }) => {
    await layout.navigateToAllAssets();
    await expect(page).toHaveURL(/\/all-assets/);
  });

  test('navigate to Admin page (admin)', async ({ page }) => {
    await layout.navigateToAdmin();
    await expect(page).toHaveURL(/\/admin/);
  });

  test('dark mode toggle changes theme', async ({ page }) => {
    // Clear any pre-existing value so we can detect the toggle write
    await page.evaluate(() => localStorage.removeItem('darkMode'));

    // Retry until Blazor interactivity is ready and the toggle actually fires
    await expect(async () => {
      await layout.toggleDarkMode();
      await page.waitForTimeout(500);
      const setting = await page.evaluate(() => localStorage.getItem('darkMode'));
      expect(setting).not.toBeNull();
    }).toPass({ timeout: 15_000 });
  });

  test('direct URL navigation to /assets works', async ({ page }) => {
    await page.goto('/assets');
    await page.waitForLoadState('networkidle');
    await expect(page.getByText(/collections/i).first()).toBeVisible();
  });

  test('direct URL navigation to /admin works for admin', async ({ page }) => {
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.mud-typography-h4')).toBeVisible();
  });

  test('direct URL navigation to /all-assets works for admin', async ({ page }) => {
    await page.goto('/all-assets');
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.mud-typography-h4')).toBeVisible();
  });

  test('hamburger menu toggles drawer visibility', async ({ page }) => {
    // MudBlazor keeps the drawer in the DOM; detect state via class that contains "open"
    const isDrawerOpen = async () => {
      return await layout.drawer.evaluate(el =>
        // MudBlazor uses mud-drawer--open or mud-drawer-open depending on version
        [...el.classList].some(c => c.includes('drawer') && c.includes('open'))
      );
    };

    // Initially drawer should be open (nav menu visible)
    expect(await isDrawerOpen()).toBeTruthy();

    await expect(layout.menuToggle).toBeVisible();
    await expect(layout.menuToggle).toBeEnabled();

    // Click hamburger to close drawer, then wait for the class to actually change
    await layout.menuToggle.click();
    await expect(async () => {
      expect(await isDrawerOpen()).toBeFalsy();
    }).toPass({ timeout: 10_000 });

    // Toggle again to restore
    await layout.menuToggle.click();
    await expect(async () => {
      expect(await isDrawerOpen()).toBeTruthy();
    }).toPass({ timeout: 10_000 });
  });
});
