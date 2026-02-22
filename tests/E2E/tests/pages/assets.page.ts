import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for the Assets / Collections page (/assets).
 */
export class AssetsPage {
  readonly page: Page;

  // Sidebar
  readonly collectionsHeading: Locator;
  readonly createCollectionButton: Locator;
  readonly deselectButton: Locator;
  readonly collectionTree: Locator;

  // Search & Filter bar
  readonly searchInput: Locator;
  readonly typeFilter: Locator;
  readonly sortSelect: Locator;
  readonly gridViewButton: Locator;
  readonly listViewButton: Locator;

  // Actions
  readonly downloadAllButton: Locator;
  readonly shareCollectionButton: Locator;
  readonly manageAccessButton: Locator;
  readonly refreshButton: Locator;

  // Upload area
  readonly uploadArea: Locator;
  readonly fileInput: Locator;
  readonly browseFilesButton: Locator;

  // Asset grid
  readonly assetCards: Locator;
  readonly loadMoreButton: Locator;
  readonly emptyState: Locator;

  // Breadcrumbs
  readonly breadcrumbs: Locator;

  constructor(page: Page) {
    this.page = page;

    // Sidebar
    this.collectionsHeading = page.getByText(/collections/i).first();
    this.createCollectionButton = page.getByRole('button', { name: /create collection/i });
    this.deselectButton = page.locator('.mud-icon-button').filter({ has: page.locator('.mud-svg-icon') });
    this.collectionTree = page.locator('.mud-card').first();

    // Search bar
    this.searchInput = page.locator('.mud-input-root input[type="text"]').first();
    this.typeFilter = page.locator('.mud-select').first();
    this.sortSelect = page.locator('.mud-select').nth(1);
    this.gridViewButton = page.locator('.mud-button-group .mud-icon-button').first();
    this.listViewButton = page.locator('.mud-button-group .mud-icon-button').last();

    // Actions
    this.downloadAllButton = page.getByRole('button', { name: /download all/i });
    this.shareCollectionButton = page.getByRole('button', { name: /share collection/i });
    this.manageAccessButton = page.getByRole('button', { name: /manage access/i });
    this.refreshButton = page.getByRole('button', { name: /refresh/i });

    // Upload
    this.uploadArea = page.locator('.upload-area');
    this.fileInput = page.locator('#fileInput');
    this.browseFilesButton = page.locator('label[for="fileInput"]');

    // Grid
    this.assetCards = page.locator('.asset-card');
    this.loadMoreButton = page.getByRole('button', { name: /load more/i });
    this.emptyState = page.locator('.mud-alert, .empty-state, [class*="empty"]');

    // Breadcrumbs
    this.breadcrumbs = page.locator('.mud-breadcrumbs');
  }

  async goto(collectionId?: string) {
    const url = collectionId ? `/assets?collection=${collectionId}` : '/assets';
    await this.page.goto(url);
    await this.page.waitForLoadState('networkidle');
  }

  /** Select a collection by clicking its card */
  async selectCollection(name: string) {
    await this.page.locator('.mud-card').filter({ hasText: name }).first().click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Create a new collection via the dialog */
  async createCollection(name: string, description?: string) {
    // Click the "Create Collection" button
    await this.page.getByRole('button', { name: /create collection/i }).click();

    // Fill the dialog
    const dialog = this.page.locator('.mud-dialog');
    await expect(dialog).toBeVisible();
    await dialog.locator('input').first().fill(name);
    if (description) {
      await dialog.locator('input, textarea').last().fill(description);
    }
    // Submit
    await dialog.getByRole('button', { name: /create|save|ok/i }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Search for assets */
  async searchAssets(query: string) {
    await this.searchInput.fill(query);
    await this.page.waitForTimeout(env.timeouts.debounce);
  }

  /** Upload a file via the file input */
  async uploadFile(filePath: string) {
    await this.fileInput.setInputFiles(filePath);
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Get asset card by title text */
  getAssetCard(title: string): Locator {
    return this.assetCards.filter({ hasText: title });
  }

  /** Click view on an asset card */
  async viewAsset(title: string) {
    const card = this.getAssetCard(title);
    await card.click();
  }

  /** Count visible asset cards */
  async getAssetCount(): Promise<number> {
    return await this.assetCards.count();
  }

  async expectCollectionSelected(name: string) {
    await expect(this.breadcrumbs.getByText(name)).toBeVisible();
  }

  async expectUploadAreaVisible() {
    await expect(this.uploadArea).toBeVisible();
  }

  async expectUploadAreaHidden() {
    await expect(this.uploadArea).not.toBeVisible();
  }
}
