import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Page Object for the Login page (/login).
 */
export class LoginPage {
  readonly page: Page;
  readonly card: Locator;
  readonly appIcon: Locator;
  readonly appName: Locator;
  readonly subtitle: Locator;
  readonly signInButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.card = page.locator('.mud-paper').first();
    this.appIcon = page.locator('.mud-icon-root').first();
    this.appName = page.locator('.mud-typography-h4');
    this.subtitle = page.locator('.mud-typography-body1');
    this.signInButton = page.getByRole('button', { name: /sign in/i });
  }

  async goto() {
    await this.page.goto('/login');
  }

  async expectVisible() {
    await expect(this.signInButton).toBeVisible();
    await expect(this.appName).toBeVisible();
  }

  async clickSignIn() {
    await this.signInButton.click();
  }
}
