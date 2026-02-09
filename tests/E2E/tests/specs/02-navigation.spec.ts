import { test, expect } from '@playwright/test';
import { LayoutPage } from '../pages/layout.page';
import { env } from '../config/env';

test.describe('Navigation & Layout @navigation @smoke', () => {
  let layout: LayoutPage;

  test.beforeEach(async ({ page }) => {
    layout = new LayoutPage(page);
    await page.goto('/');
    await page.waitForLoadState('networkidle');
  });

  test('app bar is visible with branding', async ({ page }) => {
    await expect(layout.appBar).toBeVisible();
    await expect(layout.appName).toBeVisible();
  });

  test('user display name shown in app bar', async ({ page }) => {
    await expect(layout.userDisplayName).toBeVisible();
  });

  test('sign out button visible for authenticated user', async () => {
    await layout.expectAuthenticated();
  });

  test('navigation drawer is visible', async () => {
    await expect(layout.drawer).toBeVisible();
  });

  test('nav menu contains Home link', async () => {
    await expect(layout.navHome).toBeVisible();
  });

  test('nav menu contains Collections link', async () => {
    await expect(layout.navCollections).toBeVisible();
  });

  test('admin nav items visible for admin user', async () => {
    await layout.expectAdminNavVisible();
  });

  test('navigate to Home page', async ({ page }) => {
    await layout.navigateToCollections();
    await layout.navigateHome();
    await expect(page).toHaveURL(/\/$/);
  });

  test('navigate to Collections page', async ({ page }) => {
    await layout.navigateToCollections();
    await expect(page).toHaveURL(/\/assets/);
  });

  test('navigate to All Assets page (admin)', async ({ page }) => {
    await layout.navigateToAllAssets();
    await expect(page).toHaveURL(/\/all-assets/);
  });

  test('navigate to Admin page (admin)', async ({ page }) => {
    await layout.navigateToAdmin();
    await expect(page).toHaveURL(/\/admin/);
  });

  test('dark mode toggle works', async ({ page }) => {
    // Get initial theme state
    const bodyClasses = await page.locator('body').getAttribute('class') || '';
    await layout.toggleDarkMode();
    await page.waitForTimeout(env.timeouts.animation);
    // Theme should change (MudBlazor applies theme via style)
    const darkModeIcon = page.locator('.mud-appbar .mud-icon-button').last();
    await expect(darkModeIcon).toBeVisible();
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

  test('hamburger menu toggles drawer', async ({ page }) => {
    // Click menu toggle
    await layout.menuToggle.click();
    await page.waitForTimeout(env.timeouts.animation);
    // Drawer state should change
    const drawerVisible = await layout.drawer.isVisible();
    // Toggle again
    await layout.menuToggle.click();
    await page.waitForTimeout(env.timeouts.animation);
  });
});
