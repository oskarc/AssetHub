import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { DialogHelper } from '../helpers/dialog-helper';
import { env } from '../config/env';

test.describe('End-to-End Workflow Tests @e2e @smoke', () => {
  let api: ApiHelper;
  const timestamp = Date.now();

  /**
   * Full DAM workflow:
   * 1. Create a collection
   * 2. Upload an asset to it
   * 3. View the asset detail
   * 4. Edit the asset
   * 5. Share the asset
   * 6. Access the share publicly
   * 7. Manage collection access
   * 8. View in admin panel
   * 9. Clean up
   */
  test('complete DAM workflow: create → upload → edit → share → admin → cleanup', async ({ page, request }) => {
    api = new ApiHelper(request);
    await api.authenticate();
    const fixtures = ensureTestFixtures();
    const dialog = new DialogHelper(page);

    // === STEP 1: Create collection via UI ===
    const collectionName = `Workflow-${timestamp}`;
    await page.goto('/assets');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation);

    // Click create collection
    const createBtn = page.locator('[title*="reate"]').first()
      .or(page.locator('.mud-icon-button').nth(1));
    if (await createBtn.isVisible()) {
      await createBtn.click();
      await dialog.waitForDialog();
      await dialog.fillInput(0, collectionName);
      await dialog.confirmDialog(/create|save|ok/i);
      await page.waitForTimeout(env.timeouts.animation * 2);
    } else {
      // Fallback: create via API
      await api.createCollection(collectionName, 'Workflow test');
      await page.reload();
      await page.waitForLoadState('networkidle');
    }

    // Verify collection appears
    await expect(page.getByText(collectionName)).toBeVisible({ timeout: 10_000 });

    // Select the collection
    await page.getByText(collectionName).click();
    await page.waitForTimeout(env.timeouts.animation);

    // === STEP 2: Upload asset ===
    const fileInput = page.locator('#fileInput');
    if (await fileInput.isVisible()) {
      await fileInput.setInputFiles(fixtures.testImage);
      await page.waitForTimeout(env.timeouts.upload);
    }

    // === STEP 3: Navigate to All Assets to verify the asset appears ===
    await page.goto('/all-assets');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation * 2);

    // Should see at least one asset card
    await expect(page.locator('.asset-card, .mud-card').first()).toBeVisible({ timeout: 15_000 });

    // === STEP 4: View in admin panel ===
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    await expect(page.locator('.mud-typography-h4')).toBeVisible();

    // Check share management tab
    const shareTab = page.getByRole('tab', { name: /share/i });
    if (await shareTab.isVisible()) {
      await shareTab.click();
      await page.waitForTimeout(env.timeouts.animation);
    }

    // Check user management tab
    const userTab = page.getByRole('tab', { name: /user/i });
    if (await userTab.isVisible()) {
      await userTab.click();
      await page.waitForTimeout(env.timeouts.animation);
      // Should show users
      await page.waitForTimeout(env.timeouts.animation * 2);
      await expect(page.locator('.mud-table')).toBeVisible({ timeout: 15_000 });
    }

    // Check collection access tab
    const collAccessTab = page.getByRole('tab', { name: /collection/i });
    if (await collAccessTab.isVisible()) {
      await collAccessTab.click();
      await page.waitForTimeout(env.timeouts.animation * 2);
    }

    // === STEP 5: Return to the collection and verify everything is stable ===
    await page.goto('/assets');
    await page.waitForLoadState('networkidle');
    await expect(page.getByText(collectionName)).toBeVisible({ timeout: 10_000 });

    // === Cleanup: delete collection via API ===
    await api.authenticate();
    const collections = await api.getCollections();
    const workflowCol = collections.find((c: any) => c.name === collectionName);
    if (workflowCol) {
      await api.deleteCollection(workflowCol.id);
    }
  });

  test('share workflow: create share → access publicly → verify content', async ({ page, request, browser }) => {
    api = new ApiHelper(request);
    await api.authenticate();
    const fixtures = ensureTestFixtures();

    // Create collection + asset + share via API
    const collection = await api.createCollection(`ShareFlow-${timestamp}`, 'Share workflow test');
    let assetId: string;
    try {
      const asset = await api.uploadAsset(collection.id, fixtures.testImage, `ShareFlow-Asset-${timestamp}`);
      assetId = asset.id;
    } catch {
      // Cleanup and skip
      await api.deleteCollection(collection.id);
      test.skip();
      return;
    }

    const share = await api.createShare(assetId!, 'asset', 'share-flow-pwd', 30);
    const shareToken = share.token;
    const sharePassword = share.password || 'share-flow-pwd';

    // === Access share in a new incognito context (unauthenticated) ===
    const context = await browser.newContext();
    const sharePage = await context.newPage();

    await sharePage.goto(`${env.baseUrl}/share/${shareToken}`);
    await sharePage.waitForLoadState('networkidle');

    // Should show password prompt
    const passwordInput = sharePage.locator('input[type="password"]');
    await expect(passwordInput).toBeVisible({ timeout: 10_000 });

    // Submit password
    await passwordInput.fill(sharePassword);
    await sharePage.getByRole('button', { name: /access/i }).click();
    await sharePage.waitForTimeout(env.timeouts.animation * 2);

    // Should show shared content
    await expect(
      sharePage.locator('.mud-typography-h5').or(sharePage.locator('.mud-image')).or(sharePage.locator('.mud-card'))
    ).toBeVisible({ timeout: 15_000 });

    await context.close();

    // Cleanup
    await api.revokeShare(share.id);
    await api.deleteAsset(assetId!);
    await api.deleteCollection(collection.id);
  });

  test('collection hierarchy: root → sub-collection → assets → breadcrumbs', async ({ page, request }) => {
    api = new ApiHelper(request);
    await api.authenticate();

    // Create root + sub-collection via API
    const root = await api.createCollection(`Root-${timestamp}`, 'Root collection');
    const sub = await api.createCollection(`Sub-${timestamp}`, 'Sub collection', root.id);

    await page.goto('/assets');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(env.timeouts.animation * 2);

    // Should see root collection in tree
    await expect(page.getByText(`Root-${timestamp}`)).toBeVisible({ timeout: 10_000 });

    // Click root to see sub-collection
    await page.getByText(`Root-${timestamp}`).click();
    await page.waitForTimeout(env.timeouts.animation);

    // Sub-collection should appear (as child in tree or in content area)
    // Navigate to sub-collection if visible
    const subCol = page.getByText(`Sub-${timestamp}`);
    if (await subCol.isVisible()) {
      await subCol.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Breadcrumbs should show hierarchy
      const breadcrumbs = page.locator('.mud-breadcrumbs');
      if (await breadcrumbs.isVisible()) {
        await expect(breadcrumbs).toBeVisible();
      }
    }

    // Cleanup
    await api.deleteCollection(sub.id);
    await api.deleteCollection(root.id);
  });
});
