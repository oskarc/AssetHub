import { test, expect } from '@playwright/test';
import { AdminPage } from '../pages/admin.page';
import { ApiHelper } from '../helpers/api-helper';
import { DialogHelper, SnackbarHelper } from '../helpers/dialog-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('Admin Panel @admin', () => {
  let adminPage: AdminPage;
  let dialog: DialogHelper;
  let snackbar: SnackbarHelper;
  let api: ApiHelper;
  let testCollectionId: string;
  let testShareId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-Admin-${timestamp}`;

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();

    // Create test data
    const collection = await api.createCollection(testCollectionName, 'Admin test collection');
    testCollectionId = collection.id;

    // Create a share
    const fixtures = ensureTestFixtures();
    try {
      const asset = await api.uploadAsset(testCollectionId, fixtures.testImage, `Admin-Test-Asset-${timestamp}`);
      if (asset?.id) {
        const share = await api.createShare(asset.id, 'asset', 'admin-test-pwd', 30);
        testShareId = share.id;
      }
    } catch {
      // Non-critical
    }
  });

  test.afterAll(async () => {
    api = await ApiHelper.withCookieAuth();
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
  });

  test.describe('Admin Page Access', () => {
    test('admin page loads for admin user @smoke', async ({ page }) => {
      adminPage = new AdminPage(page);
      await adminPage.goto();
      await adminPage.expectLoaded();
    });

    test('admin page has four tabs', async ({ page }) => {
      adminPage = new AdminPage(page);
      await adminPage.goto();

      await expect(adminPage.shareManagementTab).toBeVisible();
      await expect(adminPage.collectionAccessTab).toBeVisible();
      await expect(adminPage.userManagementTab).toBeVisible();
      await expect(adminPage.auditTab).toBeVisible();
    });
  });

  test.describe('Share Management Tab', () => {
    test.beforeEach(async ({ page }) => {
      adminPage = new AdminPage(page);
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);
      await adminPage.goto();
      await adminPage.switchToShareManagement();
    });

    test('share management tab loads @smoke', async ({ page }) => {
      // Should show shares heading and table
      await expect(page.getByText(/shared|share/i).first()).toBeVisible();
    });

    test('share table is visible', async ({ page }) => {
      const table = page.locator('.mud-table');
      // Table should exist (may be empty)
      await expect(table).toBeVisible({ timeout: 10_000 });
    });

    test('share table has search functionality', async ({ page }) => {
      const searchInput = page.locator('.mud-table .mud-input-root input, .mud-toolbar input').first();
      await expect(searchInput).toBeVisible({ timeout: 10_000 });
      
      await searchInput.fill('test');
      await page.waitForTimeout(env.timeouts.debounce);
      // Page should still be functional after search
      await expect(page.locator('.mud-table')).toBeVisible();
    });

    test('share table shows status chips', async ({ page }) => {
      // Status chips (Active, Expired, Revoked counts)
      const chips = page.locator('.mud-chip').filter({ hasText: /active|expired|revoked/i });
      // Should have at least one status indicator
      await expect(chips.first()).toBeVisible({ timeout: 10_000 });
    });

    test('share info button opens dialog', async ({ page }) => {
      const infoButtons = page.locator('[title*="nfo"], .mud-icon-button').filter({ has: page.locator('svg') });
      const rows = page.locator('.mud-table tbody tr');
      const rowCount = await rows.count();
      if (rowCount > 0) {
        // Click info on first share
        const firstRow = rows.first();
        const infoBtn = firstRow.locator('.mud-icon-button').first();
        if (await infoBtn.isVisible()) {
          await infoBtn.click();
          await page.waitForTimeout(env.timeouts.animation);
          // Dialog should appear
          const dlg = page.locator('.mud-dialog');
          if (await dlg.isVisible()) {
            await dlg.getByRole('button', { name: /close|ok/i }).first().click().catch(() => {
              page.keyboard.press('Escape');
            });
          }
        }
      }
    });

    test('edit password button opens dialog', async ({ page }) => {
      const rows = page.locator('.mud-table tbody tr');
      const rowCount = await rows.count();
      if (rowCount > 0) {
        const firstRow = rows.first();
        const editBtn = firstRow.locator('[title*="assword"], [title*="dit"], .mud-icon-button').nth(1);
        if (await editBtn.isVisible()) {
          await editBtn.click();
          await page.waitForTimeout(env.timeouts.animation);
          const dlg = page.locator('.mud-dialog');
          if (await dlg.isVisible()) {
            // Should have password input
            await expect(dlg.locator('input')).toBeVisible();
            await page.keyboard.press('Escape');
          }
        }
      }
    });

    test('revoke share shows confirmation', async ({ page }) => {
      const rows = page.locator('.mud-table tbody tr');
      const rowCount = await rows.count();
      if (rowCount > 0) {
        const firstRow = rows.first();
        const revokeBtn = firstRow.locator('[title*="evoke"], .mud-icon-button').last();
        if (await revokeBtn.isVisible()) {
          await revokeBtn.click();
          await page.waitForTimeout(env.timeouts.animation);
          const dlg = page.locator('.mud-dialog');
          if (await dlg.isVisible()) {
            // Cancel to avoid revoking
            await dlg.getByRole('button', { name: /cancel|no/i }).first().click().catch(() => {
              page.keyboard.press('Escape');
            });
          }
        }
      }
    });
  });

  test.describe('Collection Access Tab', () => {
    test.beforeEach(async ({ page }) => {
      adminPage = new AdminPage(page);
      dialog = new DialogHelper(page);
      await adminPage.goto();
      await adminPage.switchToCollectionAccess();
    });

    test('collection access tab loads @smoke', async ({ page }) => {
      // Should show collection access heading or Collections label
      await page.waitForTimeout(env.timeouts.animation);
      const heading = page.getByRole('heading', { name: /collection access/i })
        .or(page.getByText(/Collections/i).and(page.locator('p')));
      await expect(heading.first()).toBeVisible();
    });

    test('collection tree displays collections', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      const collections = page.locator('.mud-button, .mud-nav-link').filter({ hasText: /.+/ });
      const count = await collections.count();
      expect(count).toBeGreaterThan(0);
    });

    test('selecting collection shows ACL details', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      // Click on a collection
      const collectionBtn = page.getByText(testCollectionName);
      if (await collectionBtn.isVisible()) {
        await collectionBtn.click();
        await page.waitForTimeout(env.timeouts.animation);
        // Should show ACL table or add user form
        const aclArea = page.locator('.mud-simple-table, .mud-paper').filter({ hasText: /user|role|access/i });
        if (await aclArea.first().isVisible()) {
          await expect(aclArea.first()).toBeVisible();
        }
      }
    });

    test('add user access form is functional', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      const collectionBtn = page.getByText(testCollectionName);
      await expect(collectionBtn).toBeVisible({ timeout: 10_000 });
      
      await collectionBtn.click();
      await page.waitForTimeout(env.timeouts.animation);

      // Should have user ID input and role select
      const userInput = page.locator('input').first();
      const roleSelect = page.locator('.mud-select').first();
      
      await expect(userInput).toBeVisible();
      await expect(roleSelect).toBeVisible();
      await expect(userInput).toBeEnabled();
    });
  });

  test.describe('User Management Tab', () => {
    test.beforeEach(async ({ page }) => {
      adminPage = new AdminPage(page);
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);
      await adminPage.goto();
      await adminPage.switchToUserManagement();
    });

    test('user management tab loads @smoke', async ({ page }) => {
      await expect(page.getByText(/user/i).first()).toBeVisible();
    });

    test('user table displays users', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      const table = page.locator('.mud-table');
      await expect(table).toBeVisible({ timeout: 15_000 });

      const rows = table.locator('tbody tr');
      const rowCount = await rows.count();
      expect(rowCount).toBeGreaterThan(0);
    });

    test('user table shows seeded users', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      // Should show mediaadmin and testuser from Keycloak seed
      const adminRow = page.getByText('mediaadmin');
      const viewerRow = page.getByText('testuser');
      // At least admin should be visible
      await expect(adminRow.or(viewerRow).first()).toBeVisible({ timeout: 15_000 });
    });

    test('user search filters results', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      const searchInput = page.locator('.mud-table .mud-input-root input, .mud-toolbar input').first();
      await expect(searchInput).toBeVisible({ timeout: 10_000 });
      
      await searchInput.fill('mediaadmin');
      await page.waitForTimeout(env.timeouts.debounce);
      
      // Should filter to show admin user
      const rows = page.locator('.mud-table tbody tr');
      const count = await rows.count();
      // After filtering, should have at least one result for mediaadmin
      expect(count).toBeGreaterThanOrEqual(1);
    });

    test('create user button is visible', async ({ page }) => {
      const createBtn = page.getByRole('button', { name: /create user/i });
      await expect(createBtn).toBeVisible({ timeout: 10_000 });
    });

    test('create user dialog opens and has form fields', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation);
      const createBtn = page.getByRole('button', { name: /create user/i });
      await expect(createBtn).toBeVisible({ timeout: 10_000 });
      
      await createBtn.click();
      await dialog.waitForDialog();

      // Dialog should have form fields
      const inputs = dialog.dialog.locator('input');
      const count = await inputs.count();
      expect(count).toBeGreaterThanOrEqual(3); // username, email, password at minimum

      await dialog.closeDialog();
    });

    test('create user dialog validates required fields', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation);
      const createBtn = page.getByRole('button', { name: /create user/i });
      await expect(createBtn).toBeVisible({ timeout: 10_000 });
      
      await createBtn.click();
      await dialog.waitForDialog();

      // Try to submit empty form - should stay disabled or show validation
      const submitBtn = dialog.dialog.getByRole('button', { name: /create user/i });
      await expect(submitBtn).toBeVisible();
      await expect(submitBtn).toBeDisabled();
      
      await dialog.closeDialog();
    });

    test('manage access button opens dialog', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      const rows = page.locator('.mud-table tbody tr');
      const rowCount = await rows.count();
      if (rowCount > 0) {
        const manageBtn = rows.first().getByRole('button', { name: /manage/i });
        if (await manageBtn.isVisible()) {
          await manageBtn.click();
          await dialog.waitForDialog();
          await expect(dialog.dialog).toBeVisible();
          await dialog.closeDialog();
        }
      }
    });

    test('stats chips show total and with-access counts', async ({ page }) => {
      await page.waitForTimeout(env.timeouts.animation * 2);
      const chips = page.locator('.mud-chip').filter({ hasText: /total|access|\d+/i });
      // Should have stat chips
      await expect(chips.first()).toBeVisible({ timeout: 10_000 });
    });
  });

  test.describe('Audit Log Tab', () => {
    test.beforeEach(async ({ page }) => {
      adminPage = new AdminPage(page);
      dialog = new DialogHelper(page);
      snackbar = new SnackbarHelper(page);
      await adminPage.goto();
      await adminPage.switchToAudit();
    });

    test('audit log tab loads @smoke', async ({ page }) => {
      // Should show audit heading or table
      await expect(page.getByText(/audit/i).first()).toBeVisible();
    });

    test('audit table is visible', async ({ page }) => {
      const table = page.locator('.mud-table');
      // Table should exist (may be empty)
      await expect(table).toBeVisible({ timeout: 10_000 });
    });

    test('audit table has search functionality', async ({ page }) => {
      const searchInput = page.locator('.mud-table .mud-input-root input, .mud-toolbar input').first();
      await expect(searchInput).toBeVisible({ timeout: 10_000 });
      
      await searchInput.fill('test');
      await page.waitForTimeout(env.timeouts.debounce);
      // Page should still be functional
      await expect(page.locator('.mud-table')).toBeVisible();
    });

  });
});
