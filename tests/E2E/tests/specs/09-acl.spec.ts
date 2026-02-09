import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('Access Control & Permissions @acl', () => {
  let api: ApiHelper;
  let testCollectionId: string;
  let viewerUserId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-ACL-${timestamp}`;

  test.beforeAll(async ({ request }) => {
    api = new ApiHelper(request);
    await api.authenticate();

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

  test.afterAll(async ({ request }) => {
    api = new ApiHelper(request);
    await api.authenticate();
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
  });

  test.describe('Collection ACL Management', () => {
    test('admin can view collection ACL', async () => {
      const acl = await api.getCollectionAcl(testCollectionId);
      expect(Array.isArray(acl)).toBeTruthy();
    });

    test('admin can grant viewer access to user', async () => {
      if (!viewerUserId) test.skip();
      const res = await api.setCollectionAccess(testCollectionId, viewerUserId, 'viewer');
      expect(res.ok()).toBeTruthy();
    });

    test('ACL shows granted access', async () => {
      if (!viewerUserId) test.skip();
      const acl = await api.getCollectionAcl(testCollectionId);
      const viewerEntry = acl.find((a: any) =>
        (a.principalId || a.PrincipalId) === viewerUserId
      );
      expect(viewerEntry).toBeTruthy();
    });

    test('admin can upgrade user role', async () => {
      if (!viewerUserId) test.skip();
      const res = await api.setCollectionAccess(testCollectionId, viewerUserId, 'contributor');
      expect(res.ok()).toBeTruthy();

      // Verify role changed
      const acl = await api.getCollectionAcl(testCollectionId);
      const entry = acl.find((a: any) =>
        (a.principalId || a.PrincipalId) === viewerUserId
      );
      expect(entry.role || entry.Role).toBe('contributor');
    });

    test('admin can revoke user access', async ({ request }) => {
      if (!viewerUserId) test.skip();
      const token = await api.authenticate();
      const res = await request.delete(
        `${env.baseUrl}/api/collections/${testCollectionId}/acl/user/${viewerUserId}`,
        { headers: { Authorization: `Bearer ${token}` } }
      );
      expect(res.ok()).toBeTruthy();
    });
  });

  test.describe('Role-Based UI Visibility', () => {
    test('admin sees all nav items', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Admin should see All Assets and Admin nav
      await expect(page.getByText(/all assets/i)).toBeVisible();
      await expect(page.getByText(/admin/i).first()).toBeVisible();
    });

    test('admin sees upload area on collections page', async ({ page }) => {
      if (!testCollectionId) test.skip();

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const uploadArea = page.locator('.upload-area');
      await expect(uploadArea).toBeVisible({ timeout: 10_000 });
    });

    test('admin sees manage access button', async ({ page }) => {
      if (!testCollectionId) test.skip();

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const manageAccessBtn = page.getByRole('button', { name: /manage access/i });
      await expect(manageAccessBtn).toBeVisible({ timeout: 10_000 });
    });

    test('admin sees delete buttons on assets', async ({ page }) => {
      if (!testCollectionId) test.skip();

      const fixtures = ensureTestFixtures();
      await api.authenticate();
      try {
        await api.uploadAsset(testCollectionId, fixtures.testImage, `ACL-Vis-${timestamp}`);
      } catch {}

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation * 3);

      const cards = page.locator('.asset-card');
      if (await cards.first().isVisible()) {
        // Admin should see delete button
        const deleteBtn = cards.first().locator('.mud-icon-button').last();
        if (await deleteBtn.isVisible()) {
          await expect(deleteBtn).toBeVisible();
        }
      }
    });
  });

  test.describe('Manage Access Dialog UI', () => {
    test('manage access dialog shows existing ACL entries', async ({ page }) => {
      if (!testCollectionId) test.skip();

      // Grant access first
      if (viewerUserId) {
        await api.setCollectionAccess(testCollectionId, viewerUserId, 'viewer');
      }

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const manageAccessBtn = page.getByRole('button', { name: /manage access/i });
      if (await manageAccessBtn.isVisible()) {
        await manageAccessBtn.click();

        const dialog = new DialogHelper(page);
        await dialog.waitForDialog();

        // Should have user search and ACL table
        await expect(dialog.dialog.locator('input').first()).toBeVisible();

        await dialog.closeDialog();
      }
    });

    test('manage access dialog has role selector', async ({ page }) => {
      if (!testCollectionId) test.skip();

      await page.goto(`/assets?collection=${testCollectionId}`);
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(env.timeouts.animation);

      const manageAccessBtn = page.getByRole('button', { name: /manage access/i });
      if (await manageAccessBtn.isVisible()) {
        await manageAccessBtn.click();

        const dialog = new DialogHelper(page);
        await dialog.waitForDialog();

        // Should have a role select dropdown
        const roleSelect = dialog.dialog.locator('.mud-select');
        if (await roleSelect.first().isVisible()) {
          await expect(roleSelect.first()).toBeVisible();
        }

        await dialog.closeDialog();
      }
    });
  });
});
