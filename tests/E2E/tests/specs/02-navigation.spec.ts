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
    // Get initial body background
    const initialBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    
    await layout.toggleDarkMode();
    await page.waitForTimeout(env.timeouts.animation);
    
    // Background should change
    const newBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(newBg).not.toBe(initialBg);
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
    // Get initial drawer state
    const initialVisible = await layout.drawer.isVisible();
    
    // Click menu toggle
    await layout.menuToggle.click();
    await page.waitForTimeout(env.timeouts.animation);
    
    // Drawer state should change
    const afterFirstClick = await layout.drawer.isVisible();
    expect(afterFirstClick).not.toBe(initialVisible);
    
    // Toggle again to restore
    await layout.menuToggle.click();
    await page.waitForTimeout(env.timeouts.animation);
    
    const afterSecondClick = await layout.drawer.isVisible();
    expect(afterSecondClick).toBe(initialVisible);
  });
});
