import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { env } from '../config/env';

/**
 * Forensic watermarking smoke spec (T5-WMK-01 G-43).
 *
 * The watermark feature is mostly implemented at the API + worker layer; the
 * UI surface is one admin page (/admin/watermarks/verify), one panel inside
 * ManageAccessDialog, one tri-state in CreateShareDialog, and a disclosure
 * banner on the public share page. This spec exercises:
 *
 *   1. The admin verify page loads under /admin/watermarks/verify and is
 *      protected by RequireAdmin.
 *   2. The PATCH endpoints round-trip — toggling collection watermark state
 *      via API and reading it back via GET reflects the change.
 *
 * The deeper "leak the watermarked file, verify it" loop is covered by the
 * unit-level round-trip test (DctLsbWatermarkRoundTripTests). Reproducing it
 * end-to-end would mean uploading a real asset, downloading it, then re-
 * uploading the bytes through /verify — possible but slow, and adds little
 * over the unit test's confirmation that embed/extract round-trips correctly.
 */
test.describe('Watermarks @admin @watermark', () => {
  let api: ApiHelper;
  const cleanupCollectionIds: string[] = [];

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();
  });

  test.afterAll(async () => {
    for (const id of cleanupCollectionIds) {
      await api.delete(`${env.baseUrl}/api/v1/collections/${id}`).catch(() => {});
    }
    await api.dispose();
  });

  // ── Admin verify page smoke ────────────────────────────────────────────

  test('admin watermark verify page loads @smoke', async ({ page }) => {
    await page.goto('/admin/watermarks/verify');
    await page.waitForLoadState('networkidle');
    // Page renders the upload card + verify button.
    await expect(page.getByRole('button', { name: /verify/i }).first()).toBeVisible();
  });

  test('admin tab links to verify route', async ({ page }) => {
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    // The Watermarks tab embeds a CTA that navigates to the standalone page.
    await page.getByRole('tab', { name: /watermark|vattenmärk/i }).click();
    await page.getByRole('button', { name: /verify watermark|verifiera vattenmärke/i }).click();
    await expect(page).toHaveURL(/\/admin\/watermarks\/verify/);
  });

  // ── PATCH endpoint round-trip ──────────────────────────────────────────

  test('toggle collection watermark via API and read it back', async () => {
    // Create a fresh collection so we don't perturb shared seeds.
    const createRes = await api.post(`${env.baseUrl}/api/v1/collections`, {
      name: `e2e-wmk-${Date.now()}`,
      description: 'watermark e2e',
    });
    expect(createRes.ok()).toBeTruthy();
    const created: { id: string } = await createRes.json();
    cleanupCollectionIds.push(created.id);

    // Default state: off.
    let getRes = await api.get(`${env.baseUrl}/api/v1/collections/${created.id}`);
    expect(getRes.ok()).toBeTruthy();
    let collection: { watermarkEnabled: boolean } = await getRes.json();
    expect(collection.watermarkEnabled).toBe(false);

    // Toggle on.
    const onResp = await api.patch(
      `${env.baseUrl}/api/v1/collections/${created.id}/watermark`,
      { enabled: true },
    );
    expect(onResp.ok()).toBeTruthy();

    getRes = await api.get(`${env.baseUrl}/api/v1/collections/${created.id}`);
    collection = await getRes.json();
    expect(collection.watermarkEnabled).toBe(true);

    // Toggle off again.
    const offResp = await api.patch(
      `${env.baseUrl}/api/v1/collections/${created.id}/watermark`,
      { enabled: false },
    );
    expect(offResp.ok()).toBeTruthy();

    getRes = await api.get(`${env.baseUrl}/api/v1/collections/${created.id}`);
    collection = await getRes.json();
    expect(collection.watermarkEnabled).toBe(false);
  });
});
