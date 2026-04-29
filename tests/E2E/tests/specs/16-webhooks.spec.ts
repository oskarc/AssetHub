import { test, expect } from '@playwright/test';
import * as crypto from 'node:crypto';
import { ApiHelper } from '../helpers/api-helper';
import { ensureTestFixtures } from '../helpers/test-fixtures';
import { env } from '../config/env';

/**
 * Webhook admin flow + signed delivery verification.
 *
 * Most assertions hit the AssetHub admin API directly because the in-app
 * UI for webhooks is a single Admin tab whose value is conveyed primarily
 * through API state (deliveries list, signature on outbound POSTs). The
 * spec also exercises one UI smoke (Admin → Webhooks tab loads) so a
 * regression in the tab routing surfaces here.
 *
 * The "real receiver" path uses webhook.site — a public, no-auth webhook
 * inspector. We mint a unique token per test, point the AssetHub webhook
 * at it, trigger an event (post a comment), then read webhook.site's
 * inspector API to confirm the request landed and verify the HMAC.
 *
 * Set WEBHOOK_E2E_RECEIVER=skip in env to skip the receiver-dependent
 * tests when running without internet (everything else still runs).
 */

const RECEIVER_DISABLED = process.env.WEBHOOK_E2E_RECEIVER === 'skip';
const WEBHOOK_SITE = 'https://webhook.site';

type WebhookCreatedResponse = {
  webhook: { id: string; name: string; url: string; eventTypes: string[] };
  plaintextSecret: string;
};

type DeliveryDto = {
  id: string;
  eventType: string;
  status: 'Pending' | 'Delivered' | 'Failed';
  attemptCount: number;
  responseStatus?: number;
};

test.describe('Webhooks @admin @webhook', () => {
  let api: ApiHelper;
  const created: string[] = []; // webhook ids to clean up

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();
  });

  test.afterAll(async () => {
    for (const id of created) {
      await api.delete(`${env.baseUrl}/api/v1/admin/webhooks/${id}`).catch(() => {});
    }
    await api.dispose();
  });

  // ── Admin tab smoke ─────────────────────────────────────────────────────

  test('admin Webhooks tab loads @smoke', async ({ page }) => {
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    // Tab text is "Webhooks" in EN, "Webhooks" in SV (loanword) — match either way.
    await page.getByRole('tab', { name: /webhook/i }).click();
    // Empty state OR list — both valid; just confirm the panel rendered.
    await expect(page.locator('.mud-tabs')).toBeVisible();
  });

  // ── CRUD via API ────────────────────────────────────────────────────────

  test('create → list → rotate secret → delete', async () => {
    const url = `https://example.com/${randomToken()}`;
    const created1 = await createWebhook(api, {
      name: `e2e-crud-${Date.now()}`,
      url,
      eventTypes: ['comment.created'],
    });
    expect(created1.webhook.url).toBe(url);
    expect(created1.plaintextSecret).toMatch(/^wh_/); // documented prefix
    created.push(created1.webhook.id);

    // List should include it.
    const listRes = await api.get(`${env.baseUrl}/api/v1/admin/webhooks`);
    expect(listRes.ok()).toBeTruthy();
    const list = await listRes.json();
    expect(list.find((w: { id: string }) => w.id === created1.webhook.id)).toBeTruthy();

    // Rotate secret returns a new plaintext.
    const rotateRes = await api.post(
      `${env.baseUrl}/api/v1/admin/webhooks/${created1.webhook.id}/rotate-secret`,
    );
    expect(rotateRes.ok()).toBeTruthy();
    const rotated = await rotateRes.json();
    expect(rotated.plaintextSecret).toMatch(/^wh_/);
    expect(rotated.plaintextSecret).not.toBe(created1.plaintextSecret);

    // Delete.
    const delRes = await api.delete(`${env.baseUrl}/api/v1/admin/webhooks/${created1.webhook.id}`);
    expect([200, 204]).toContain(delRes.status());
    created.splice(created.indexOf(created1.webhook.id), 1);
  });

  test('rejects non-public URLs (SSRF guard)', async () => {
    // OutboundUrlGuard rejects loopback/RFC 1918/link-local. The validation
    // happens at create time so this should come back 400 / VALIDATION_ERROR.
    const res = await api.post(`${env.baseUrl}/api/v1/admin/webhooks`, {
      name: `e2e-ssrf-${Date.now()}`,
      url: 'http://127.0.0.1:9999/hook',
      eventTypes: ['comment.created'],
    });
    expect(res.status()).toBe(400);
  });

  // ── Send test creates a delivery row ────────────────────────────────────

  test('Send test enqueues a delivery row', async () => {
    const c = await createWebhook(api, {
      name: `e2e-test-${Date.now()}`,
      url: `https://example.com/${randomToken()}`,
      eventTypes: ['comment.created'],
    });
    created.push(c.webhook.id);

    // Trigger the test.
    const testRes = await api.post(
      `${env.baseUrl}/api/v1/admin/webhooks/${c.webhook.id}/test`,
    );
    expect(testRes.ok()).toBeTruthy();

    // Poll deliveries endpoint until our test delivery appears.
    const delivery = await waitFor(async () => {
      const r = await api.get(
        `${env.baseUrl}/api/v1/admin/webhooks/${c.webhook.id}/deliveries`,
      );
      if (!r.ok()) return null;
      const rows: DeliveryDto[] = await r.json();
      return rows.find((d) => d.eventType === 'webhook.test') ?? null;
    });
    expect(delivery).not.toBeNull();
    // Delivery may be Pending (worker hasn't picked up yet), Delivered (rare —
    // example.com often 405s POST), or Failed (4xx). Anything but absent is fine.
    expect(['Pending', 'Delivered', 'Failed']).toContain(delivery!.status);
  });

  // ── Real receiver: comment.created → HMAC verifies ──────────────────────

  test('comment.created reaches webhook.site with valid HMAC signature', async () => {
    test.skip(RECEIVER_DISABLED, 'WEBHOOK_E2E_RECEIVER=skip — receiver tests disabled');
    test.setTimeout(120_000); // network round-trips + worker dispatch

    // Mint a webhook.site token (anonymous, free, no auth).
    const tokenRes = await fetch(`${WEBHOOK_SITE}/token`, { method: 'POST' });
    if (!tokenRes.ok) {
      test.skip(true, `webhook.site unreachable (${tokenRes.status}) — skipping real-receiver test`);
    }
    const { uuid: token } = (await tokenRes.json()) as { uuid: string };
    const receiverUrl = `${WEBHOOK_SITE}/${token}`;

    // Create the webhook subscribed to comment.created.
    const c = await createWebhook(api, {
      name: `e2e-receiver-${Date.now()}`,
      url: receiverUrl,
      eventTypes: ['comment.created'],
    });
    created.push(c.webhook.id);

    // Need an asset to comment on. Create a one-off collection + asset.
    const collection = await api.createCollection(`E2E-Webhook-${Date.now()}`, 'webhook test');
    try {
      const fix = ensureTestFixtures();
      const asset = await api.uploadAsset(collection.id, fix.testImage, 'webhook-test-asset');

      // Post a comment — fires comment.created.
      const commentRes = await api.post(
        `${env.baseUrl}/api/v1/assets/${asset.id}/comments`,
        { body: 'Hello from the e2e webhook spec' },
      );
      expect(commentRes.ok()).toBeTruthy();

      // Poll webhook.site's inspector API for the request.
      const received = await waitFor(
        async () => {
          const r = await fetch(`${WEBHOOK_SITE}/token/${token}/requests?sorting=newest`);
          if (!r.ok) return null;
          const j = (await r.json()) as { data: Array<{ headers: Record<string, string[]>; content: string }> };
          return j.data[0] ?? null;
        },
        { timeoutMs: 60_000, intervalMs: 1500 },
      );
      expect(received, 'webhook.site never received the delivery').not.toBeNull();

      // Headers (webhook.site lowercases them).
      const sigHeader = (received!.headers['x-assethub-signature'] ?? [])[0];
      const eventHeader = (received!.headers['x-assethub-event'] ?? [])[0];
      expect(eventHeader).toBe('comment.created');
      expect(sigHeader).toMatch(/^sha256=[a-f0-9]{64}$/);

      // Verify the HMAC ourselves using the secret we got at create time.
      const expected =
        'sha256=' +
        crypto
          .createHmac('sha256', c.plaintextSecret)
          .update(received!.content) // raw body bytes as webhook.site stored them
          .digest('hex');
      expect(sigHeader).toBe(expected);
    } finally {
      await api.deleteCollection(collection.id).catch(() => {});
    }
  });
});

// ── Helpers ────────────────────────────────────────────────────────────────

async function createWebhook(
  api: ApiHelper,
  payload: { name: string; url: string; eventTypes: string[] },
): Promise<WebhookCreatedResponse> {
  const res = await api.post(`${env.baseUrl}/api/v1/admin/webhooks`, payload);
  if (!res.ok()) {
    const body = await res.text();
    throw new Error(`createWebhook failed (${res.status()}): ${body.substring(0, 500)}`);
  }
  return (await res.json()) as WebhookCreatedResponse;
}

function randomToken(): string {
  return crypto.randomBytes(8).toString('hex');
}

async function waitFor<T>(
  fn: () => Promise<T | null>,
  opts: { timeoutMs?: number; intervalMs?: number } = {},
): Promise<T | null> {
  const timeoutMs = opts.timeoutMs ?? 15_000;
  const intervalMs = opts.intervalMs ?? 500;
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const value = await fn();
    if (value !== null) return value;
    await new Promise((r) => setTimeout(r, intervalMs));
  }
  return null;
}
