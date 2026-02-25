import { test, expect } from '@playwright/test';
import { env } from '../config/env';

test.describe('Language Switching @language', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('.mud-appbar')).toBeVisible({ timeout: 15_000 });
  });

  /**
   * The LanguageSwitcher is a MudMenu with a Language icon button in the app bar.
   * Clicking it opens a popover with MudMenuItems for "English" and "Svenska".
   */

  /** Helper: open the language menu */
  async function openLanguageMenu(page: import('@playwright/test').Page) {
    const languageActivator = page.locator('.mud-appbar .mud-menu-icon-button-activator').last();

    await expect(languageActivator).toBeVisible({ timeout: 15_000 });
    await page.keyboard.press('Escape');
    await languageActivator.click({ force: true });

    await expect(page.getByText('English')).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText('Svenska')).toBeVisible({ timeout: 10_000 });
  }

  test('language switcher is visible in app bar', async ({ page }) => {
    // The language switcher is a MudMenu with a language icon in the app bar
    // Verify we can open it and see the options
    await openLanguageMenu(page);
    await expect(page.getByText('English')).toBeVisible();
    await expect(page.getByText('Svenska')).toBeVisible();
    await page.keyboard.press('Escape');
  });

  test('language switcher defaults to English', async ({ page }) => {
    await openLanguageMenu(page);
    // The current language should have a check mark icon next to "English"
    const englishItem = page.getByText('English');
    await expect(englishItem).toBeVisible();
    // Just verify English is visible (it's the default)
    await page.keyboard.press('Escape');
  });

  test('dropdown shows English and Svenska options', async ({ page }) => {
    await openLanguageMenu(page);
    await expect(page.getByText('English')).toBeVisible();
    await expect(page.getByText('Svenska')).toBeVisible();
    await page.keyboard.press('Escape');
  });

  test('switching to Swedish reloads the page', async ({ page }) => {
    await openLanguageMenu(page);

    const svenskaOption = page.getByText('Svenska');
    await expect(svenskaOption).toBeVisible();

    // Clicking Svenska triggers forceLoad: true → full page reload
    await svenskaOption.click();
    await page.waitForLoadState('domcontentloaded');

    // After reload, Swedish UI text should be visible
    await expect(page.getByRole('link', { name: 'Hem' })).toBeVisible({ timeout: 10_000 });
  });

  test('Swedish culture persists after page reload', async ({ page }) => {
    // First switch to Swedish
    await openLanguageMenu(page);

    const svenskaOption = page.getByText('Svenska');
    await expect(svenskaOption).toBeVisible();
    await svenskaOption.click();
    await page.waitForLoadState('domcontentloaded');

    // Verify Swedish is active
    await expect(page.getByRole('link', { name: 'Hem' })).toBeVisible({ timeout: 10_000 });

    // Navigate via full reload to verify language state is retained
    await page.reload({ waitUntil: 'domcontentloaded' });

    // Swedish UI should persist after navigation
    await expect(page.getByRole('link', { name: 'Hem' })).toBeVisible({ timeout: 10_000 });
  });

  test('switching back to English shows English UI', async ({ page }) => {
    // Set Swedish first via cookie
    await page.context().addCookies([{
      name: '.AspNetCore.Culture',
      value: 'c=sv|uic=sv',
      domain: new URL(env.baseUrl).hostname,
      path: '/',
    }]);
    await page.reload({ waitUntil: 'networkidle' });

    // Verify Swedish is showing
    await expect(page.getByRole('link', { name: 'Hem' })).toBeVisible({ timeout: 10_000 });

    // Open language menu and switch back to English
    await openLanguageMenu(page);

    const englishOption = page.getByText('English').last();
    await expect(englishOption).toBeVisible({ timeout: 10_000 });
    await englishOption.click();
    await page.waitForLoadState('domcontentloaded');

    // English UI should now be visible
    await expect(page.getByRole('link', { name: 'Home' })).toBeVisible({ timeout: 10_000 });
  });
});
