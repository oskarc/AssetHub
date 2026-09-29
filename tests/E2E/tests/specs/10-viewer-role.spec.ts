import { test, expect } from '@playwright/test';
import { IdentityLoginPage } from '../pages/identity-login.page';
import { LayoutPage } from '../pages/layout.page';
import { ErrorPagesPage } from '../pages/error-pages.page';
import { env } from '../config/env';

test.describe('Viewer Role Restrictions @acl @auth', () => {
  test.use({ storageState: { cookies: [], origins: [] } }); // Start unauthenticated

  let layout: LayoutPage;

  test.beforeEach(async ({ page }) => {
    layout = new LayoutPage(page);
    const login = new IdentityLoginPage(page);
    await login.loginAsViewer();
    await page.waitForLoadState('networkidle');
  });

  test('viewer cannot see admin nav items', async ({ page }) => {
    const allAssets = page.locator('.mud-navmenu').getByText(/all assets/i);
    const admin = page.locator('.mud-navmenu').getByText(/admin/i);

    // These should NOT be visible for a viewer
    await expect(allAssets).not.toBeVisible();
    await expect(admin).not.toBeVisible();
  });

  // Both used to pass with the bug in place: the forbid landed on
  // /login?ReturnUrl=%2Fadmin, and because the slash is percent-encoded the URL
  // did not contain '/admin', so "redirected away" was true for the sign-in form.
  // They now pin the hop itself and what the viewer actually sees.
  for (const path of ['/admin', '/all-assets']) {
    test(`viewer opening ${path} gets the access-denied page, not the sign-in form`, async ({ page }) => {
      const errorPages = new ErrorPagesPage(page);
      const deniedUrl = new RegExp('/access-denied\\?ReturnUrl=' + encodeURIComponent(path) + '$');

      const hop = await page.request.get(path, { maxRedirects: 0 });
      expect(hop.status()).toBe(302);
      expect(hop.headers()['location']).toMatch(deniedUrl);

      const response = await page.goto(path);
      await expect(page).toHaveURL(deniedUrl);
      expect(response?.status()).toBe(403);
      await expect(errorPages.accessDeniedHeading).toBeVisible();
      await expect(errorPages.headings).toHaveCount(1);
      await expect(errorPages.loginForm).toHaveCount(0);
      await expect(page).toHaveTitle(/^Access denied - AssetHub$/);
      // The requested URL is never echoed back onto the page.
      await expect(page.locator('#main-content')).not.toContainText(path);
      await expect(errorPages.goHome).toHaveAttribute('href', '/');
    });
  }

  test('viewer sees collections page', async ({ page }) => {
    // Was navigating to /assets, which no page declares. At the time an unknown
    // route answered 404 with an empty body, so the assertion saw a blank page
    // (it now gets the not-found page, still 404). A stale route, not a
    // regression: /collections is the page the test name has always described.
    await page.goto('/collections');
    await page.waitForLoadState('networkidle');
    await expect(page.getByText(/collections/i).first()).toBeVisible();
  });

  test('viewer does not see upload area (without explicit collection access)', async ({ page }) => {
    await page.goto('/collections');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation);

    // Viewer without specific collection access should not see upload
    const uploadArea = page.locator('.upload-area');
    // With no collection selected or no access, upload should not be visible
    await expect(uploadArea).not.toBeVisible();
  });
});
