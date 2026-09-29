import { test, expect } from '@playwright/test';
import { ErrorPagesPage } from '../pages/error-pages.page';
import { IdentityLoginPage } from '../pages/identity-login.page';
import { LayoutPage } from '../pages/layout.page';
import { waitForBlazorInteractive } from '../helpers/blazor-helper';

const UNKNOWN_PAGE = '/this-route-does-not-exist';

test.describe('Not-found page @errors', () => {
  let errorPages: ErrorPagesPage;
  let layout: LayoutPage;

  test.beforeEach(async ({ page }) => {
    errorPages = new ErrorPagesPage(page);
    layout = new LayoutPage(page);
  });

  test('signed-in user gets a 404 not-found page inside the app shell', async ({ page }) => {
    const response = await page.goto(UNKNOWN_PAGE);

    expect(response?.status()).toBe(404);
    await expect(errorPages.notFoundHeading).toBeVisible();
    await expect(errorPages.headings).toHaveCount(1);
    await expect(layout.appBar).toBeVisible();
    await expect(page).toHaveTitle(/^Page not found - AssetHub$/);
  });

  test('not-found page keeps a working app shell once the circuit starts', async ({ page }) => {
    await page.goto(UNKNOWN_PAGE);
    await waitForBlazorInteractive(page);

    // The account menu only opens when the page is interactive — a dead menu on
    // an error page is exactly what this guards against.
    await expect(async () => {
      await layout.accountMenuTrigger.click();
      await expect(page.getByText(/sign out/i)).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 15_000 });
    await expect(errorPages.notFoundHeading).toBeVisible();
    await expect(errorPages.blazorErrorUi).toBeHidden();
  });

  test('Go Home leaves the not-found page', async ({ page }) => {
    await page.goto(UNKNOWN_PAGE);
    await errorPages.goHome.click();

    await expect(page).toHaveURL(/\/$/);
    await expect(errorPages.notFoundHeading).toHaveCount(0);
  });

  test('in-circuit navigation to an unknown route renders the not-found page', async ({ page }) => {
    await page.goto('/');
    await waitForBlazorInteractive(page);

    await expect(async () => {
      await errorPages.navigateInCircuit('/no-such-route-in-circuit');
      await expect(errorPages.notFoundHeading).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 15_000 });
    await expect(page).toHaveURL(/\/no-such-route-in-circuit$/);
    await expect(errorPages.blazorErrorUi).toBeHidden();
    await expect(layout.appBar).toBeVisible();
  });

  test('unknown /api path answers 404 without an HTML page', async ({ page }) => {
    const response = await page.request.get('/api/v1/does-not-exist', { maxRedirects: 0 });

    expect(response.status()).toBe(404);
    expect(response.headers()['content-type'] ?? '').not.toContain('text/html');
    expect(await response.text()).toBe('');
  });
});

test.describe('Not-found page, anonymous @errors @auth', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('anonymous user opening an unknown URL is sent to sign in', async ({ page }) => {
    await page.goto(UNKNOWN_PAGE);

    // Unchanged on purpose: an anonymous visitor learns nothing about which
    // routes exist.
    await expect(page).toHaveURL(/\/login\?ReturnUrl=%2Fthis-route-does-not-exist$/);
    await expect(page.locator('#userName')).toBeVisible();
  });
});

test.describe('Access-denied page, in-circuit @errors @acl', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('viewer denied in-circuit sees the same access-denied content', async ({ page }) => {
    const errorPages = new ErrorPagesPage(page);
    await new IdentityLoginPage(page).loginAsViewer();
    await page.goto('/');
    await waitForBlazorInteractive(page);

    await expect(async () => {
      await errorPages.navigateInCircuit('/admin');
      await expect(errorPages.accessDeniedHeading).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout: 15_000 });
    await expect(page).toHaveURL(/\/admin$/);
    await expect(errorPages.loginForm).toHaveCount(0);
    await expect(errorPages.blazorErrorUi).toBeHidden();
  });

  test('Sign in again from the access-denied page signs the viewer out', async ({ page }) => {
    const errorPages = new ErrorPagesPage(page);
    await new IdentityLoginPage(page).loginAsViewer();
    await page.goto('/admin');
    await expect(errorPages.accessDeniedHeading).toBeVisible();

    await errorPages.signInAgain.click();

    await expect(page).toHaveURL(/\/login/);
    await expect(errorPages.loginForm).toBeVisible();
  });
});
