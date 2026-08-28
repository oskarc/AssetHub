import { test, expect } from '@playwright/test';
import { IdentityLoginPage } from '../pages/identity-login.page';
import { LoginPage } from '../pages/login.page';
import { LayoutPage } from '../pages/layout.page';
import { env } from '../config/env';

/**
 * Authentication against the local ASP.NET Core Identity provider.
 *
 * Sign-in is a form POST served by the application itself — there is no
 * redirect to an external identity provider, so every assertion here stays
 * within the app's own origin.
 */
test.describe('Authentication & Login @auth @smoke', () => {
  test.describe('Login page', () => {
    test.use({ storageState: { cookies: [], origins: [] } }); // Unauthenticated

    test('displays login page with branding', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();
      await loginPage.expectVisible();
      await expect(page.locator('.mud-typography-h4')).toContainText(/assethub/i);
    });

    test('shows the sign-in form', async ({ page }) => {
      await page.goto('/login');
      await expect(page.locator('#userName')).toBeVisible();
      await expect(page.locator('#password')).toBeVisible();
    });

    test('/auth/login serves the sign-in form', async ({ page }) => {
      // Under Identity this redirects to /login rather than to an external IdP.
      await page.goto('/auth/login?returnUrl=%2F');
      await page.waitForURL(/\/login/, { timeout: 15_000 });
      await expect(page.locator('#userName')).toBeVisible();
    });

    test('full login flow with admin user', async ({ page }) => {
      const login = new IdentityLoginPage(page);
      await login.loginAsAdmin();

      const layout = new LayoutPage(page);
      await layout.expectAuthenticated();
    });

    test('full login flow with viewer user', async ({ page }) => {
      const login = new IdentityLoginPage(page);
      await login.loginAsViewer();

      const layout = new LayoutPage(page);
      await layout.expectAuthenticated();
    });

    test('rejects invalid credentials without revealing whether the account exists', async ({ page }) => {
      const login = new IdentityLoginPage(page);
      await page.goto('/login', { waitUntil: 'domcontentloaded' });
      await login.login('baduser', 'badpassword');

      // Comes back to the sign-in page with a generic error. The message must be
      // the same for an unknown user as for a wrong password — see
      // PasswordResetLinkSender / the /auth/login handler for why.
      await page.waitForURL(/\/login\?.*error=/, { timeout: 15_000 });
      await expect(login.errorAlert).toBeVisible({ timeout: 10_000 });
    });

    test('unauthenticated user redirected from protected pages', async ({ page }) => {
      await page.goto('/collections');
      await page.waitForURL(/\/login/, { timeout: 15_000 });
    });
  });

  test.describe('Authenticated session', () => {
    test('logout clears session', async ({ page }) => {
      await page.goto('/');
      // Blazor Server prerenders the shell before the circuit connects; the menu
      // button exists in the DOM but has no handler until then.
      await page.waitForLoadState('networkidle');
      const layout = new LayoutPage(page);
      await layout.expectAuthenticated();

      await layout.signOut();
      // Assert the resulting STATE, not a URL — the pre-logout URL can match a
      // permissive pattern and let waitForURL return before navigation happens.
      await layout.expectUnauthenticated();
    });
  });
});
