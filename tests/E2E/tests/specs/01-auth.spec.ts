import { test, expect } from '@playwright/test';
import { KeycloakLoginPage } from '../pages/keycloak-login.page';
import { LoginPage } from '../pages/login.page';
import { LayoutPage } from '../pages/layout.page';
import { env } from '../config/env';

test.describe('Authentication & Login @auth @smoke', () => {
  test.describe('Login page', () => {
    test.use({ storageState: { cookies: [], origins: [] } }); // Unauthenticated

    test('displays login page with branding', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();
      await loginPage.expectVisible();
      await expect(page.locator('.mud-typography-h4')).toContainText(/assethub/i);
    });

    test('has a functional sign-in button', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();
      await expect(loginPage.signInButton).toBeEnabled();
    });

    test('sign-in redirects to Keycloak', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();
      await loginPage.clickSignIn();
      // Should redirect to Keycloak login
      await page.waitForURL(/.*keycloak.*|.*8443.*/, { timeout: 15_000 });
      await expect(page.locator('#username')).toBeVisible();
      await expect(page.locator('#password')).toBeVisible();
    });

    test('full login flow with admin user', async ({ page }) => {
      const keycloak = new KeycloakLoginPage(page);
      await keycloak.loginAsAdmin();

      const layout = new LayoutPage(page);
      await layout.expectAuthenticated();
    });

    test('full login flow with viewer user', async ({ page }) => {
      const keycloak = new KeycloakLoginPage(page);
      await keycloak.loginAsViewer();

      const layout = new LayoutPage(page);
      await layout.expectAuthenticated();
    });

    test('rejects invalid credentials at Keycloak', async ({ page }) => {
      await page.goto('/login');
      await page.locator('button.mud-button-filled-primary.mud-button-filled-size-large').click();
      await page.waitForURL(/.*keycloak.*|.*8443.*/);

      const keycloak = new KeycloakLoginPage(page);
      await keycloak.login('baduser', 'badpassword');

      // Should stay on Keycloak with error
      await expect(page.locator('#input-error, .kc-feedback-text, [class*="alert"]')).toBeVisible({ timeout: 10_000 });
    });

    test('unauthenticated user redirected from protected pages', async ({ page }) => {
      await page.goto('/assets');
      // Should redirect to login
      await page.waitForURL(/\/login|.*keycloak.*|.*8443.*/, { timeout: 15_000 });
    });
  });

  test.describe('Authenticated session', () => {
    test('logout clears session', async ({ page }) => {
      await page.goto('/');
      const layout = new LayoutPage(page);
      await layout.expectAuthenticated();

      await layout.signOut();
      // Should redirect to login or Keycloak logout
      await page.waitForURL(/\/login|.*keycloak.*/, { timeout: 15_000 });
    });
  });
});
