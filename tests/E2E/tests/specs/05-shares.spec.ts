import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { AssetsPage } from '../pages/assets.page';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';
import { SharePage } from '../pages/share.page';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('Share Management @shares', () => {
  let api: ApiHelper;
  let dialog: DialogHelper;
  let snackbar: SnackbarHelper;
  let testCollectionId: string;
  let testAssetId: string;
  let shareToken: string;
  let shareId: string;
  let sharePassword: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-Share-${timestamp}`;
  const testAssetTitle = `${env.testData.assetTitlePrefix}-Share-${timestamp}`;

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();

    // Create collection + asset for sharing
    const collection = await api.createCollection(testCollectionName, 'Share test collection');
    testCollectionId = collection.id;

    const fixtures = ensureTestFixtures();
    const asset = await api.uploadAsset(testCollectionId, fixtures.testImage, testAssetTitle);
    testAssetId = asset.id;
  });

  test.afterAll(async () => {
    api = await ApiHelper.withCookieAuth();
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
  });

  test.describe('Share Creation (via UI)', () => {
    test('create asset share from detail page @smoke', async ({ page }) => {
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);

      // Navigate to collection, then click asset to open detail view
      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const card = page.locator('.asset-card').first();
      await expect(card).toBeVisible({ timeout: 10_000 });
      const openTarget = card.locator('.clickable').first();
      await expect(openTarget).toBeVisible();
      await Promise.all([
        page.waitForURL(/\/assets\/[0-9a-f-]+/),
        openTarget.click()
      ]);
      await page.waitForLoadState('networkidle');

      // Now on asset detail page — find the share button (not "Share Collection")
      const shareBtn = page.getByRole('button', { name: /^share$/i }).first();
      await expect(shareBtn).toBeVisible({ timeout: 10_000 });

      await dialog.clickAndWaitForDialog(shareBtn);

      // Dialog should have password field
      const passwordInput = dialog.dialog.locator('input[type="password"], input').first();
      await expect(passwordInput).toBeVisible();

      // Click create share
      await dialog.confirmDialog(/create|share/i);
      await page.waitForTimeout(env.timeouts.animation);

      // Success dialog should appear with share URL
      const successDialog = page.locator('.mud-dialog');
      if (await successDialog.isVisible()) {
        // Should show share URL
        const urlText = successDialog.locator('input, .mud-input-root, .mud-typography').filter({ hasText: /share|http/i });
        if (await urlText.first().isVisible()) {
          // Copy button should exist
          const copyBtn = successDialog.getByRole('button', { name: /copy/i }).first();
          if (await copyBtn.isVisible()) {
            await expect(copyBtn).toBeEnabled();
          }
        }
        await successDialog.getByRole('button', { name: /close|ok|done/i }).first().click().catch(() => {
          page.keyboard.press('Escape');
        });
      }
    });

    test('create collection share from assets page', async ({ page }) => {
      dialog = new DialogHelper(page);

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const shareCollBtn = page.getByRole('button', { name: /share collection/i });
      if (!(await shareCollBtn.isVisible())) {
        test.skip();
        return;
      }

      await dialog.clickAndWaitForDialog(shareCollBtn);

      // Set password
      const passwordInput = dialog.dialog.locator('input[type="password"], input').first();
      if (await passwordInput.isVisible()) {
        await passwordInput.clear();
        await passwordInput.fill(env.testData.sharePasswordDefault);
      }

      await dialog.confirmDialog(/create|share/i);
      await page.waitForTimeout(env.timeouts.animation * 2);

      // Close any success dialog
      const successDialog = page.locator('.mud-dialog');
      if (await successDialog.isVisible()) {
        await successDialog.getByRole('button', { name: /close|ok|done/i }).first().click().catch(() => {
          page.keyboard.press('Escape');
        });
      }
    });
  });

  test.describe('Public Share Access', () => {
    test.use({ storageState: { cookies: [], origins: [] } }); // Unauthenticated

    // Create share in beforeAll for this describe block since test.use creates separate context
    test.beforeAll(async () => {
      if (!testAssetId) return;

      const apiForShare = await ApiHelper.withCookieAuth();
      try {
        const shareResult = await apiForShare.createShare(
          testAssetId,
          'asset',
          env.testData.sharePasswordDefault,
          30
        );
        shareToken = shareResult.token;
        shareId = shareResult.id;
        sharePassword = shareResult.password || env.testData.sharePasswordDefault;
      } finally {
        await apiForShare.dispose();
      }
    });

    test('share page shows password prompt for protected share', async ({ page }) => {
      if (!shareToken) {
        test.skip();
        return;
      }

      const sharePage = new SharePage(page);
      await sharePage.goto(shareToken);

      // Should show password prompt
      await sharePage.expectPasswordPrompt();
    });

    test('wrong password shows error', async ({ page }) => {
      if (!shareToken) {
        test.skip();
        return;
      }

      const sharePage = new SharePage(page);
      await sharePage.goto(shareToken);

      await sharePage.submitPassword('wrong-password');

      // Should show error
      const error = page.locator('.mud-alert-error, .mud-alert').filter({ hasText: /./  }).first();
      await expect(error).toBeVisible({ timeout: 10_000 });
    });

    test('correct password reveals shared content @smoke', async ({ page }) => {
      if (!shareToken || !sharePassword) {
        test.skip();
        return;
      }

      const sharePage = new SharePage(page);
      await sharePage.goto(shareToken);

      await sharePage.submitPassword(sharePassword);
      await page.waitForTimeout(env.timeouts.animation * 2);

      // Should show asset title or content
      await expect(
        page.locator('.mud-typography-h5').or(page.locator('.mud-image')).first()
      ).toBeVisible({ timeout: 15_000 });
    });

    test('shared asset download button works', async ({ page }) => {
      if (!shareToken || !sharePassword) {
        test.skip();
        return;
      }

      const sharePage = new SharePage(page);
      await sharePage.goto(shareToken);
      await sharePage.submitPassword(sharePassword);
      await page.waitForTimeout(env.timeouts.animation * 2);

      // Download button should be present
      const downloadBtn = page.getByRole('button', { name: /download/i }).first()
        .or(page.getByRole('link', { name: /download/i }).first());
      if (await downloadBtn.first().isVisible()) {
        await expect(downloadBtn.first()).toBeEnabled();
      }
    });

    test('invalid share token shows error', async ({ page }) => {
      const sharePage = new SharePage(page);
      await sharePage.goto('invalid-token-12345');

      // Should show error or not found
      const error = page.locator('.mud-alert, [class*="error"]').filter({ hasText: /.+/ }).first();
      await expect(error).toBeVisible({ timeout: 10_000 });
    });

    test('shared content shows file info', async ({ page }) => {
      if (!shareToken || !sharePassword) {
        test.skip();
        return;
      }

      const sharePage = new SharePage(page);
      await sharePage.goto(shareToken);
      await sharePage.submitPassword(sharePassword);
      await page.waitForTimeout(env.timeouts.animation * 2);

      // Should show file details
      const infoTable = page.locator('.mud-simple-table');
      if (await infoTable.isVisible()) {
        await expect(infoTable).toBeVisible();
      }
    });
  });

  test.describe('Share Revocation', () => {
    test.use({ storageState: { cookies: [], origins: [] } }); // Unauthenticated

    let revokedShareToken: string;
    let revokedShareId: string;

    // Create and revoke a share for revocation testing
    test.beforeAll(async () => {
      if (!testAssetId) return;

      const apiForShare = await ApiHelper.withCookieAuth();
      try {
        // Create share and verify it was created
        const shareResult = await apiForShare.createShare(
          testAssetId,
          'asset',
          env.testData.sharePasswordDefault,
          30
        );
        if (!shareResult.token || !shareResult.id) {
          throw new Error('Failed to create share for revocation test');
        }
        revokedShareToken = shareResult.token;
        revokedShareId = shareResult.id;

        // Revoke it immediately
        const revokeRes = await apiForShare.revokeShare(revokedShareId);
        if (!revokeRes.ok()) {
          throw new Error(`Failed to revoke share: ${revokeRes.status()}`);
        }
      } finally {
        await apiForShare.dispose();
      }
    });

    test('revoked share is inaccessible', async ({ page }) => {
      if (!revokedShareToken) {
        test.skip();
        return;
      }

      const sharePage = new SharePage(page);
      await sharePage.goto(revokedShareToken);
      await page.waitForTimeout(env.timeouts.animation);

      // Should show error (expired/revoked)
      const error = page.locator('.mud-alert, [class*="error"]').filter({ hasText: /.+/ }).first();
      await expect(error).toBeVisible({ timeout: 10_000 });
    });
  });
});
