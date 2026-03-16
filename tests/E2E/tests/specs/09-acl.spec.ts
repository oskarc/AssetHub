import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';
import { waitForBlazorInteractive } from '../helpers/blazor-helper';

test.describe('Access Control & Permissions @acl', () => {
  let api: ApiHelper;
  let testCollectionId: string;
  let viewerUserId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-ACL-${timestamp}`;

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();

    // Create a test collection
    const collection = await api.createCollection(testCollectionName, 'ACL test collection');
    testCollectionId = collection.id;

    // Get viewer user ID from Keycloak
    const users = await api.getKeycloakUsers();
    const viewer = users.find((u: any) => u.username === env.viewerUser.username);
    if (viewer) {
      viewerUserId = viewer.id;
    }
  });

  test.afterAll(async () => {
    api = await ApiHelper.withCookieAuth();
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
  });

  test.describe('Collection ACL Management', () => {
    test('admin can view collection ACL', async () => {
      api = await ApiHelper.withCookieAuth();
      const acl = await api.getCollectionAcl(testCollectionId);
      expect(Array.isArray(acl)).toBeTruthy();
    });

    test('admin can grant viewer access to user', async () => {
      if (!viewerUserId) test.skip();
      api = await ApiHelper.withCookieAuth();
      const res = await api.setCollectionAccess(testCollectionId, viewerUserId, 'viewer');
      expect(res.ok()).toBeTruthy();
    });

    test('ACL shows granted access', async () => {
      if (!viewerUserId) test.skip();
      api = await ApiHelper.withCookieAuth();
      const acl = await api.getCollectionAcl(testCollectionId);
      const viewerEntry = acl.find((a: any) =>
        (a.principalId || a.PrincipalId) === viewerUserId
      );
      expect(viewerEntry).toBeTruthy();
    });

    test('admin can upgrade user role', async () => {
      if (!viewerUserId) test.skip();
      api = await ApiHelper.withCookieAuth();
      const res = await api.setCollectionAccess(testCollectionId, viewerUserId, 'contributor');
      expect(res.ok()).toBeTruthy();

      // Verify role changed
      const acl = await api.getCollectionAcl(testCollectionId);
      const entry = acl.find((a: any) =>
        (a.principalId || a.PrincipalId) === viewerUserId
      );
      expect(entry.role || entry.Role).toBe('contributor');
    });

    test('admin can revoke user access', async () => {
      if (!viewerUserId) test.skip();
      api = await ApiHelper.withCookieAuth();
      const res = await api.delete(
        `${env.baseUrl}/api/v1/collections/${testCollectionId}/acl/user/${viewerUserId}`
      );
      expect(res.ok()).toBeTruthy();
      await api.dispose();
    });
  });

  test.describe('Role-Based UI Visibility', () => {
    test('admin sees all nav items', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');
      await waitForBlazorInteractive(page);

      // Admin should see All Assets and Admin nav
      await expect(page.getByText(/all assets/i)).toBeVisible();
      await expect(page.getByText(/admin/i).first()).toBeVisible();
    });

    test('admin sees upload area on collections page', async ({ page }) => {
      if (!testCollectionId) test.skip();

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await waitForBlazorInteractive(page);

      const uploadArea = page.locator('.upload-area');
      await expect(uploadArea).toBeVisible({ timeout: 10_000 });
    });

    test('admin sees manage access button', async ({ page }) => {
      if (!testCollectionId) test.skip();

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await waitForBlazorInteractive(page);

      const manageAccessBtn = page.getByRole('button', { name: /manage access/i });
      await expect(manageAccessBtn).toBeVisible({ timeout: 10_000 });
    });

    test('admin sees delete buttons on assets', async ({ page }) => {
      if (!testCollectionId) test.skip();

      const fixtures = ensureTestFixtures();
      api = await ApiHelper.withCookieAuth();
      try {
        await api.uploadAsset(testCollectionId, fixtures.testImage, `ACL-Vis-${timestamp}`);
      } catch {}

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await waitForBlazorInteractive(page);
      await page.waitForTimeout(env.timeouts.animation * 2);

      const cards = page.locator('.asset-card');
      await expect(cards.first()).toBeVisible({ timeout: 15_000 });
      
      // Admin should see delete button
      const deleteBtn = cards.first().locator('.mud-icon-button').last();
      await expect(deleteBtn).toBeVisible();
    });
  });

  test.describe('Manage Access Dialog UI', () => {
    test('manage access dialog shows user search and ACL entries', async ({ page }) => {
      if (!testCollectionId) test.skip();

      // Grant access first
      if (viewerUserId) {
        api = await ApiHelper.withCookieAuth();
        await api.setCollectionAccess(testCollectionId, viewerUserId, 'viewer');
      }

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await waitForBlazorInteractive(page);

      const manageAccessBtn = page.getByRole('button', { name: /manage access/i });
      await expect(manageAccessBtn).toBeVisible({ timeout: 10_000 });
      
      const dialog = new DialogHelper(page);
      await dialog.clickAndWaitForDialog(manageAccessBtn);

      // Should have user search input
      await expect(dialog.dialog.locator('input').first()).toBeVisible();

      await dialog.closeDialog();
    });

    test('manage access dialog has role selector', async ({ page }) => {
      if (!testCollectionId) test.skip();

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await waitForBlazorInteractive(page);

      const manageAccessBtn = page.getByRole('button', { name: /manage access/i });
      await expect(manageAccessBtn).toBeVisible({ timeout: 10_000 });

      const dialog = new DialogHelper(page);
      await dialog.clickAndWaitForDialog(manageAccessBtn);

      // Should have a role select dropdown
      const roleSelect = dialog.dialog.locator('.mud-select');
      await expect(roleSelect.first()).toBeVisible();

      await dialog.closeDialog();
    });
  });
});
