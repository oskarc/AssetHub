import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for the Admin page (/admin).
 */
export class AdminPage {
  readonly page: Page;

  // Page elements
  readonly pageTitle: Locator;
  readonly tabs: Locator;

  // Tab panels
  readonly shareManagementTab: Locator;
  readonly collectionAccessTab: Locator;
  readonly userManagementTab: Locator;

  constructor(page: Page) {
    this.page = page;

    this.pageTitle = page.locator('.mud-typography-h4');
    this.tabs = page.locator('.mud-tabs');

    // Tab selectors — click by visible tab text
    this.shareManagementTab = page.getByRole('tab', { name: /share/i });
    this.collectionAccessTab = page.getByRole('tab', { name: /collection/i });
    this.userManagementTab = page.getByRole('tab', { name: /user/i });
  }

  async goto() {
    await this.page.goto('/admin');
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.pageTitle).toBeVisible();
  }

  // --- Share Management ---

  async switchToShareManagement() {
    await this.shareManagementTab.click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  get shareTable(): Locator {
    return this.page.locator('.mud-table').first();
  }

  get shareSearchInput(): Locator {
    return this.page.locator('.mud-table .mud-input-root input').first();
  }

  async searchShares(query: string) {
    await this.shareSearchInput.fill(query);
    await this.page.waitForTimeout(env.timeouts.debounce);
  }

  getShareRow(index: number): Locator {
    return this.shareTable.locator('tbody tr').nth(index);
  }

  async revokeShare(rowIndex: number) {
    const row = this.getShareRow(rowIndex);
    await row.locator('[title*="evoke"], .mud-icon-button').filter({ has: this.page.locator('svg') }).last().click();
    // Confirm
    const dialog = this.page.locator('.mud-dialog');
    if (await dialog.isVisible()) {
      await dialog.getByRole('button', { name: /revoke|confirm|yes/i }).click();
    }
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  async viewShareInfo(rowIndex: number) {
    const row = this.getShareRow(rowIndex);
    await row.locator('[title*="nfo"], .mud-icon-button').first().click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  async editSharePassword(rowIndex: number) {
    const row = this.getShareRow(rowIndex);
    await row.locator('[title*="assword"], [title*="dit"]').first().click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  // --- Collection Access ---

  async switchToCollectionAccess() {
    await this.collectionAccessTab.click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  get collectionTreePanel(): Locator {
    return this.page.locator('.admin-collection-tree, .mud-paper').first();
  }

  async selectAdminCollection(name: string) {
    await this.page.getByText(name, { exact: false }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  get aclTable(): Locator {
    return this.page.locator('.mud-simple-table');
  }

  async addUserAccess(userId: string, role: string) {
    const panel = this.page.locator('.mud-paper').filter({ hasText: /user|access/i });
    await panel.locator('input').first().fill(userId);
    // Select role
    await panel.locator('.mud-select').click();
    await this.page.getByRole('option', { name: new RegExp(role, 'i') }).click();
    await panel.getByRole('button', { name: /add/i }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  // --- User Management ---

  async switchToUserManagement() {
    await this.userManagementTab.click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  get userTable(): Locator {
    return this.page.locator('.mud-table').first();
  }

  get userSearchInput(): Locator {
    return this.page.locator('.mud-table .mud-input-root input').first();
  }

  get createUserButton(): Locator {
    return this.page.getByRole('button', { name: /create user/i });
  }

  async searchUsers(query: string) {
    await this.userSearchInput.fill(query);
    await this.page.waitForTimeout(env.timeouts.debounce);
  }

  async openCreateUserDialog() {
    await this.createUserButton.click();
    await expect(this.page.locator('.mud-dialog')).toBeVisible();
  }

  async createUser(opts: { username: string; email: string; firstName?: string; lastName?: string; password?: string }) {
    await this.openCreateUserDialog();
    const dialog = this.page.locator('.mud-dialog');

    // Fill fields — the order matches CreateUserDialog.razor
    const inputs = dialog.locator('input');
    await inputs.nth(0).fill(opts.username);  // Username
    await inputs.nth(1).fill(opts.email);     // Email
    if (opts.firstName) await inputs.nth(2).fill(opts.firstName);
    if (opts.lastName) await inputs.nth(3).fill(opts.lastName);
    if (opts.password) {
      await inputs.nth(4).clear();
      await inputs.nth(4).fill(opts.password);
    }

    await dialog.getByRole('button', { name: /create/i }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  async manageUserAccess(rowIndex: number) {
    const row = this.userTable.locator('tbody tr').nth(rowIndex);
    await row.getByRole('button', { name: /manage access/i }).click();
    await expect(this.page.locator('.mud-dialog')).toBeVisible();
  }
}
