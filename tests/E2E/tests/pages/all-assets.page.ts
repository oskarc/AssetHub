import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for the All Assets admin page (/all-assets).
 */
export class AllAssetsPage {
  readonly page: Page;

  readonly pageTitle: Locator;
  readonly refreshButton: Locator;
  readonly searchInput: Locator;
  readonly collectionFilter: Locator;
  readonly typeFilter: Locator;
  readonly sortSelect: Locator;
  readonly gridViewButton: Locator;
  readonly listViewButton: Locator;
  readonly assetCards: Locator;
  readonly loadMoreButton: Locator;
  readonly statsText: Locator;
  readonly loadingIndicator: Locator;

  constructor(page: Page) {
    this.page = page;

    this.pageTitle = page.locator('.mud-typography-h4');
    this.refreshButton = page.getByRole('button', { name: /refresh/i });
    this.searchInput = page.getByRole('textbox', { name: /search/i });
    this.collectionFilter = page.locator('.mud-select').first();
    this.typeFilter = page.locator('.mud-select').nth(1);
    this.sortSelect = page.locator('.mud-select').nth(2);
    this.gridViewButton = page.locator('.mud-button-group .mud-icon-button').first();
    this.listViewButton = page.locator('.mud-button-group .mud-icon-button').last();
    this.assetCards = page.locator('.asset-card');
    this.loadMoreButton = page.getByRole('button', { name: /load more/i });
    this.statsText = page.locator('.mud-typography-body2').filter({ hasText: /showing/i });
    this.loadingIndicator = page.locator('.mud-progress-circular');
  }

  async goto() {
    await this.page.goto('/all-assets');
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.pageTitle).toBeVisible();
  }

  async search(query: string) {
    await this.searchInput.fill(query);
    await this.page.waitForTimeout(env.timeouts.debounce);
  }

  async getAssetCount(): Promise<number> {
    return await this.assetCards.count();
  }

  getAssetCard(title: string): Locator {
    return this.assetCards.filter({ hasText: title });
  }

  async viewAsset(title: string) {
    const card = this.getAssetCard(title);
    await card.locator('.mud-icon-button').first().click();
  }

  async deleteAsset(title: string) {
    const card = this.getAssetCard(title);
    await card.locator('.mud-icon-button[style*="color"] , .mud-icon-button').last().click();
    // Confirm
    const dialog = this.page.locator('.mud-dialog');
    if (await dialog.isVisible()) {
      await dialog.getByRole('button', { name: /delete|confirm|yes/i }).click();
    }
    await this.page.waitForTimeout(env.timeouts.animation);
  }
}
