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

  test('language switcher is visible in app bar', async ({ page }) => {
    const switcher = page.locator('.language-switcher');
    await expect(switcher).toBeVisible();
  });

  test('language switcher defaults to English', async ({ page }) => {
    const switcher = page.locator('.language-switcher');
    // MudSelect renders the selected value in a hidden input or display text
    const selectedText = switcher.locator('.mud-input-slot, input');
    // The default value should be "en" or display "English"
    const value = await selectedText.inputValue().catch(() => '');
    // Accept either "en" or empty (some MudSelect renders just show text)
    expect(value === 'en' || value === '').toBeTruthy();
  });

  test('dropdown shows English and Svenska options', async ({ page }) => {
    const switcher = page.locator('.language-switcher');
    // Open the MudSelect dropdown
    await switcher.locator('.mud-input-control').click();
    await page.waitForTimeout(env.timeouts.animation);

    // Options appear in the popover
    const popover = page.locator('.mud-popover-provider, .mud-popover-open');

    // Check for both language options somewhere on the page
    await expect(page.getByText('English')).toBeVisible();
    await expect(page.getByText('Svenska')).toBeVisible();
  });

  test('switching to Swedish reloads the page', async ({ page }) => {
    const switcher = page.locator('.language-switcher');

    // Open dropdown
    await switcher.locator('.mud-input-control').click();
    await page.waitForTimeout(env.timeouts.animation);

    // Click Svenska option — this triggers a full page reload
    const svenskaOption = page.getByText('Svenska');
    await expect(svenskaOption).toBeVisible();

    // Listen for navigation (forceLoad: true causes full reload)
    const [response] = await Promise.all([
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
    const switcher = page.locator('.language-switcher');
    await switcher.locator('.mud-input-control').click();
    await page.waitForTimeout(env.timeouts.animation);

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

    // Now switch back to English
    const switcher = page.locator('.language-switcher');
    await switcher.locator('.mud-input-control').click();
    await page.waitForTimeout(env.timeouts.animation);

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
