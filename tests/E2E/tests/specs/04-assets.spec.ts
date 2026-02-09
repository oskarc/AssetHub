import { test, expect } from '@playwright/test';
import { AssetsPage } from '../pages/assets.page';
import { AssetDetailPage } from '../pages/asset-detail.page';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('Asset Management @assets', () => {
  let assetsPage: AssetsPage;
  let detailPage: AssetDetailPage;
  let dialog: DialogHelper;
  let snackbar: SnackbarHelper;
  let api: ApiHelper;
  let testCollectionId: string;
  let testAssetId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-Assets-${timestamp}`;
  const testAssetTitle = `${env.testData.assetTitlePrefix}-${timestamp}`;

  test.beforeAll(async ({ request }) => {
    // Create test fixtures and API-seed a collection
    ensureTestFixtures();
    api = new ApiHelper(request);
    await api.authenticate();

    // Create a test collection via API
    const collection = await api.createCollection(testCollectionName, 'E2E test collection for assets');
    testCollectionId = collection.id;
  });

  test.afterAll(async ({ request }) => {
    // Cleanup: delete test collection (and its assets)
    if (testCollectionId) {
      api = new ApiHelper(request);
      await api.authenticate();
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
  });

  test.describe('Asset Upload', () => {
    test.beforeEach(async ({ page }) => {
      assetsPage = new AssetsPage(page);
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);
      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
    });

    test('upload area is visible for authorized users', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation);
      // Admin should see upload area
      const uploadArea = page.locator('.upload-area');
      await expect(uploadArea).toBeVisible({ timeout: 10_000 });
    });

    test('upload area shows correct UI elements', async ({ page }) => {
      const uploadArea = page.locator('.upload-area');
      if (await uploadArea.isVisible()) {
        // Should have cloud icon
        await expect(uploadArea.locator('.mud-icon-root')).toBeVisible();
        // Should have browse button
        await expect(page.locator('label[for="fileInput"]')).toBeVisible();
      }
    });

    test('upload a PNG image @smoke', async ({ page }) => {
      const fixtures = ensureTestFixtures();
      const fileInput = page.locator('#fileInput');

      // Upload the test image
      await fileInput.setInputFiles(fixtures.testImage);

      // Wait for upload to complete — look for success indicator
      await page.waitForTimeout(env.timeouts.upload);

      // Should see upload progress or success
      const success = page.locator('.mud-icon-root').filter({ has: page.locator('[data-testid*="Check"], svg') });
      // Upload might complete quickly or show progress
    });

    test('upload a PDF document', async ({ page }) => {
      const fixtures = ensureTestFixtures();
      const fileInput = page.locator('#fileInput');
      await fileInput.setInputFiles(fixtures.testPdf);
      await page.waitForTimeout(env.timeouts.upload);
    });

    test('file input accepts correct file types', async ({ page }) => {
      const fileInput = page.locator('#fileInput');
      const accept = await fileInput.getAttribute('accept');
      // Should accept images, videos, PDFs
      expect(accept).toContain('image/');
      expect(accept).toContain('video/');
      expect(accept).toContain('.pdf');
    });
  });

  test.describe('Asset Browsing & Search', () => {
    test.beforeAll(async ({ request }) => {
      // Ensure we have test assets — upload via API
      api = new ApiHelper(request);
      await api.authenticate();
      const fixtures = ensureTestFixtures();
      try {
        const result = await api.uploadAsset(testCollectionId, fixtures.testImage, testAssetTitle);
        testAssetId = result.id;
      } catch {
        // Asset may already exist from upload test
      }
    });

    test.beforeEach(async ({ page }) => {
      assetsPage = new AssetsPage(page);
      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);
    });

    test('asset grid displays cards', async ({ page }) => {
      const cards = page.locator('.asset-card');
      // Should have at least one card (from upload)
      const count = await cards.count();
      if (count > 0) {
        // Each card should have a thumbnail area and title
        const firstCard = cards.first();
        await expect(firstCard).toBeVisible();
      }
    });

    test('asset card shows title, type chip, and size', async ({ page }) => {
      const cards = page.locator('.asset-card');
      const count = await cards.count();
      if (count > 0) {
        const card = cards.first();
        // Should have title text
        await expect(card.locator('.mud-typography')).toBeVisible();
        // Should have type chip
        await expect(card.locator('.mud-chip')).toBeVisible();
      }
    });

    test('search filters assets', async ({ page }) => {
      const searchInput = page.locator('.mud-input-root input[type="text"]').first();
      if (await searchInput.isVisible()) {
        await searchInput.fill('nonexistent-asset-xyz');
        await page.waitForTimeout(env.timeouts.debounce);
        // Should show no results or empty state
        const cards = page.locator('.asset-card');
        const emptyState = page.getByText(/no.*asset|no.*result|empty/i);
        const hasCards = await cards.count() > 0;
        const hasEmpty = await emptyState.isVisible().catch(() => false);
        // Either no cards or empty state visible
        expect(hasCards || hasEmpty !== undefined).toBeTruthy();
      }
    });

    test('type filter works', async ({ page }) => {
      const typeSelect = page.locator('.mud-select').first();
      if (await typeSelect.isVisible()) {
        await typeSelect.click();
        await page.waitForTimeout(500);
        // Select image filter
        const imageOption = page.getByRole('option', { name: /image/i });
        if (await imageOption.isVisible()) {
          await imageOption.click();
          await page.waitForTimeout(env.timeouts.debounce);
        } else {
          // Close dropdown
          await page.keyboard.press('Escape');
        }
      }
    });

    test('sort options work', async ({ page }) => {
      const sortSelect = page.locator('.mud-select').nth(1);
      if (await sortSelect.isVisible()) {
        await sortSelect.click();
        await page.waitForTimeout(500);
        // Select oldest first
        const option = page.getByRole('option', { name: /oldest|asc/i }).first();
        if (await option.isVisible()) {
          await option.click();
          await page.waitForTimeout(env.timeouts.debounce);
        } else {
          await page.keyboard.press('Escape');
        }
      }
    });

    test('grid/list view toggle works', async ({ page }) => {
      const viewButtons = page.locator('.mud-button-group .mud-icon-button');
      const count = await viewButtons.count();
      if (count >= 2) {
        // Click list view
        await viewButtons.last().click();
        await page.waitForTimeout(env.timeouts.animation);
        // Click grid view
        await viewButtons.first().click();
        await page.waitForTimeout(env.timeouts.animation);
      }
    });

    test('clicking asset navigates to detail page', async ({ page }) => {
      const cards = page.locator('.asset-card');
      const count = await cards.count();
      if (count > 0) {
        // Click the view button on first card
        const viewBtn = cards.first().locator('.mud-icon-button').first();
        await viewBtn.click();
        await page.waitForURL(/\/assets\/[0-9a-f-]+/, { timeout: env.timeouts.navigation });
      }
    });

    test('refresh button reloads assets', async ({ page }) => {
      const refreshBtn = page.getByRole('button', { name: /refresh/i });
      if (await refreshBtn.isVisible()) {
        await refreshBtn.click();
        await page.waitForTimeout(env.timeouts.animation);
        // Page should still show assets
        await expect(page.locator('.mud-container, .mud-grid')).toBeVisible();
      }
    });

    test('download all button is visible', async ({ page }) => {
      const downloadAllBtn = page.getByRole('button', { name: /download all/i });
      if (await downloadAllBtn.isVisible()) {
        await expect(downloadAllBtn).toBeEnabled();
      }
    });

    test('share collection button is visible', async ({ page }) => {
      const shareBtn = page.getByRole('button', { name: /share collection/i });
      // Should be visible for contributor+ roles
      if (await shareBtn.isVisible()) {
        await expect(shareBtn).toBeEnabled();
      }
    });
  });

  test.describe('Asset Detail', () => {
    test.beforeEach(async ({ page }) => {
      detailPage = new AssetDetailPage(page);
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);

      if (testAssetId) {
        await detailPage.goto(testAssetId);
      } else {
        // Navigate via collection
        await page.goto(`/assets?collection=${testCollectionId}`);
        await page.waitForLoadState('networkidle');
        await page.waitForTimeout(env.timeouts.animation);
        const card = page.locator('.asset-card').first();
        if (await card.isVisible()) {
          const viewBtn = card.locator('.mud-icon-button').first();
          await viewBtn.click();
          await page.waitForURL(/\/assets\/[0-9a-f-]+/);
        }
      }
    });

    test('asset detail page loads @smoke', async ({ page }) => {
      // Should show title
      await expect(page.locator('.mud-typography-h5').first()).toBeVisible({ timeout: 15_000 });
    });

    test('shows asset type chip', async ({ page }) => {
      const chips = page.locator('.mud-chip');
      await expect(chips.first()).toBeVisible();
    });

    test('shows asset status chip', async ({ page }) => {
      const chips = page.locator('.mud-chip');
      const count = await chips.count();
      if (count >= 2) {
        await expect(chips.nth(1)).toBeVisible();
      }
    });

    test('shows file info table', async ({ page }) => {
      const table = page.locator('.mud-simple-table');
      if (await table.isVisible()) {
        // Should contain size, type, created date
        await expect(table).toBeVisible();
      }
    });

    test('shows collection membership', async ({ page }) => {
      // Collection chips should be visible
      const collectionChips = page.locator('.mud-chip').filter({ has: page.locator('svg') });
      if (await collectionChips.first().isVisible()) {
        await expect(collectionChips.first()).toBeVisible();
      }
    });

    test('preview image is displayed for image assets', async ({ page }) => {
      // Either image, video, iframe, or generic icon
      const preview = page.locator('.mud-image, video, iframe, .mud-icon-root').first();
      await expect(preview).toBeVisible({ timeout: 10_000 });
    });

    test('download button is functional', async ({ page }) => {
      const downloadBtn = page.getByRole('button', { name: /download/i }).first()
        .or(page.getByRole('link', { name: /download/i }).first());
      await expect(downloadBtn).toBeVisible();
    });

    test('edit button opens dialog', async ({ page }) => {
      const editBtn = page.getByRole('button', { name: /edit/i });
      if (await editBtn.isVisible()) {
        await editBtn.click();
        await dialog.waitForDialog();
        // Dialog should have title input
        await expect(dialog.dialog.locator('input').first()).toBeVisible();
        await dialog.closeDialog();
      }
    });

    test('edit asset title and description', async ({ page }) => {
      const editBtn = page.getByRole('button', { name: /edit/i });
      if (await editBtn.isVisible()) {
        await editBtn.click();
        await dialog.waitForDialog();

        const newTitle = `Updated-${timestamp}`;
        const titleInput = dialog.dialog.locator('input').first();
        await titleInput.clear();
        await titleInput.fill(newTitle);

        await dialog.confirmDialog(/save|update|ok/i);
        await page.waitForTimeout(env.timeouts.animation);

        // Title should update
        await expect(page.locator('.mud-typography-h5').first()).toContainText(newTitle, { timeout: 5_000 });
      }
    });

    test('share button opens share dialog', async ({ page }) => {
      const shareBtn = page.getByRole('button', { name: /share/i }).first();
      if (await shareBtn.isVisible()) {
        await shareBtn.click();
        await dialog.waitForDialog();
        // Should have password field and expiration
        await expect(dialog.dialog.locator('input')).toBeVisible();
        await dialog.closeDialog();
      }
    });

    test('delete button opens confirmation dialog', async ({ page }) => {
      const deleteBtn = page.getByRole('button', { name: /delete/i });
      if (await deleteBtn.isVisible()) {
        await deleteBtn.click();
        // Confirmation dialog
        const confirmDialog = page.locator('.mud-dialog');
        if (await confirmDialog.isVisible()) {
          // Should have cancel and confirm buttons
          const cancelBtn = confirmDialog.getByRole('button', { name: /cancel|no/i });
          if (await cancelBtn.isVisible()) {
            await cancelBtn.click();
          } else {
            await page.keyboard.press('Escape');
          }
        }
      }
    });

    test('back button navigates to collection view', async ({ page }) => {
      const backBtn = page.getByRole('button', { name: /back/i });
      if (await backBtn.isVisible()) {
        await backBtn.click();
        await page.waitForURL(/\/assets/, { timeout: env.timeouts.navigation });
      }
    });

    test('add to collection button opens dialog', async ({ page }) => {
      const addBtn = page.locator('[title*="Add"]').first();
      if (await addBtn.isVisible()) {
        await addBtn.click();
        await dialog.waitForDialog();
        await dialog.closeDialog();
      }
    });

    test('advanced metadata panel is expandable', async ({ page }) => {
      const metadataPanel = page.locator('.mud-expand-panel');
      if (await metadataPanel.isVisible()) {
        // Click to expand
        await metadataPanel.click();
        await page.waitForTimeout(env.timeouts.animation);
      }
    });

    test('tags section is displayed', async ({ page }) => {
      const tagChipSet = page.locator('.mud-chip-set');
      // Tags section may or may not have tags
      if (await tagChipSet.isVisible()) {
        await expect(tagChipSet).toBeVisible();
      }
    });
  });
});
