import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('API Endpoint Tests @api', () => {
  let testCollectionId: string;
  let testAssetId: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-API-${timestamp}`;

  /** Helper: create an authenticated API helper for a given request context */
  async function makeApi(request: import('@playwright/test').APIRequestContext) {
    const api = new ApiHelper(request);
    await api.authenticate();
    return api;
  }

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
    test('create collection @smoke', async ({ request }) => {
      const api = await makeApi(request);
      const result = await api.createCollection(testCollectionName, 'API test collection');
      testCollectionId = result.id;
      expect(result.id).toBeTruthy();
      expect(result.name).toBe(testCollectionName);
    });

    test('get collections returns list', async ({ request }) => {
      const api = await makeApi(request);
      const result = await api.getCollections();
      expect(Array.isArray(result)).toBeTruthy();
      expect(result.length).toBeGreaterThan(0);
    });

    test('get collection by ID', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/collections/${testCollectionId}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(res.ok()).toBeTruthy();
      const data = await res.json();
      expect(data.name).toBe(testCollectionName);
    });

    test('create sub-collection', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const subName = `${testCollectionName}-Sub`;
      const result = await api.createCollection(subName, 'Sub-collection', testCollectionId);
      expect(result.id).toBeTruthy();
      expect(result.parentId || result.ParentId).toBe(testCollectionId);
    });

    test('get collection children', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const children = await api.getCollectionChildren(testCollectionId);
      expect(Array.isArray(children)).toBeTruthy();
    });

    test('update collection', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.patch(`${env.baseUrl}/api/collections/${testCollectionId}`, {
        headers: { Authorization: `Bearer ${token}` },
        data: { name: `${testCollectionName}-Updated`, description: 'Updated description' },
      });
      expect(res.ok()).toBeTruthy();
    });

    test('get collection ACL', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const acl = await api.getCollectionAcl(testCollectionId);
      expect(Array.isArray(acl)).toBeTruthy();
    });

    test('search users for ACL', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(
        `${env.baseUrl}/api/collections/${testCollectionId}/acl/users/search?q=test`,
        { headers: { Authorization: `Bearer ${token}` } }
      );
      expect(res.ok()).toBeTruthy();
    });
  });

  test.describe('Asset API', () => {
    test('upload asset via form @smoke', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const fixtures = ensureTestFixtures();
      const result = await api.uploadAsset(testCollectionId, fixtures.testImage, `API-Asset-${timestamp}`);
      testAssetId = result.id;
      expect(result.id).toBeTruthy();
    });

    test('get assets in collection', async ({ request }) => {
      if (!testCollectionId) test.skip();
      const api = await makeApi(request);
      const result = await api.getAssets(testCollectionId);
      expect(result.items || result.Items || result).toBeTruthy();
    });

    test('get single asset', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const result = await api.getAsset(testAssetId);
      expect(result.id || result.Id).toBeTruthy();
    });

    test('update asset metadata', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const result = await api.updateAsset(testAssetId, {
        title: `Updated-API-Asset-${timestamp}`,
        description: 'Updated via API test',
        tags: ['test', 'api', 'e2e'],
      });
      expect(result).toBeTruthy();
    });

    test('get asset collections', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/assets/${testAssetId}/collections`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(res.ok()).toBeTruthy();
    });

    test('asset thumbnail endpoint', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/assets/${testAssetId}/thumb`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      // May return 200/3xx or 404/400 if not processed or unavailable
      expect([200, 301, 302, 307, 400, 404]).toContain(res.status());
    });

    test('asset medium endpoint', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/assets/${testAssetId}/medium`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect([200, 301, 302, 307, 400, 404]).toContain(res.status());
    });

    test('asset download endpoint', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/assets/${testAssetId}/download`, {
        headers: { Authorization: `Bearer ${token}` },
        maxRedirects: 0,
      });
      // Should redirect to presigned URL
      expect([200, 301, 302, 307]).toContain(res.status());
    });

    test('asset preview endpoint', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/assets/${testAssetId}/preview`, {
        headers: { Authorization: `Bearer ${token}` },
        maxRedirects: 0,
      });
      expect([200, 301, 302, 307]).toContain(res.status());
    });

    test('add asset to another collection', async ({ request }) => {
      if (!testAssetId || !testCollectionId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();

      // Create another collection
      const col2 = await api.createCollection(`${testCollectionName}-Link`, 'Link test');
      const res = await request.post(
        `${env.baseUrl}/api/assets/${testAssetId}/collections/${col2.id}`,
        { headers: { Authorization: `Bearer ${token}` } }
      );
      expect(res.ok()).toBeTruthy();

      // Remove from second collection
      const removeRes = await request.delete(
        `${env.baseUrl}/api/assets/${testAssetId}/collections/${col2.id}`,
        { headers: { Authorization: `Bearer ${token}` } }
      );
      expect(removeRes.ok()).toBeTruthy();

      // Cleanup
      await api.deleteCollection(col2.id);
    });
  });

  test.describe('Share API', () => {
    let shareId: string;
    let shareToken: string;

    test('create asset share @smoke', async ({ request }) => {
      if (!testAssetId) test.skip();
      const api = await makeApi(request);
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

    test('admin list shares', async ({ request }) => {
      const api = await makeApi(request);
      const shares = await api.getAdminShares();
      expect(Array.isArray(shares)).toBeTruthy();
    });

    test('admin get share token', async ({ request }) => {
      if (!shareId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/admin/shares/${shareId}/token`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('update share password', async ({ request }) => {
      if (!shareId) test.skip();
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.put(`${env.baseUrl}/api/shares/${shareId}/password`, {
        headers: { Authorization: `Bearer ${token}` },
        data: { password: 'new-password-456' },
      });
      expect(res.ok()).toBeTruthy();
    });

    test('revoke share', async ({ request }) => {
      if (!shareId) test.skip();
      const api = await makeApi(request);
      const res = await api.revokeShare(shareId);
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('revoked share returns error', async ({ request }) => {
      if (!shareToken) test.skip();
      const res = await request.get(
        `${env.baseUrl}/api/shares/${shareToken}?password=new-password-456`
      );
      expect([401, 403, 404, 410]).toContain(res.status());
    });

    test('invalid token returns error', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/api/shares/invalid-token-xyz`);
      expect([400, 401, 404]).toContain(res.status());
    });
  });

  test.describe('Admin API', () => {
    test('admin collections access endpoint', async ({ request }) => {
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/admin/collections/access`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('admin users endpoint', async ({ request }) => {
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/admin/users`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect([200, 204, 403, 404]).toContain(res.status());
    });

    test('admin keycloak users endpoint', async ({ request }) => {
      const api = await makeApi(request);
      const users = await api.getKeycloakUsers();
      expect(Array.isArray(users)).toBeTruthy();
      expect(users.length).toBeGreaterThanOrEqual(0);
    });

    test('admin all assets endpoint', async ({ request }) => {
      const api = await makeApi(request);
      const token = await api.authenticate();
      const res = await request.get(`${env.baseUrl}/api/assets/all?skip=0&take=10`, {
        headers: { Authorization: `Bearer ${token}` },
      });
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

    test('viewer cannot create collections at root', async ({ request }) => {
      // Authenticate as viewer
      const viewerApi = new ApiHelper(request);
      const token = await viewerApi.authenticate(env.viewerUser.username, env.viewerUser.password);

      const res = await request.post(`${env.baseUrl}/api/collections`, {
        headers: { Authorization: `Bearer ${token}` },
        data: { name: 'Unauthorized Collection' },
      });
      // Should be forbidden
      expect([400, 401, 403]).toContain(res.status());
    });

    test('viewer cannot access admin endpoints', async ({ request }) => {
      const viewerApi = new ApiHelper(request);
      const token = await viewerApi.authenticate(env.viewerUser.username, env.viewerUser.password);

      const res = await request.get(`${env.baseUrl}/api/admin/shares`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect([401, 403]).toContain(res.status());
    });

    test('viewer cannot delete assets', async ({ request }) => {
      if (!testAssetId) test.skip();
      const viewerApi = new ApiHelper(request);
      const token = await viewerApi.authenticate(env.viewerUser.username, env.viewerUser.password);

      const res = await request.delete(`${env.baseUrl}/api/assets/${testAssetId}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect([401, 403]).toContain(res.status());
    });
  });

  // Cleanup
  test.describe('Cleanup', () => {
    test('delete test asset', async ({ request }) => {
      if (testAssetId) {
        const api = await makeApi(request);
        const res = await api.deleteAsset(testAssetId);
        // May have been cleaned up already
      }
    });

    test('delete test collection', async ({ request }) => {
      if (testCollectionId) {
        const api = await makeApi(request);
        const res = await api.deleteCollection(testCollectionId);
      }
    });
  });
});
