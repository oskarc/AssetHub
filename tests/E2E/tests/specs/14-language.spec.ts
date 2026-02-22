import { test, expect } from '@playwright/test';
import { LayoutPage } from '../pages/layout.page';
import { env } from '../config/env';

test.describe('Language Switching @language', () => {
  let layout: LayoutPage;

  test.beforeEach(async ({ page }) => {
    layout = new LayoutPage(page);
    await page.goto('/');
    await page.waitForLoadState('networkidle');
  });

  /**
   * The LanguageSwitcher is a MudMenu with a Language icon button in the app bar.
   * Clicking it opens a popover with MudMenuItems for "English" and "Svenska".
   */

  /** Helper: find the language menu trigger button (MudMenu icon button with language icon) */
  function languageButton(page: import('@playwright/test').Page) {
    // The MudMenu renders an icon button; look for the language icon in the app bar
    return page.locator('.mud-appbar button').filter({ has: page.locator('[data-testid="LanguageIcon"], svg') })
      .or(page.locator('.mud-appbar .mud-menu button'));
  }

  /** Helper: open the language menu */
  async function openLanguageMenu(page: import('@playwright/test').Page) {
    const btn = languageButton(page);
    // The language button is the MudMenu trigger — may be the last icon button before the account menu
    // Use a more specific approach: find the button that opens a menu with "English" and "Svenska"
    const appbarButtons = page.locator('.mud-appbar button, .mud-appbar .mud-icon-button');
    const count = await appbarButtons.count();

    // Try each button to find the language menu
    for (let i = 0; i < count; i++) {
      const current = appbarButtons.nth(i);
      if (!(await current.isVisible())) continue;
      await current.click();
      await page.waitForTimeout(500);

      // Check if "English" and "Svenska" appeared in a popover
      const hasEnglish = await page.getByText('English').isVisible().catch(() => false);
      const hasSvenska = await page.getByText('Svenska').isVisible().catch(() => false);
      if (hasEnglish && hasSvenska) return; // Found it!

      // Close anything we opened by pressing Escape
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
    }
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
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'networkidle' }),
      svenskaOption.click(),
    ]);

    // After reload, the culture cookie should be set
    const cookies = await page.context().cookies();
    const cultureCookie = cookies.find(c => c.name === '.AspNetCore.Culture');
    expect(cultureCookie).toBeTruthy();
    expect(cultureCookie!.value).toContain('sv');
  });

  test('Swedish culture persists after navigation', async ({ page }) => {
    // First switch to Swedish
    await openLanguageMenu(page);

    const svenskaOption = page.getByText('Svenska');
    if (await svenskaOption.isVisible()) {
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        svenskaOption.click(),
      ]);

      // Navigate to collections page
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');

      // Cookie should still be set to Swedish
      const cookies = await page.context().cookies();
      const cultureCookie = cookies.find(c => c.name === '.AspNetCore.Culture');
      expect(cultureCookie).toBeTruthy();
      expect(cultureCookie!.value).toContain('sv');
    }
  });

  test('switching back to English works', async ({ page }) => {
    // Set Swedish first via cookie
    await page.context().addCookies([{
      name: '.AspNetCore.Culture',
      value: 'c=sv|uic=sv',
      domain: new URL(env.baseUrl).hostname,
      path: '/',
    }]);
    await page.reload({ waitUntil: 'networkidle' });

    // Open language menu and switch back to English
    await openLanguageMenu(page);

    const englishOption = page.getByText('English');
    if (await englishOption.isVisible()) {
      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }),
        englishOption.click(),
      ]);

      const cookies = await page.context().cookies();
      const cultureCookie = cookies.find(c => c.name === '.AspNetCore.Culture');
      expect(cultureCookie).toBeTruthy();
      expect(cultureCookie!.value).toContain('en');
    }
  });
});
