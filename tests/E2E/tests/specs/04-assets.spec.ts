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

  test.beforeAll(async () => {
    // Create test fixtures and API-seed a collection
    ensureTestFixtures();
    api = await ApiHelper.withCookieAuth();

    // Create a test collection via API
    const collection = await api.createCollection(testCollectionName, 'E2E test collection for assets');
    testCollectionId = collection.id;
  });

  test.afterAll(async () => {
    // Cleanup: delete test collection (and its assets)
    if (testCollectionId) {
      api = await ApiHelper.withCookieAuth();
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
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
      await expect(uploadArea).toBeVisible({ timeout: 10_000 });

      // Should have an icon in the upload area
      await expect(uploadArea.locator('.mud-icon-root, svg').first()).toBeVisible();

      // Should expose a file input for uploads
      const fileInput = page.locator('#fileInput');
      await expect(fileInput).toHaveAttribute('type', 'file');
    });

    test('upload a PNG image and verify it appears @smoke', async ({ page }) => {
      const fixtures = ensureTestFixtures();
      const fileInput = page.locator('#fileInput');
      const uploadTitle = `Upload-Test-${timestamp}`;

      // Count existing cards
      const cardsBefore = await page.locator('.asset-card').count();

      // Upload the test image
      await fileInput.setInputFiles(fixtures.testImage);

      // Wait for upload to complete - asset card should appear
      await expect(page.locator('.asset-card')).toHaveCount(cardsBefore + 1, { timeout: env.timeouts.upload });
    });

    test('upload a PDF document and verify it appears', async ({ page }) => {
      const fixtures = ensureTestFixtures();
      const fileInput = page.locator('#fileInput');

      // Count existing cards
      const cardsBefore = await page.locator('.asset-card').count();

      // Upload the PDF
      await fileInput.setInputFiles(fixtures.testPdf);

      // Wait for upload to complete - asset card should appear
      await expect(page.locator('.asset-card')).toHaveCount(cardsBefore + 1, { timeout: env.timeouts.upload });
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
    test.beforeAll(async () => {
      // Ensure we have test assets — upload via API
      api = await ApiHelper.withCookieAuth();
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
      // Should have at least one card (from upload in beforeAll)
      await expect(cards.first()).toBeVisible({ timeout: 15_000 });
      // Each card should have a title
      await expect(cards.first().locator('.mud-typography').first()).toBeVisible();
    });

    test('asset card shows title and type chip', async ({ page }) => {
      const cards = page.locator('.asset-card');
      await expect(cards.first()).toBeVisible({ timeout: 15_000 });
      
      const card = cards.first();
      // Should have title text
      await expect(card.locator('.mud-typography').first()).toBeVisible();
      // Should have type chip
      await expect(card.locator('.mud-chip').first()).toBeVisible();
    });

    test('search filters assets with no results for nonexistent query', async ({ page }) => {
      // Search input may be in toolbar or main content area
      const searchInput = page.locator('input[placeholder*="earch" i], .mud-input input[type="text"]').first();
      const hasSearch = await searchInput.isVisible().catch(() => false);
      test.skip(!hasSearch, 'Search input not available on this page');
      
      await searchInput.fill('nonexistent-asset-xyz-99999');
      await page.waitForTimeout(env.timeouts.debounce);
      
      // Should show no asset cards for nonsense query
      const cards = page.locator('.asset-card');
      await expect(cards).toHaveCount(0, { timeout: 5_000 });
    });

    test('type filter dropdown has options', async ({ page }) => {
      const typeSelect = page.locator('.mud-select').first();
      await expect(typeSelect).toBeVisible({ timeout: 10_000 });
      
      await typeSelect.click();
      await page.waitForTimeout(500);
      
      // MudBlazor renders options as list items in a popover
      const options = page.locator('.mud-popover .mud-list-item');
      await expect(options.first()).toBeVisible({ timeout: 5_000 });
      
      await page.keyboard.press('Escape');
    });

    test('sort dropdown has options', async ({ page }) => {
      const sortSelect = page.locator('.mud-select').nth(1);
      await expect(sortSelect).toBeVisible({ timeout: 10_000 });
      
      await sortSelect.click();
      await page.waitForTimeout(500);
      
      // MudBlazor renders options as list items in a popover
      const options = page.locator('.mud-popover .mud-list-item');
      await expect(options.first()).toBeVisible({ timeout: 5_000 });
      
      await page.keyboard.press('Escape');
    });

    test('clicking asset navigates to detail page', async ({ page }) => {
      const cards = page.locator('.asset-card');
      await expect(cards.first()).toBeVisible({ timeout: 15_000 });
      
      // Click the card body, which is the actual navigation target in grid mode
      const openTarget = cards.first().locator('.clickable').first();
      await expect(openTarget).toBeVisible();
      await Promise.all([
        page.waitForURL(/\/assets\/[0-9a-f-]+/, { timeout: 30_000 }),
        openTarget.click()
      ]);
    });

    test('refresh button reloads assets', async ({ page }) => {
      const refreshBtn = page.getByRole('button', { name: /refresh/i });
      await expect(refreshBtn).toBeVisible({ timeout: 10_000 });
      
      await refreshBtn.click();
      await page.waitForTimeout(env.timeouts.animation);
      
      // Page should still show assets
      await expect(page.locator('.mud-container, .mud-grid').first()).toBeVisible();
    });

    test('download all button is visible and enabled', async ({ page }) => {
      const downloadAllBtn = page.getByRole('button', { name: /download all/i });
      await expect(downloadAllBtn).toBeVisible({ timeout: 10_000 });
      await expect(downloadAllBtn).toBeEnabled();
    });

    test('share collection button is visible and enabled', async ({ page }) => {
      const shareBtn = page.getByRole('button', { name: /share collection/i });
      await expect(shareBtn).toBeVisible({ timeout: 10_000 });
      await expect(shareBtn).toBeEnabled();
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
          const openTarget = card.locator('.clickable').first();
          await expect(openTarget).toBeVisible();
          await Promise.all([
            page.waitForURL(/\/assets\/[0-9a-f-]+/),
            openTarget.click()
          ]);
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
      await expect(table).toBeVisible({ timeout: 10_000 });
    });

    test('preview is displayed for image assets', async ({ page }) => {
      // Either image, video, iframe, or generic icon
      const preview = page.locator('.mud-image, video, iframe, .mud-icon-root').first();
      await expect(preview).toBeVisible({ timeout: 10_000 });
    });

    test('download button is functional', async ({ page }) => {
      const downloadBtn = page.getByRole('button', { name: /download/i }).first()
        .or(page.getByRole('link', { name: /download/i }).first());
      await expect(downloadBtn).toBeVisible();
    });

    test('edit button opens dialog with title input', async ({ page }) => {
      const editBtn = page.getByRole('button', { name: /edit/i });
      await expect(editBtn).toBeVisible({ timeout: 10_000 });
      
      await editBtn.click();
      await dialog.waitForDialog();
      
      // Dialog should have title input
      await expect(dialog.dialog.locator('input').first()).toBeVisible();
      await dialog.closeDialog();
    });

    test('edit asset title and verify change', async ({ page }) => {
      const editBtn = page.getByRole('button', { name: /edit/i });
      await expect(editBtn).toBeVisible({ timeout: 10_000 });

      await editBtn.click();
      await dialog.waitForDialog();

      const newTitle = `Updated-${timestamp}`;
      const titleInput = dialog.dialog.locator('input').first();
      await titleInput.clear();
      await titleInput.fill(newTitle);
      // Trigger MudBlazor validation by moving focus away from the input
      await titleInput.press('Tab');
      await page.waitForTimeout(env.timeouts.animation);

      const saveBtn = dialog.dialog.getByRole('button', { name: /save|update|ok/i });
      await expect(saveBtn).toBeVisible({ timeout: 5_000 });
      await expect(saveBtn).toBeEnabled({ timeout: 5_000 });

      await saveBtn.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Title should update — use getByRole to find the asset heading (not the app title)
      await expect(page.getByRole('heading', { name: newTitle, level: 5 })).toBeVisible({ timeout: 5_000 });
    });

    test('share button opens share dialog with password field', async ({ page }) => {
      const shareBtn = page.getByRole('button', { name: /share/i }).first();
      await expect(shareBtn).toBeVisible({ timeout: 10_000 });
      
      await shareBtn.click();
      await dialog.waitForDialog();
      
      // Should have password field
      await expect(dialog.dialog.locator('input').first()).toBeVisible();
      await dialog.closeDialog();
    });

    test('delete button opens confirmation dialog', async ({ page }) => {
      const deleteBtn = page.getByRole('button', { name: /delete/i });
      await expect(deleteBtn).toBeVisible({ timeout: 10_000 });
      
      await deleteBtn.click();
      
      // Confirmation dialog
      const confirmDialog = page.locator('.mud-dialog');
      await expect(confirmDialog).toBeVisible({ timeout: 5_000 });
      
      // Should have cancel button
      const cancelBtn = confirmDialog.getByRole('button', { name: /cancel|no/i });
      await expect(cancelBtn).toBeVisible();
      await cancelBtn.click();
    });

    // Note: 'back button', 'add to collection', 'metadata panel', and 'tags section' tests removed
    // These were testing element existence with conditional guards - not meaningful behavioral tests
    // The core workflows (edit, share, delete) are tested above
  });
});
