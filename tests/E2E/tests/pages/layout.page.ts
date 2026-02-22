import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Page Object for the main layout — AppBar, NavMenu, Drawer.
 * MudBlazor v8 uses class "mud-navmenu" (no hyphen) for the nav menu.
 * Sign Out is a MudMenuItem inside a MudMenu dropdown (account icon).
 */
export class LayoutPage {
  readonly page: Page;

  // AppBar
  readonly appBar: Locator;
  readonly menuToggle: Locator;
  readonly appName: Locator;
  readonly signInButton: Locator;
  readonly darkModeToggle: Locator;
  readonly userDisplayName: Locator;
  readonly accountMenuTrigger: Locator;

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
    this.signInButton = page.getByRole('button', { name: /sign in/i });
    this.darkModeToggle = page.locator('.mud-appbar .mud-icon-button').last();
    this.userDisplayName = page.locator('.mud-appbar .mud-typography-body2');
    // The account menu trigger is the first MudMenu icon button in the app bar
    this.accountMenuTrigger = page.locator('.mud-appbar .mud-menu .mud-menu-icon-button-activator').first();

    this.drawer = page.locator('#nav-drawer, .mud-drawer');
    this.navHome = page.locator('.mud-navmenu a[href="/"], .mud-navmenu a[href=""]').first();
    this.navCollections = page.locator('.mud-navmenu a[href="collections"]').first();
    this.navAllAssets = page.locator('.mud-navmenu').getByText(/all assets/i);
    this.navAdmin = page.locator('.mud-navmenu').getByText(/admin/i).first();
  }

  async navigateHome() {
    await this.navHome.click();
    await this.page.waitForURL('/');
  }

  async navigateToCollections() {
    await this.navCollections.click();
    await this.page.waitForURL(/\/collections/);
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

  /** Opens the account dropdown menu and clicks Sign Out */
  async signOut() {
    await this.accountMenuTrigger.click();
    const signOutItem = this.page.getByText(/sign out/i);
    await signOutItem.waitFor({ state: 'visible', timeout: 5_000 });
    await signOutItem.click();
  }

  /** Checks user display name is visible (proves authenticated state) */
  async expectAuthenticated() {
    await expect(this.userDisplayName).toBeVisible();
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
