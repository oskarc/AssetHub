import { type APIRequestContext, expect, request as playwrightRequest } from '@playwright/test';
import { env } from '../config/env';
import * as path from 'node:path';
import * as fs from 'node:fs';

const AUTH_DIR = path.join(__dirname, '..', '.auth');

/**
 * API helper for direct backend calls in test setup/teardown.
 * Cookie-based auth from the saved browser session.
 */
export class ApiHelper {
  private readonly request: APIRequestContext;
  private readonly useCookieAuth: boolean = false;

  constructor(request: APIRequestContext, useCookieAuth: boolean = false) {
    this.request = request;
    this.useCookieAuth = useCookieAuth;
  }

  /**
   * Create an ApiHelper that uses cookies from the saved browser session.
   * Cookie auth from the saved browser session is the only API auth path.
   * @param user - 'admin' (default) or 'viewer'
   */
  static async withCookieAuth(user: 'admin' | 'viewer' = 'admin'): Promise<ApiHelper> {
    const stateFile = user === 'viewer' ? 'viewer.json' : 'admin.json';
    const storageStatePath = path.join(AUTH_DIR, stateFile);
    
    if (!fs.existsSync(storageStatePath)) {
      throw new Error(`Storage state not found at ${storageStatePath}. Run global setup first.`);
    }

    const context = await playwrightRequest.newContext({
      baseURL: env.baseUrl,
      storageState: storageStatePath,
      ignoreHTTPSErrors: true,
    });

    return new ApiHelper(context, true);
  }


  private authHeaders(): Record<string, string> {
    // Cookie auth: cookies are sent automatically, no header needed
    if (this.useCookieAuth) {
      return {};
    }
    throw new Error('ApiHelper requires cookie auth — construct it with ApiHelper.withCookieAuth()');
  }

  /** Clean up the API context if created via withCookieAuth() */
  async dispose(): Promise<void> {
    if (this.useCookieAuth) {
      await this.request.dispose();
    }
  }

  // --- Raw HTTP methods for direct API access ---

  /** Make a raw GET request with authentication */
  async get(url: string, options?: { maxRedirects?: number }) {
    return await this.request.get(url, {
      headers: this.authHeaders(),
      ...options,
    });
  }

  /** Make a raw POST request with authentication */
  async post(url: string, data?: unknown, options?: { maxRedirects?: number }) {
    return await this.request.post(url, {
      headers: this.authHeaders(),
      data,
      ...options,
    });
  }

  /** Make a raw PATCH request with authentication */
  async patch(url: string, data?: unknown) {
    return await this.request.patch(url, {
      headers: this.authHeaders(),
      data,
    });
  }

  /** Make a raw PUT request with authentication */
  async put(url: string, data?: unknown) {
    return await this.request.put(url, {
      headers: this.authHeaders(),
      data,
    });
  }

  /** Make a raw DELETE request with authentication */
  async delete(url: string, options?: { maxRedirects?: number }) {
    return await this.request.delete(url, {
      headers: this.authHeaders(),
      ...options,
    });
  }

  // --- Collections ---

  async createCollection(name: string, description?: string) {
    const res = await this.request.post(`${env.baseUrl}/api/v1/collections`, {
      headers: this.authHeaders(),
      data: { name, description: description || '' },
      maxRedirects: 0,
    });
    if (!res.ok()) {
      const body = await res.text();
      throw new Error(`createCollection failed (${res.status()} ${res.url()}): ${body.substring(0, 500)}`);
    }
    return await res.json();
  }

  async getCollections() {
    const res = await this.request.get(`${env.baseUrl}/api/v1/collections`, {
      headers: this.authHeaders(),
    });
    expect(res.ok()).toBeTruthy();
    return await res.json();
  }

  async deleteCollection(id: string) {
    const res = await this.request.delete(`${env.baseUrl}/api/v1/collections/${id}`, {
      headers: this.authHeaders(),
    });
    return res;
  }

  // --- Assets ---

  async uploadAsset(collectionId: string, filePath: string, title: string) {
    const fs = await import('node:fs');
    const path = await import('node:path');
    const fileBuffer = fs.readFileSync(filePath);
    const fileName = path.basename(filePath);

    const res = await this.request.post(`${env.baseUrl}/api/v1/assets`, {
      headers: this.authHeaders(),
      multipart: {
        file: { name: fileName, mimeType: 'image/png', buffer: fileBuffer },
        collectionId,
        title,
      },
    });
    expect(res.ok()).toBeTruthy();
    return await res.json();
  }

  async getAssets(collectionId: string) {
    const res = await this.request.get(`${env.baseUrl}/api/v1/assets/collection/${collectionId}?skip=0&take=100`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  async getAsset(id: string) {
    const res = await this.request.get(`${env.baseUrl}/api/v1/assets/${id}`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  async deleteAsset(id: string) {
    const res = await this.request.delete(`${env.baseUrl}/api/v1/assets/${id}`, {
      headers: this.authHeaders(),
    });
    return res;
  }

  async updateAsset(id: string, data: { title?: string; description?: string; tags?: string[] }) {
    const res = await this.request.patch(`${env.baseUrl}/api/v1/assets/${id}`, {
      headers: this.authHeaders(),
      data,
    });
    return await res.json();
  }

  // --- Shares ---

  async createShare(scopeId: string, scopeType: 'asset' | 'collection', password?: string, expiresInDays?: number) {
    const expiresAt = expiresInDays
      ? new Date(Date.now() + expiresInDays * 86400000).toISOString()
      : undefined;
    const res = await this.request.post(`${env.baseUrl}/api/v1/shares`, {
      headers: this.authHeaders(),
      data: {
        scopeId,
        scopeType,
        password: password || env.testData.sharePasswordDefault,
        expiresAt,
      },
    });
    expect(res.ok()).toBeTruthy();
    return await res.json();
  }

  async getAdminShares() {
    const res = await this.request.get(`${env.baseUrl}/api/v1/admin/shares`, {
      headers: this.authHeaders(),
    });
    if (!res.ok()) {
      console.warn(`getAdminShares failed with status ${res.status()}`);
      return [];
    }
    const text = await res.text();
    if (!text || text.trim().length === 0) {
      return [];
    }
    try {
      const data = JSON.parse(text);
      // API returns paginated response with items array
      if (Array.isArray(data)) {
        return data;
      }
      // Try both camelCase and PascalCase property names
      if (Array.isArray(data.items)) {
        return data.items;
      }
      if (Array.isArray(data.Items)) {
        return data.Items;
      }
      return [];
    } catch {
      console.warn(`getAdminShares returned invalid JSON: ${text.substring(0, 200)}`);
      return [];
    }
  }

  async revokeShare(id: string) {
    const res = await this.request.delete(`${env.baseUrl}/api/v1/admin/shares/${id}`, {
      headers: this.authHeaders(),
    });
    return res;
  }

  // --- ACL ---

  async setCollectionAccess(collectionId: string, principalId: string, role: string) {
    const res = await this.request.post(`${env.baseUrl}/api/v1/collections/${collectionId}/acl`, {
      headers: this.authHeaders(),
      data: { principalType: 'user', principalId, role },
    });
    return res;
  }

  async getCollectionAcl(collectionId: string) {
    const res = await this.request.get(`${env.baseUrl}/api/v1/collections/${collectionId}/acl`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  // --- Admin Users ---

  async getDirectoryUsers() {
    const res = await this.request.get(`${env.baseUrl}/api/v1/admin/directory-users`, {
      headers: this.authHeaders(),
    });
    if (!res.ok()) {
      console.warn(`getDirectoryUsers failed with status ${res.status()}`);
      return [];
    }
    const text = await res.text();
    if (!text || text.trim().length === 0) {
      console.warn('getDirectoryUsers returned empty body');
      return [];
    }
    try {
      return JSON.parse(text);
    } catch {
      console.warn(`getDirectoryUsers returned invalid JSON: ${text.substring(0, 200)}`);
      return [];
    }
  }

  // --- Health ---

  async checkHealth() {
    const res = await this.request.get(`${env.baseUrl}/health`);
    return res.status();
  }

  async checkReady() {
    const res = await this.request.get(`${env.baseUrl}/health/ready`);
    return res.status();
  }
}
