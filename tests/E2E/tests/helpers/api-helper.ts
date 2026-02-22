import { type APIRequestContext, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * API helper for direct backend calls in test setup/teardown.
 * Uses the admin JWT for authenticated requests.
 */
export class ApiHelper {
  private request: APIRequestContext;
  private token: string | null = null;

  constructor(request: APIRequestContext) {
    this.request = request;
  }

  /** Obtain a JWT from Keycloak for API calls */
  async authenticate(username?: string, password?: string): Promise<string> {
    const tokenUrl = `${env.keycloakUrl}/realms/${env.keycloakRealm}/protocol/openid-connect/token`;
    let lastError = 'Unknown authentication error';

    for (let attempt = 1; attempt <= 3; attempt++) {
      try {
        const response = await this.request.post(tokenUrl, {
          form: {
            grant_type: 'password',
            client_id: env.keycloakClientId,
            client_secret: env.keycloakClientSecret,
            username: username || env.adminUser.username,
            password: password || env.adminUser.password,
          },
        });

        if (!response.ok()) {
          const body = await response.text();
          lastError = `Keycloak token request failed (${response.status()}): ${body}`;
          continue;
        }

        const data = await response.json();
        if (!data.access_token) {
          lastError = `Keycloak returned no access_token: ${JSON.stringify(data)}`;
          continue;
        }

        this.token = data.access_token;
        return this.token;
      } catch (error) {
        lastError = error instanceof Error ? error.message : String(error);
      }
    }

    throw new Error(`Failed to authenticate against ${tokenUrl} after 3 attempts: ${lastError}`);
  }

  private authHeaders() {
    if (!this.token) throw new Error('Not authenticated — call authenticate() first');
    return { Authorization: `Bearer ${this.token}` };
  }

  // --- Collections ---

  async createCollection(name: string, description?: string, parentId?: string) {
    const res = await this.request.post(`${env.baseUrl}/api/collections`, {
      headers: this.authHeaders(),
      data: { name, description: description || '', parentId },
      maxRedirects: 0,
    });
    if (!res.ok()) {
      const body = await res.text();
      throw new Error(`createCollection failed (${res.status()} ${res.url()}): ${body.substring(0, 500)}`);
    }
    return await res.json();
  }

  async getCollections() {
    const res = await this.request.get(`${env.baseUrl}/api/collections`, {
      headers: this.authHeaders(),
    });
    expect(res.ok()).toBeTruthy();
    return await res.json();
  }

  async deleteCollection(id: string) {
    const res = await this.request.delete(`${env.baseUrl}/api/collections/${id}`, {
      headers: this.authHeaders(),
    });
    return res;
  }

  async getCollectionChildren(id: string) {
    const res = await this.request.get(`${env.baseUrl}/api/collections/${id}/children`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  // --- Assets ---

  async uploadAsset(collectionId: string, filePath: string, title: string) {
    const fs = await import('fs');
    const path = await import('path');
    const fileBuffer = fs.readFileSync(filePath);
    const fileName = path.basename(filePath);

    const res = await this.request.post(`${env.baseUrl}/api/assets`, {
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
    const res = await this.request.get(`${env.baseUrl}/api/assets/collection/${collectionId}?skip=0&take=100`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  async getAsset(id: string) {
    const res = await this.request.get(`${env.baseUrl}/api/assets/${id}`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  async deleteAsset(id: string) {
    const res = await this.request.delete(`${env.baseUrl}/api/assets/${id}`, {
      headers: this.authHeaders(),
    });
    return res;
  }

  async updateAsset(id: string, data: { title?: string; description?: string; tags?: string[] }) {
    const res = await this.request.patch(`${env.baseUrl}/api/assets/${id}`, {
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
    const res = await this.request.post(`${env.baseUrl}/api/shares`, {
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
    const res = await this.request.get(`${env.baseUrl}/api/admin/shares`, {
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
      return JSON.parse(text);
    } catch {
      console.warn(`getAdminShares returned invalid JSON: ${text.substring(0, 200)}`);
      return [];
    }
  }

  async revokeShare(id: string) {
    const res = await this.request.delete(`${env.baseUrl}/api/admin/shares/${id}`, {
      headers: this.authHeaders(),
    });
    return res;
  }

  // --- ACL ---

  async setCollectionAccess(collectionId: string, principalId: string, role: string) {
    const res = await this.request.post(`${env.baseUrl}/api/collections/${collectionId}/acl`, {
      headers: this.authHeaders(),
      data: { principalType: 'user', principalId, role },
    });
    return res;
  }

  async getCollectionAcl(collectionId: string) {
    const res = await this.request.get(`${env.baseUrl}/api/collections/${collectionId}/acl`, {
      headers: this.authHeaders(),
    });
    return await res.json();
  }

  // --- Admin Users ---

  async getKeycloakUsers() {
    const res = await this.request.get(`${env.baseUrl}/api/admin/keycloak-users`, {
      headers: this.authHeaders(),
    });
    if (!res.ok()) {
      console.warn(`getKeycloakUsers failed with status ${res.status()}`);
      return [];
    }
    const text = await res.text();
    if (!text || text.trim().length === 0) {
      console.warn('getKeycloakUsers returned empty body');
      return [];
    }
    try {
      return JSON.parse(text);
    } catch {
      console.warn(`getKeycloakUsers returned invalid JSON: ${text.substring(0, 200)}`);
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
