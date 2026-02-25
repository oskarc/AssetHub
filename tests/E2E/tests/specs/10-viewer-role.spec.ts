import { test, expect } from '@playwright/test';
import { KeycloakLoginPage } from '../pages/keycloak-login.page';
import { LayoutPage } from '../pages/layout.page';
import { env } from '../config/env';

test.describe('Viewer Role Restrictions @acl @auth', () => {
  test.use({ storageState: { cookies: [], origins: [] } }); // Start unauthenticated

  let layout: LayoutPage;

  test.beforeEach(async ({ page }) => {
    layout = new LayoutPage(page);
    const keycloak = new KeycloakLoginPage(page);
    await keycloak.loginAsViewer();
    await page.waitForLoadState('networkidle');
  });

  test('viewer cannot see admin nav items', async ({ page }) => {
    const allAssets = page.locator('.mud-navmenu').getByText(/all assets/i);
    const admin = page.locator('.mud-navmenu').getByText(/admin/i);

    // These should NOT be visible for a viewer
    await expect(allAssets).not.toBeVisible();
    await expect(admin).not.toBeVisible();
  });

  test('viewer is redirected from /admin', async ({ page }) => {
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation);

    // Should be redirected away or see forbidden content
    const url = page.url();
    const forbiddenText = page.getByText(/forbidden|unauthorized|access denied/i);
    
    // Either redirected OR showing forbidden message
    const isRedirected = !url.includes('/admin');
    const hasForbiddenMessage = await forbiddenText.isVisible().catch(() => false);
    
    expect(isRedirected || hasForbiddenMessage).toBeTruthy();
  });

  test('viewer is redirected from /all-assets', async ({ page }) => {
    await page.goto('/all-assets');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation);

    // Should be redirected or not show admin page content
    const url = page.url();
    const adminTitle = page.locator('.mud-typography-h4');
    
    // Either redirected away from all-assets OR admin title not visible
    const isRedirected = !url.includes('/all-assets');
    const hasAdminContent = await adminTitle.isVisible().catch(() => false);
    
    expect(isRedirected || !hasAdminContent).toBeTruthy();
  });

  test('viewer sees collections page', async ({ page }) => {
    await page.goto('/assets');
    await page.waitForLoadState('networkidle');
    await expect(page.getByText(/collections/i).first()).toBeVisible();
  });

  test('viewer does not see upload area (without explicit collection access)', async ({ page }) => {
    await page.goto('/assets');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation);

    // Viewer without specific collection access should not see upload
    const uploadArea = page.locator('.upload-area');
    // With no collection selected or no access, upload should not be visible
    await expect(uploadArea).not.toBeVisible();
  });
});
