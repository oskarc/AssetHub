import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

test.describe('API Endpoint Tests @api', () => {
  let api: ApiHelper;
  let testCollectionId: string;
  let testAssetId: string;
  let shareId: string;
  let shareToken: string;

  const timestamp = Date.now();
  const testCollectionName = `${env.testData.collectionPrefix}-API-${timestamp}`;

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();

    // Create shared test fixtures upfront so no test depends on another test's side effects
    const collection = await api.createCollection(testCollectionName, 'API test collection');
    testCollectionId = collection.id;

    const fixtures = ensureTestFixtures();
    const asset = await api.uploadAsset(testCollectionId, fixtures.testImage, `API-Asset-${timestamp}`);
    testAssetId = asset.id;

    const share = await api.createShare(testAssetId, 'asset', 'test-password-123', 30);
    shareId = share.id;
    shareToken = share.token;
  });

  test.afterAll(async () => {
    if (shareId) {
      await api.revokeShare(shareId).catch(() => {});
    }
    if (testAssetId) {
      await api.deleteAsset(testAssetId).catch(() => {});
    }
    if (testCollectionId) {
      await api.deleteCollection(testCollectionId).catch(() => {});
    }
    await api.dispose();
  });

  // ─── Health Endpoints ───────────────────────────────────────────────

  test.describe('Health Endpoints', () => {
    test('health endpoint returns valid response @smoke', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/health`);
      expect(res.status()).toBe(200);
      const body = await res.json();
      expect(body.status).toMatch(/^(Healthy|Degraded|Unhealthy)$/);
      expect(body).toHaveProperty('duration');
      expect(body.checks).toEqual([]); // liveness probe has no checks
    });

    test('readiness endpoint returns health check details', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/health/ready`);
      expect([200, 503]).toContain(res.status());
      const body = await res.json();
      expect(body.status).toMatch(/^(Healthy|Degraded|Unhealthy)$/);
      expect(Array.isArray(body.checks)).toBe(true);
      for (const check of body.checks) {
        expect(check).toHaveProperty('name');
        expect(check.status).toMatch(/^(Healthy|Degraded|Unhealthy)$/);
        expect(check).toHaveProperty('duration');
      }
    });

    test('build info endpoint returns non-empty content', async ({ request }) => {
      const res = await request.get(`${env.baseUrl}/__build`);
      expect(res.ok()).toBeTruthy();
      const text = await res.text();
      expect(text.length).toBeGreaterThan(0);
    });
  });

  // ─── Collection API ─────────────────────────────────────────────────

  test.describe('Collection API', () => {
    test('get collections returns array including test collection', async () => {
      const result = await api.getCollections();
      expect(Array.isArray(result)).toBe(true);
      expect(result.length).toBeGreaterThan(0);

      const ours = result.find((c: { id: string }) => c.id === testCollectionId);
      expect(ours).toBeDefined();
      expect(ours.name).toBe(testCollectionName);
      expect(ours).toHaveProperty('userRole');
      expect(typeof ours.assetCount).toBe('number');
    });

    test('get collection by ID returns full DTO', async () => {
      const res = await api.get(`${env.baseUrl}/api/collections/${testCollectionId}`);
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(data.id).toBe(testCollectionId);
      expect(data.name).toBe(testCollectionName);
      expect(data.description).toBe('API test collection');
      expect(data).toHaveProperty('userRole');
      expect(data).toHaveProperty('createdAt');
      expect(typeof data.assetCount).toBe('number');
    });

    test('update collection persists changes', async () => {
      const updatedName = `${testCollectionName}-Updated`;
      const res = await api.patch(`${env.baseUrl}/api/collections/${testCollectionId}`, {
        name: updatedName,
        description: 'Updated description',
      });
      expect(res.ok()).toBeTruthy();

      // Verify the update persisted
      const getRes = await api.get(`${env.baseUrl}/api/collections/${testCollectionId}`);
      const data = await getRes.json();
      expect(data.name).toBe(updatedName);
      expect(data.description).toBe('Updated description');

      // Restore original name so other tests aren't affected
      await api.patch(`${env.baseUrl}/api/collections/${testCollectionId}`, {
        name: testCollectionName,
        description: 'API test collection',
      });
    });

    test('get collection ACL returns array of ACL entries', async () => {
      const acl = await api.getCollectionAcl(testCollectionId);
      expect(Array.isArray(acl)).toBe(true);
      if (acl.length > 0) {
        const entry = acl[0];
        expect(entry).toHaveProperty('id');
        expect(entry).toHaveProperty('principalType');
        expect(entry).toHaveProperty('principalId');
        expect(entry).toHaveProperty('role');
        expect(entry.role).toMatch(/^(viewer|contributor|manager|admin)$/);
      }
    });

    test('search users for ACL returns array', async () => {
      const res = await api.get(
        `${env.baseUrl}/api/collections/${testCollectionId}/acl/users/search?q=test`
      );
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(Array.isArray(data)).toBe(true);
    });
  });

  // ─── Asset API ──────────────────────────────────────────────────────

  test.describe('Asset API', () => {
    test('get assets in collection returns paginated response', async () => {
      const result = await api.getAssets(testCollectionId);
      expect(result).toHaveProperty('items');
      expect(result).toHaveProperty('total');
      expect(Array.isArray(result.items)).toBe(true);
      expect(typeof result.total).toBe('number');
      expect(result.total).toBeGreaterThan(0);
      expect(result.collectionId).toBe(testCollectionId);
    });

    test('get single asset returns full DTO with expected fields', async () => {
      const result = await api.getAsset(testAssetId);
      expect(result.id).toBe(testAssetId);
      expect(result.title).toContain('API-Asset');
      expect(result.assetType).toMatch(/^(image|video|document)$/);
      expect(result.status).toMatch(/^(uploading|processing|ready|failed)$/);
      expect(typeof result.sizeBytes).toBe('number');
      expect(result.contentType).toBeDefined();
      expect(Array.isArray(result.tags)).toBe(true);
      expect(result.createdAt).toBeDefined();
      expect(result.createdByUserId).toBeDefined();
    });

    test('update asset metadata persists changes', async () => {
      const newTitle = `Updated-API-Asset-${timestamp}`;
      const result = await api.updateAsset(testAssetId, {
        title: newTitle,
        description: 'Updated via API test',
        tags: ['test', 'api', 'e2e'],
      });

      expect(result.title).toBe(newTitle);
      expect(result.description).toBe('Updated via API test');
      expect(result.tags).toEqual(expect.arrayContaining(['test', 'api', 'e2e']));

      // Verify via separate GET
      const fetched = await api.getAsset(testAssetId);
      expect(fetched.title).toBe(newTitle);
      expect(fetched.tags).toEqual(expect.arrayContaining(['test', 'api', 'e2e']));
    });

    test('get asset collections returns lightweight collection DTOs', async () => {
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/collections`);
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(Array.isArray(data)).toBe(true);
      expect(data.length).toBeGreaterThan(0);
      const entry = data[0];
      expect(entry).toHaveProperty('id');
      expect(entry).toHaveProperty('name');
    });

    test('asset thumbnail returns image or not-yet-processed', async () => {
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/thumb`);
      // 200 = served directly, 3xx = redirect to object storage, 404 = not yet processed
      expect([200, 301, 302, 307, 404]).toContain(res.status());
      if (res.status() === 200) {
        const contentType = res.headers()['content-type'] || '';
        expect(contentType).toMatch(/^image\//);
      }
    });

    test('asset download redirects to presigned URL', async () => {
      const res = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/download`, { maxRedirects: 0 });
      expect([200, 301, 302, 307]).toContain(res.status());
      if ([301, 302, 307].includes(res.status())) {
        expect(res.headers()['location']).toBeTruthy();
      }
    });

    test('add and remove asset from another collection', async () => {
      const col2 = await api.createCollection(`${testCollectionName}-Link`, 'Link test');
      try {
        const addRes = await api.post(`${env.baseUrl}/api/assets/${testAssetId}/collections/${col2.id}`);
        expect(addRes.ok()).toBeTruthy();

        // Verify asset appears in the second collection
        const collectionsRes = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/collections`);
        const collections = await collectionsRes.json();
        const ids = collections.map((c: { id: string }) => c.id);
        expect(ids).toContain(col2.id);

        // Remove and verify
        const removeRes = await api.delete(`${env.baseUrl}/api/assets/${testAssetId}/collections/${col2.id}`);
        expect(removeRes.ok()).toBeTruthy();

        const afterRes = await api.get(`${env.baseUrl}/api/assets/${testAssetId}/collections`);
        const afterIds = (await afterRes.json()).map((c: { id: string }) => c.id);
        expect(afterIds).not.toContain(col2.id);
      } finally {
        await api.deleteCollection(col2.id);
      }
    });
  });

  // ─── Share API ──────────────────────────────────────────────────────
  // Share public endpoints are AllowAnonymous. We use a fresh browser context
  // without stored cookies so the auth middleware doesn't interfere with 401
  // responses (cookie auth can intercept 401 and redirect to login).

  test.describe('Share API', () => {
    test('access share with correct password returns shared asset DTO', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/shares/${shareToken}`, {
          headers: { 'X-Share-Password': 'test-password-123' },
        });
        expect(res.status()).toBe(200);
        const data = await res.json();
        expect(data.type).toBe('asset');
        expect(data.id).toBeDefined();
        expect(data.title).toBeDefined();
        expect(data.assetType).toMatch(/^(image|video|document)$/);
        expect(data.contentType).toBeDefined();
        expect(typeof data.sizeBytes).toBe('number');
        expect(data.permissions).toBeDefined();
      } finally {
        await ctx.close();
      }
    });

    test('access share without password returns 401 with requiresPassword', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/shares/${shareToken}`);
        expect(res.status()).toBe(401);
        const data = await res.json();
        expect(data.requiresPassword).toBe(true);
      } finally {
        await ctx.close();
      }
    });

    test('access share with wrong password returns 401', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/shares/${shareToken}`, {
          headers: { 'X-Share-Password': 'wrong-password' },
        });
        expect(res.status()).toBe(401);
      } finally {
        await ctx.close();
      }
    });

    test('share download redirects to presigned URL', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/shares/${shareToken}/download`, {
          headers: { 'X-Share-Password': 'test-password-123' },
          maxRedirects: 0,
        });
        expect([200, 302, 307]).toContain(res.status());
        if ([302, 307].includes(res.status())) {
          expect(res.headers()['location']).toBeTruthy();
        }
      } finally {
        await ctx.close();
      }
    });

    test('admin list shares returns admin share DTOs', async () => {
      const shares = await api.getAdminShares();
      expect(Array.isArray(shares)).toBe(true);
      expect(shares.length).toBeGreaterThan(0);
      const share = shares.find((s: { id: string }) => s.id === shareId);
      expect(share).toBeDefined();
      expect(share.scopeType).toMatch(/^(asset|collection)$/);
      expect(share).toHaveProperty('scopeId');
      expect(share).toHaveProperty('createdAt');
      expect(share).toHaveProperty('status');
      expect(typeof share.accessCount).toBe('number');
      expect(typeof share.hasPassword).toBe('boolean');
    });

    test('admin get share token returns token string', async () => {
      const res = await api.get(`${env.baseUrl}/api/admin/shares/${shareId}/token`);
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(typeof data.token).toBe('string');
      expect(data.token.length).toBeGreaterThan(0);
    });

    test('update share password succeeds', async () => {
      const res = await api.put(`${env.baseUrl}/api/shares/${shareId}/password`, { password: 'new-password-456' });
      expect(res.ok()).toBeTruthy();
    });

    test('revoke share returns 204', async () => {
      const res = await api.revokeShare(shareId);
      expect(res.status()).toBe(204);
      // Clear shareId so afterAll doesn't try to revoke again
      shareId = '';
    });

    test('revoked share returns 404 or 410', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/shares/${shareToken}`, {
          headers: { 'X-Share-Password': 'new-password-456' },
        });
        expect([404, 410]).toContain(res.status());
      } finally {
        await ctx.close();
      }
    });

    test('invalid share token returns 404', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/shares/invalid-token-xyz`);
        expect(res.status()).toBe(404);
      } finally {
        await ctx.close();
      }
    });
  });

  // ─── Admin API ──────────────────────────────────────────────────────

  test.describe('Admin API', () => {
    test('admin collections access returns collection tree with ACLs', async () => {
      const res = await api.get(`${env.baseUrl}/api/admin/collections/access`);
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(Array.isArray(data)).toBe(true);
      expect(data.length).toBeGreaterThan(0);
      const entry = data[0];
      expect(entry).toHaveProperty('id');
      expect(entry).toHaveProperty('name');
      expect(Array.isArray(entry.acls)).toBe(true);
    });

    test('admin users returns user access summaries', async () => {
      const res = await api.get(`${env.baseUrl}/api/admin/users`);
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(Array.isArray(data)).toBe(true);
      if (data.length > 0) {
        const user = data[0];
        expect(user).toHaveProperty('userId');
        expect(user).toHaveProperty('userName');
        expect(typeof user.collectionCount).toBe('number');
      }
    });

    test('admin keycloak users returns user list with roles', async () => {
      const users = await api.getKeycloakUsers();
      expect(Array.isArray(users)).toBe(true);
      expect(users.length).toBeGreaterThan(0);
      const user = users[0];
      expect(user).toHaveProperty('id');
      expect(user).toHaveProperty('username');
      expect(typeof user.isSystemAdmin).toBe('boolean');
      expect(typeof user.collectionCount).toBe('number');
    });

    test('admin all assets returns paginated response', async () => {
      const res = await api.get(`${env.baseUrl}/api/assets/all?skip=0&take=10`);
      expect(res.status()).toBe(200);
      const data = await res.json();
      expect(data).toHaveProperty('items');
      expect(data).toHaveProperty('total');
      expect(Array.isArray(data.items)).toBe(true);
      expect(typeof data.total).toBe('number');
      if (data.items.length > 0) {
        const asset = data.items[0];
        expect(asset).toHaveProperty('id');
        expect(asset).toHaveProperty('title');
        expect(asset).toHaveProperty('assetType');
        expect(asset).toHaveProperty('status');
      }
    });
  });

  // ─── Authorization Guards ───────────────────────────────────────────
  // Cookie auth redirects forbidden requests to /Account/AccessDenied (302)
  // rather than returning 403. We use maxRedirects: 0 to catch the actual
  // server response before Playwright follows the redirect.

  test.describe('Authorization Guards', () => {
    test('unauthenticated request redirects to Keycloak', async ({ browser }) => {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
      try {
        const res = await ctx.request.get(`${env.baseUrl}/api/collections`, { maxRedirects: 0 });
        expect([301, 302, 303, 307, 308]).toContain(res.status());
        const location = res.headers()['location'];
        expect(location).toBeTruthy();
        expect(location?.toLowerCase()).toContain('keycloak');
      } finally {
        await ctx.close();
      }
    });

    test('viewer cannot create collections at root', async () => {
      const viewerApi = await ApiHelper.withCookieAuth('viewer');
      try {
        await expect(async () => {
          await viewerApi.createCollection('Unauthorized Collection');
        }).rejects.toThrow(/400|401|403|failed/i);
      } finally {
        await viewerApi.dispose();
      }
    });

    test('viewer cannot access admin shares endpoint', async () => {
      const viewerApi = await ApiHelper.withCookieAuth('viewer');
      try {
        const res = await viewerApi.get(`${env.baseUrl}/api/admin/shares`, { maxRedirects: 0 });
        // Cookie auth returns 302 redirect to AccessDenied for forbidden, or 401/403 directly
        expect([302, 401, 403]).toContain(res.status());
      } finally {
        await viewerApi.dispose();
      }
    });

    test('viewer cannot access admin users endpoint', async () => {
      const viewerApi = await ApiHelper.withCookieAuth('viewer');
      try {
        const res = await viewerApi.get(`${env.baseUrl}/api/admin/users`, { maxRedirects: 0 });
        expect([302, 401, 403]).toContain(res.status());
      } finally {
        await viewerApi.dispose();
      }
    });

    test('viewer cannot delete assets', async () => {
      const viewerApi = await ApiHelper.withCookieAuth('viewer');
      try {
        const res = await viewerApi.delete(`${env.baseUrl}/api/assets/${testAssetId}`, { maxRedirects: 0 });
        expect([302, 401, 403]).toContain(res.status());
      } finally {
        await viewerApi.dispose();
      }
    });
  });
});
