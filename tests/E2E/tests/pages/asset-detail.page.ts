import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for the Asset Detail page (/assets/{guid}).
 */
export class AssetDetailPage {
  readonly page: Page;

  // Preview
  readonly previewImage: Locator;
  readonly previewVideo: Locator;
  readonly previewPdf: Locator;

  // Details panel
  readonly title: Locator;
  readonly typeChip: Locator;
  readonly statusChip: Locator;
  readonly description: Locator;
  readonly fileInfoTable: Locator;
  readonly tagsSection: Locator;
  readonly metadataPanel: Locator;
  readonly collectionsSection: Locator;
  readonly addToCollectionButton: Locator;

  // Action buttons
  readonly downloadButton: Locator;
  readonly shareButton: Locator;
  readonly editButton: Locator;
  readonly deleteButton: Locator;
  readonly backButton: Locator;

  // Loading / error
  readonly loadingIndicator: Locator;
  readonly notFound: Locator;

  constructor(page: Page) {
    this.page = page;

    // Preview elements
    this.previewImage = page.locator('.mud-image, img[alt]').first();
    this.previewVideo = page.locator('video');
    this.previewPdf = page.locator('iframe');

    // Details
    this.title = page.locator('.mud-typography-h5').first();
    this.typeChip = page.locator('.mud-chip').first();
    this.statusChip = page.locator('.mud-chip').nth(1);
    this.description = page.locator('.mud-typography-body1, .mud-typography-body2').filter({ hasText: /./  });
    this.fileInfoTable = page.locator('.mud-simple-table').first();
    this.tagsSection = page.locator('.mud-chip-set');
    this.metadataPanel = page.locator('.mud-expand-panel');
    this.collectionsSection = page.locator('.mud-chip').filter({ has: page.locator('svg') });
    this.addToCollectionButton = page.locator('[title*="Add"]').first();

    // Actions
    this.downloadButton = page.getByRole('button', { name: /download original/i }).first().or(page.getByRole('link', { name: /download original/i }).first());
    this.shareButton = page.getByRole('button', { name: /share/i }).first();
    this.editButton = page.getByRole('button', { name: /edit/i });
    this.deleteButton = page.getByRole('button', { name: /delete/i });
    this.backButton = page.getByRole('button', { name: /back/i });

    // State
    this.loadingIndicator = page.locator('.mud-progress-circular');
    this.notFound = page.getByText(/not found/i);
  }

  async goto(assetId: string) {
    await this.page.goto(`/assets/${assetId}`);
    await this.page.waitForLoadState('networkidle');
  }

  async expectLoaded() {
    await expect(this.title).toBeVisible({ timeout: env.timeouts.navigation });
  }

  async getTitle(): Promise<string> {
    return (await this.title.textContent()) || '';
  }

  /** Open edit dialog and update details */
  async editAsset(opts: { title?: string; description?: string; tags?: string[] }) {
    await this.editButton.click();
    const dialog = this.page.locator('.mud-dialog');
    await expect(dialog).toBeVisible();

    if (opts.title) {
      const titleInput = dialog.locator('input').first();
      await titleInput.clear();
      await titleInput.fill(opts.title);
    }
    if (opts.description) {
      const descInput = dialog.locator('textarea, input').nth(1);
      await descInput.clear();
      await descInput.fill(opts.description);
    }
    if (opts.tags) {
      // Find tag input and add tags
      const tagInput = dialog.locator('input').last();
      for (const tag of opts.tags) {
        await tagInput.fill(tag);
        await tagInput.press('Enter');
      }
    }

    await dialog.getByRole('button', { name: /save|update|ok/i }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Create a share from this asset detail page */
  async createShare(): Promise<void> {
    await this.shareButton.click();
    const dialog = this.page.locator('.mud-dialog');
    await expect(dialog).toBeVisible();
    // Click create/share button inside dialog
    await dialog.getByRole('button', { name: /create|share/i }).last().click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Delete the asset with confirmation */
  async deleteAsset() {
    await this.deleteButton.click();
    // Confirm deletion dialog
    const dialog = this.page.locator('.mud-dialog');
    if (await dialog.isVisible()) {
      await dialog.getByRole('button', { name: /delete|confirm|yes|ok/i }).click();
    }
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  async goBack() {
    await this.backButton.click();
  }
}
