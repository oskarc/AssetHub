import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for the public Share page (/share/{token}).
 */
export class SharePage {
  readonly page: Page;

  // Password prompt
  readonly passwordInput: Locator;
  readonly accessButton: Locator;
  readonly passwordError: Locator;

  // Shared asset view
  readonly assetTitle: Locator;
  readonly assetPreviewImage: Locator;
  readonly assetPreviewVideo: Locator;
  readonly downloadButton: Locator;
  readonly typeChip: Locator;
  readonly fileInfoTable: Locator;

  // Shared collection view
  readonly collectionTitle: Locator;
  readonly downloadAllButton: Locator;
  readonly collectionCards: Locator;
  readonly backToCollectionButton: Locator;

  // State
  readonly loadingIndicator: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;

    // Password
    this.passwordInput = page.locator('input[type="password"]');
    this.accessButton = page.getByRole('button', { name: /access/i });
    this.passwordError = page.locator('.mud-alert-error, .mud-alert');

    // Shared asset
    this.assetTitle = page.locator('.mud-typography-h5').first();
    this.assetPreviewImage = page.locator('.mud-image, img').first();
    this.assetPreviewVideo = page.locator('video');
    this.downloadButton = page.getByRole('button', { name: /download/i }).first().or(page.getByRole('link', { name: /download/i }).first());
    this.typeChip = page.locator('.mud-chip').first();
    this.fileInfoTable = page.locator('.mud-simple-table');

    // Shared collection
    this.collectionTitle = page.locator('.mud-typography-h5').first();
    this.downloadAllButton = page.getByRole('button', { name: /download all/i });
    this.collectionCards = page.locator('.mud-card');
    this.backToCollectionButton = page.getByRole('button', { name: /back/i });

    // State
    this.loadingIndicator = page.locator('.mud-progress-circular');
    this.errorAlert = page.locator('.mud-alert');
  }

  async goto(token: string) {
    await this.page.goto(`/share/${token}`);
    await this.page.waitForLoadState('networkidle');
  }

  async submitPassword(password: string) {
    await this.passwordInput.fill(password);
    await this.accessButton.click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  async expectPasswordPrompt() {
    await expect(this.passwordInput).toBeVisible();
    await expect(this.accessButton).toBeVisible();
  }

  async expectAssetVisible() {
    await expect(this.assetTitle).toBeVisible();
    await expect(this.downloadButton).toBeVisible();
  }

  async expectCollectionVisible() {
    await expect(this.collectionTitle).toBeVisible();
  }

  async viewCollectionAsset(title: string) {
    const card = this.collectionCards.filter({ hasText: title });
    await card.getByRole('button', { name: /view/i }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  async downloadCollectionAsset(title: string) {
    const card = this.collectionCards.filter({ hasText: title });
    await card.getByRole('button', { name: /download/i }).click();
  }
}
