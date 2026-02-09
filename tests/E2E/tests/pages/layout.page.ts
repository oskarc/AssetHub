import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Page Object for the main layout — AppBar, NavMenu, Drawer.
 */
export class LayoutPage {
  readonly page: Page;

  // AppBar
  readonly appBar: Locator;
  readonly menuToggle: Locator;
  readonly appName: Locator;
  readonly signOutButton: Locator;
  readonly signInButton: Locator;
  readonly darkModeToggle: Locator;
  readonly userDisplayName: Locator;

  // Drawer / Nav
  readonly drawer: Locator;
  readonly navHome: Locator;
  readonly navCollections: Locator;
  readonly navAllAssets: Locator;
  readonly navAdmin: Locator;

  constructor(page: Page) {
    this.page = page;

    this.appBar = page.locator('.mud-appbar');
    this.menuToggle = page.locator('.mud-appbar button').first();
    this.appName = page.locator('.mud-appbar').getByText('AssetHub');
    this.signOutButton = page.getByRole('button', { name: /sign out/i });
    this.signInButton = page.getByRole('button', { name: /sign in/i });
    this.darkModeToggle = page.locator('.mud-appbar .mud-icon-button').last();
    this.userDisplayName = page.locator('.mud-appbar .mud-typography-body2');

    this.drawer = page.locator('#nav-drawer, .mud-drawer');
    this.navHome = page.locator('.mud-nav-menu a[href="/"], .mud-nav-menu a[href=""]').first();
    this.navCollections = page.locator('.mud-nav-menu a[href*="assets"]').first();
    this.navAllAssets = page.locator('.mud-nav-menu').getByText(/all assets/i);
    this.navAdmin = page.locator('.mud-nav-menu').getByText(/admin/i).first();
  }

  async navigateHome() {
    await this.navHome.click();
    await this.page.waitForURL('/');
  }

  async navigateToCollections() {
    await this.navCollections.click();
    await this.page.waitForURL(/\/assets/);
  }

  async navigateToAllAssets() {
    await this.navAllAssets.click();
    await this.page.waitForURL(/\/all-assets/);
  }

  async navigateToAdmin() {
    await this.navAdmin.click();
    await this.page.waitForURL(/\/admin/);
  }

  async toggleDarkMode() {
    await this.darkModeToggle.click();
  }

  async signOut() {
    await this.signOutButton.click();
  }

  async expectAuthenticated() {
    await expect(this.signOutButton).toBeVisible();
  }

  async expectUnauthenticated() {
    await expect(this.signInButton).toBeVisible();
  }

  async expectAdminNavVisible() {
    await expect(this.navAllAssets).toBeVisible();
    await expect(this.navAdmin).toBeVisible();
  }

  async expectAdminNavHidden() {
    await expect(this.navAllAssets).not.toBeVisible();
    await expect(this.navAdmin).not.toBeVisible();
  }
}
