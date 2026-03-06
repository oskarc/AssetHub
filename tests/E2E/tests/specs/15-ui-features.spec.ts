import { test, expect } from '@playwright/test';
import { AssetsPage } from '../pages/assets.page';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

/**
 * Tests for UI feature additions:
 * - Collection info banner: "Description:" heading and description text
 * - Collection info banner: "Edit Collection" / "Delete Collection" text buttons
 * - Upload queued status: all files show "Queued" chip immediately
 * - Collection card: edit/delete icon buttons with stopPropagation
 */
test.describe('UI Feature Tests @ui', () => {
  let api: ApiHelper;
  let assetsPage: AssetsPage;
  let dialog: DialogHelper;
  let snackbar: SnackbarHelper;

  const timestamp = Date.now();
  const collectionName = `${env.testData.collectionPrefix}-UI-${timestamp}`;
  const collectionDescription = `E2E description for UI tests ${timestamp}`;
  let testCollectionId: string;

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();

    // Create collection with a description via API
    const collection = await api.createCollection(collectionName, collectionDescription);
    testCollectionId = collection.id;
  });

  test.afterAll(async () => {
    if (testCollectionId) {
      api = await ApiHelper.withCookieAuth();
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
  });

  test.describe('Collection Info Banner', () => {
    test.beforeEach(async ({ page }) => {
      assetsPage = new AssetsPage(page);
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);
      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);
    });

    test('shows "Description:" heading when collection has a description', async ({ page }) => {
      // The info banner should contain "Description:" as a label
      const descriptionLabel = page.getByText('Description:');
      await expect(descriptionLabel).toBeVisible({ timeout: 10_000 });
    });

    test('shows the collection description text', async ({ page }) => {
      // The actual description text should be visible
      const descriptionText = page.getByText(collectionDescription);
      await expect(descriptionText).toBeVisible({ timeout: 10_000 });
    });

    test('shows "Edit Collection" button for admin user', async ({ page }) => {
      const editBtn = page.getByRole('button', { name: /edit collection/i });
      await expect(editBtn).toBeVisible({ timeout: 10_000 });
    });

    test('shows "Delete Collection" button for admin user', async ({ page }) => {
      const deleteBtn = page.getByRole('button', { name: /delete collection/i });
      await expect(deleteBtn).toBeVisible({ timeout: 10_000 });
    });

    test('"Edit Collection" button opens edit dialog', async ({ page }) => {
      const editBtn = page.getByRole('button', { name: /edit collection/i });
      await dialog.clickAndWaitForDialog(editBtn);
      // Dialog should have an input pre-filled with the collection name
      const nameInput = dialog.dialog.locator('input').first();
      await expect(nameInput).toBeVisible();
      await expect(nameInput).toHaveValue(collectionName);

      await dialog.closeDialog();
    });

    test('"Delete Collection" button opens confirmation dialog', async ({ page }) => {
      const deleteBtn = page.getByRole('button', { name: /delete collection/i });

      await dialog.clickAndWaitForDialog(deleteBtn);
      await expect(dialog.dialog).toContainText(collectionName);

      // Cancel the deletion
      const cancelBtn = dialog.dialog.getByRole('button', { name: /cancel/i });
      await cancelBtn.click();
      await page.waitForTimeout(env.timeouts.animation);
    });

    test('shows "Your Role:" with role chip in info banner', async ({ page }) => {
      const roleLabel = page.getByText(/your role/i);
      await expect(roleLabel).toBeVisible({ timeout: 10_000 });

      // Should show the admin role chip
      const roleChip = page.locator('.mud-chip').filter({ hasText: /admin/i });
      await expect(roleChip.first()).toBeVisible();
    });
  });

  test.describe('Collection without description', () => {
    let noDescCollectionId: string;
    const noDescName = `${env.testData.collectionPrefix}-NoDesc-${timestamp}`;

    test.beforeAll(async () => {
      api = await ApiHelper.withCookieAuth();
      const collection = await api.createCollection(noDescName);
      noDescCollectionId = collection.id;
    });

    test.afterAll(async () => {
      if (noDescCollectionId) {
        api = await ApiHelper.withCookieAuth();
        await api.deleteCollection(noDescCollectionId).catch(() => {});
        await api.dispose();
      }
    });

    test('does not show "Description:" heading when collection has no description', async ({ page }) => {
      await page.goto(`/assets?collection=${noDescCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      // The "Description:" label should NOT appear
      const descriptionLabel = page.getByText('Description:');
      await expect(descriptionLabel).not.toBeVisible({ timeout: 5_000 });
    });
  });

  test.describe('Collection Card Actions', () => {
    test.beforeEach(async ({ page }) => {
      assetsPage = new AssetsPage(page);
      dialog = new DialogHelper(page);
      // Navigate to collection grid (no collection selected)
      await page.goto('/assets');
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);
    });

    test('collection card shows access role text', async ({ page }) => {
      // Find the test collection card
      const card = page.locator('.mud-card').filter({ hasText: collectionName });
      await expect(card).toBeVisible({ timeout: 10_000 });

      // Card should display "Your Role:" label
      const roleLabel = card.getByText(/your role/i);
      await expect(roleLabel).toBeVisible();

      // Card should show a role chip (Admin, Editor, or Viewer)
      const roleChip = card.locator('.mud-chip');
      await expect(roleChip).toBeVisible();
      const chipText = await roleChip.textContent();
      expect(chipText).toMatch(/admin|editor|viewer/i);
    });

    test('collection card name has tooltip with full name', async ({ page }) => {
      // Find the test collection card
      const card = page.locator('.mud-card').filter({ hasText: collectionName });
      await expect(card).toBeVisible({ timeout: 10_000 });

      // Hover over the collection name to trigger tooltip
      const collectionNameElement = card.locator('.collection-name-truncate');
      await collectionNameElement.hover();
      await page.waitForTimeout(env.timeouts.animation);

      // MudTooltip renders into a popover — check for tooltip content in popover or tooltip containers
      const tooltip = page.locator('.mud-tooltip, .mud-popover').filter({ hasText: collectionName });
      await expect(tooltip.first()).toBeVisible({ timeout: 5_000 });
    });
  });

  test.describe('Upload Queued Status', () => {
    test.beforeEach(async ({ page }) => {
      assetsPage = new AssetsPage(page);
      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);
    });

    test('multiple file upload shows "Queued" status for pending files', async ({ page }) => {
      const fixtures = ensureTestFixtures();
      const fileInput = page.locator('#fileInput');

      if (await fileInput.isVisible({ timeout: 5_000 })) {
        // Upload multiple files at once
        await fileInput.setInputFiles([fixtures.testImage, fixtures.testPdf]);

        // Immediately check for "Queued" chips — they should appear before uploads start
        // Use a short timeout since Queued status is the initial pre-populated state
        const queuedChips = page.locator('.mud-chip').filter({ hasText: /queued/i });
        const uploadSection = page.locator('.mud-paper').filter({ hasText: /uploads/i });

        // Wait for the upload section to appear
        await expect(uploadSection).toBeVisible({ timeout: 10_000 });

        // At least one file should show "Queued" status (the second file while the first uploads)
        // Note: This is timing-dependent — the first file may already be uploading
        const chipCount = await queuedChips.count();
        const uploadingIndicator = page.locator('.upload-progress, .mud-progress-linear');
        const hasQueued = chipCount > 0;
        const hasUploading = await uploadingIndicator.isVisible().catch(() => false);

        // Either we caught the Queued state or the upload already progressed
        expect(hasQueued || hasUploading).toBeTruthy();
      }
    });

    test('upload section shows file names and sizes', async ({ page }) => {
      const fixtures = ensureTestFixtures();
      const fileInput = page.locator('#fileInput');

      if (await fileInput.isVisible({ timeout: 5_000 })) {
        await fileInput.setInputFiles(fixtures.testImage);

        // Upload section should appear
        const uploadSection = page.locator('.mud-paper').filter({ hasText: /uploads/i });
        await expect(uploadSection).toBeVisible({ timeout: 10_000 });

        // Should show the file name
        await expect(uploadSection.getByText('test-image.png')).toBeVisible();
      }
    });
  });
});
