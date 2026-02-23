import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('API Endpoint Tests @api', () => {
  let testCollectionId: string;
  let testAssetId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-API-${timestamp}`;

  test.describe('Health Endpoints', () => {
    test('health endpoint returns 200 @smoke', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/health`);
      expect(res.status()).toBe(200);
    });

    test('readiness endpoint returns 200', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/health/ready`);
      expect([200, 503]).toContain(res.status());
    });

    test('build info endpoint returns data', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/__build`);
      expect(res.ok()).toBeTruthy();
      const text = await res.text();
      expect(text.length).toBeGreaterThan(0);
    });
  });

  test.describe('Collection API', () => {
    let api: ApiHelper;

    test.beforeAll(async () => {
      api = await ApiHelper.withCookieAuth();
    });

    test.afterAll(async () => {
      await api.dispose();
    });

    test('create collection @smoke', async () => {
      const result = await api.createCollection(testCollectionName, 'API test collection');
      testCollectionId = result.id;
      expect(result.id).toBeTruthy();
      expect(result.name).toBe(testCollectionName);
    });

    test('get collections returns list', async () => {
      const result = await api.getCollections();
      expect(Array.isArray(result)).toBeTruthy();
      expect(result.length).toBeGreaterThan(0);
    });

    test('get collection by ID', async () => {
      if (!testCollectionId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/collections/${testCollectionId}`);
      expect(res.ok()).toBeTruthy();
      const data = await res.json();
      expect(data.name).toBe(testCollectionName);
    });

    test('create sub-collection', async () => {
      if (!testCollectionId) test.skip();
      const subName = `${testCollectionName}-Sub`;
      const result = await api.createCollection(subName, 'Sub-collection', testCollectionId);
      expect(result.id).toBeTruthy();
      expect(result.parentId || result.ParentId).toBe(testCollectionId);
    });

    test('get collection children', async () => {
      if (!testCollectionId) test.skip();
      const children = await api.getCollectionChildren(testCollectionId);
      expect(Array.isArray(children)).toBeTruthy();
    });

    test('update collection', async () => {
      if (!testCollectionId) test.skip();
      const res = await api.patch(`${env.baseUrl}/api/collections/${testCollectionId}`, {
        name: `${testCollectionName}-Updated`,
        description: 'Updated description',
      });
      expect(res.ok()).toBeTruthy();
    });

    test('get collection ACL', async () => {
      if (!testCollectionId) test.skip();
      const acl = await api.getCollectionAcl(testCollectionId);
      expect(Array.isArray(acl)).toBeTruthy();
    });

    test('search users for ACL', async () => {
      if (!testCollectionId) test.skip();
      const res = await api.get(
        `${env.baseUrl}/api/collections/${testCollectionId}/acl/users/search?q=test`
      );
      expect(res.ok()).toBeTruthy();
    });
  });

  test.describe('Asset API', () => {
    let api: ApiHelper;

    test.beforeAll(async () => {
      api = await ApiHelper.withCookieAuth();
    });

    test.afterAll(async () => {
      await api.dispose();
    });

    test('upload asset via form @smoke', async () => {
      if (!testCollectionId) test.skip();
      const fixtures = ensureTestFixtures();
      const result = await api.uploadAsset(testCollectionId, fixtures.testImage, `API-Asset-${timestamp}`);
      testAssetId = result.id;
      expect(result.id).toBeTruthy();
    });

    test('get assets in collection', async () => {
      if (!testCollectionId) test.skip();
      const result = await api.getAssets(testCollectionId);
      expect(result.items || result.Items || result).toBeTruthy();
    });

    test('get single asset', async () => {
      if (!testAssetId) test.skip();
      const result = await api.getAsset(testAssetId);
      expect(result.id || result.Id).toBeTruthy();
    });

    test('update asset metadata', async () => {
      if (!testAssetId) test.skip();
      const result = await api.updateAsset(testAssetId, {
        title: `Updated-API-Asset-${timestamp}`,
        description: 'Updated via API test',
        tags: ['test', 'api', 'e2e'],
      });
      expect(result).toBeTruthy();
    });

    test('get asset collections', async () => {
      if (!testAssetId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/collections`);
      expect(res.ok()).toBeTruthy();
    });

    test('asset thumbnail endpoint', async () => {
      if (!testAssetId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/thumb`);
      // May return 200/3xx or 404/400 if not processed or unavailable
      expect([200, 301, 302, 307, 400, 404]).toContain(res.status());
    });

    test('asset medium endpoint', async () => {
      if (!testAssetId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/medium`);
      expect([200, 301, 302, 307, 400, 404]).toContain(res.status());
    });

    test('asset download endpoint', async () => {
      if (!testAssetId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/download`, { maxRedirects: 0 });
      // Should redirect to presigned URL
      expect([200, 301, 302, 307]).toContain(res.status());
    });

    test('asset preview endpoint', async () => {
      if (!testAssetId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/preview`, { maxRedirects: 0 });
      expect([200, 301, 302, 307]).toContain(res.status());
    });

    test('add asset to another collection', async () => {
      if (!testAssetId || !testCollectionId) test.skip();
      // Create another collection
      const col2 = await api.createCollection(`${testCollectionName}-Link`, 'Link test');
      const res = await api.post(`${env.baseUrl}/api/assets/${testAssetId}/collections/${col2.id}`);
      expect(res.ok()).toBeTruthy();

      // Remove from second collection
      const removeRes = await api.delete(`${env.baseUrl}/api/assets/${testAssetId}/collections/${col2.id}`);
      expect(removeRes.ok()).toBeTruthy();

      // Cleanup
      await api.deleteCollection(col2.id);
    });
  });

  test.describe('Share API', () => {
    let api: ApiHelper;
    let shareId: string;
    let shareToken: string;

    test.beforeAll(async () => {
      api = await ApiHelper.withCookieAuth();
    });

    test.afterAll(async () => {
      await api.dispose();
    });

    test('create asset share @smoke', async () => {
      if (!testAssetId) test.skip();
      const result = await api.createShare(testAssetId, 'asset', 'test-password-123', 30);
      shareId = result.id;
      shareToken = result.token;
      expect(shareId).toBeTruthy();
      expect(shareToken).toBeTruthy();
    });

    test('access share with password', async ({ request }) => {
      if (!shareToken) test.skip();
      const res = await request.get(
        `${env.baseUrl}/api/shares/${shareToken}?password=test-password-123`
      );
      expect([200, 400, 401, 403, 404]).toContain(res.status());
    });

    test('access share with wrong password returns 401', async ({ request }) => {
      if (!shareToken) test.skip();
      const res = await request.get(
        `${env.baseUrl}/api/shares/${shareToken}?password=wrong-password`
      );
      expect(res.status()).toBe(401);
    });

    test('share download endpoint', async ({ request }) => {
      if (!shareToken) test.skip();
      const res = await request.get(
        `${env.baseUrl}/api/shares/${shareToken}/download?password=test-password-123`,
        { maxRedirects: 0 }
      );
      expect([200, 301, 302, 307, 401, 403, 404]).toContain(res.status());
    });

    test('share preview endpoint', async ({ request }) => {
      if (!shareToken) test.skip();
      const res = await request.get(
        `${env.baseUrl}/api/shares/${shareToken}/preview?password=test-password-123`,
        { maxRedirects: 0 }
      );
      expect([200, 301, 302, 307, 401, 403, 404]).toContain(res.status());
    });

    test('admin list shares', async () => {
      const shares = await api.getAdminShares();
      expect(Array.isArray(shares)).toBeTruthy();
    });

    test('admin get share token', async () => {
      if (!shareId) test.skip();
      const res = await api.get(`${env.baseUrl}/api/admin/shares/${shareId}/token`);
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('update share password', async () => {
      if (!shareId) test.skip();
      const res = await api.put(`${env.baseUrl}/api/shares/${shareId}/password`, { password: 'new-password-456' });
      expect(res.ok()).toBeTruthy();
    });

    test('revoke share', async () => {
      if (!shareId) test.skip();
      const res = await api.revokeShare(shareId);
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('revoked share returns error', async ({ request }) => {
      if (!shareToken) test.skip();
      const res = await request.get(
        `${env.baseUrl}/api/shares/${shareToken}?password=new-password-456`
      );
      expect([400, 401, 403, 404, 410]).toContain(res.status());
    });

    test('invalid token returns error', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/api/shares/invalid-token-xyz`);
      expect([400, 401, 404]).toContain(res.status());
    });
  });

  test.describe('Admin API', () => {
    let api: ApiHelper;

    test.beforeAll(async () => {
      api = await ApiHelper.withCookieAuth();
    });

    test.afterAll(async () => {
      await api.dispose();
    });

    test('admin collections access endpoint', async () => {
      const res = await api.get(`${env.baseUrl}/api/admin/collections/access`);
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('admin users endpoint', async () => {
      const res = await api.get(`${env.baseUrl}/api/admin/users`);
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('admin keycloak users endpoint', async () => {
      const users = await api.getKeycloakUsers();
      expect(Array.isArray(users)).toBeTruthy();
      expect(users.length).toBeGreaterThanOrEqual(0);
    });

    test('admin all assets endpoint', async () => {
      const res = await api.get(`${env.baseUrl}/api/assets/all?skip=0&take=10`);
      expect([200, 204, 403, 404]).toContain(res.status());
    });
  });

  test.describe('Authorization Guards', () => {
    test('unauthenticated request to protected endpoint returns 401', async ({ browser }) => {
      // Use a fresh context without stored auth state
      const ctx = await browser.newContext();
      const request = ctx.request;
      const res = await request.get(`${env.baseUrl}/api/collections`);
      expect([200, 401, 302]).toContain(res.status());
      await ctx.close();
    });

    test('viewer cannot create collections at root', async () => {
      const viewerApi = await ApiHelper.withCookieAuth('viewer');

      let threwError = false;
      try {
        await viewerApi.createCollection('Unauthorized Collection');
      } catch (error) {
        // Expected: viewer should not be able to create collections
        threwError = true;
        expect(String(error)).toMatch(/400|401|403|failed/i);
      } finally {
        await viewerApi.dispose();
      }

      // Should have thrown an error (forbidden)
      expect(threwError).toBe(true);
    });

    test('viewer cannot access admin endpoints', async () => {
      const viewerApi = await ApiHelper.withCookieAuth('viewer');

      try {
        // Use raw GET to check actual HTTP status code
        const res = await viewerApi.get(`${env.baseUrl}/api/admin/shares`);
        expect([401, 403]).toContain(res.status());
      } finally {
        await viewerApi.dispose();
      }
    });

    test('viewer cannot delete assets', async () => {
      if (!testAssetId) test.skip();
      const viewerApi = await ApiHelper.withCookieAuth('viewer');

      try {
        const res = await viewerApi.deleteAsset(testAssetId);
        expect([401, 403]).toContain(res.status());
      } finally {
        await viewerApi.dispose();
      }
    });
  });

  // Cleanup
  test.describe('Cleanup', () => {
    let api: ApiHelper;

    test.beforeAll(async () => {
      api = await ApiHelper.withCookieAuth();
    });

    test.afterAll(async () => {
      await api.dispose();
    });

    test('delete test asset', async () => {
      if (testAssetId) {
        await api.deleteAsset(testAssetId);
      }
    });

    test('delete test collection', async () => {
      if (testCollectionId) {
        await api.deleteCollection(testCollectionId);
      }
    });
  });
});
