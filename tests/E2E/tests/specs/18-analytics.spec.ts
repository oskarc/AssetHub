import { test, expect } from '@playwright/test';
import { ApiHelper } from '../helpers/api-helper';
import { env } from '../config/env';

/**
 * Analytics dashboard smoke spec (T5-ANL-01).
 *
 * The deeper rollup correctness loop is covered by xUnit integration tests
 * (`AnalyticsRollupServiceTests`). This spec verifies the user-visible
 * surface: the page loads under RequireAdmin, the three panels render,
 * the CSV exports return text/csv, and the PDF export queue endpoint
 * returns 202 Accepted with a job id.
 *
 * The full PDF round-trip (queue → poll → ready → download) requires the
 * Worker to be running and a successful QuestPDF render — covered by the
 * integration test in `BuildAnalyticsPdfHandler`'s test suite once the
 * background service is enabled in CI.
 */
test.describe('Analytics @admin @analytics', () => {
  let api: ApiHelper;

  test.beforeAll(async () => {
    api = await ApiHelper.withCookieAuth();
  });

  test.afterAll(async () => {
    await api.dispose();
  });

  // ── Admin page smoke ────────────────────────────────────────────────────

  test('admin analytics page loads @smoke', async ({ page }) => {
    await page.goto('/admin/analytics');
    await page.waitForLoadState('networkidle');
    // Three panels render (downloads / storage / exposure). Match by the
    // Btn_ExportCsv test-localizer key — every panel has its own export button.
    const exportButtons = page.getByRole('button', { name: /export csv|exportera csv/i });
    await expect(exportButtons.first()).toBeVisible();
    expect(await exportButtons.count()).toBeGreaterThanOrEqual(3);
  });

  test('admin tab links to analytics route', async ({ page }) => {
    await page.goto('/admin');
    await page.waitForLoadState('networkidle');
    await page.getByRole('tab', { name: /analytic|analys/i }).click();
    await page.getByRole('button', { name: /analytic|analys/i }).first().click();
    await expect(page).toHaveURL(/\/admin\/analytics/);
  });

  // ── API contract checks ────────────────────────────────────────────────

  test('downloads endpoint returns array', async () => {
    const res = await api.get(`${env.baseUrl}/api/v1/admin/analytics/downloads?window=90&take=20`);
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(Array.isArray(body)).toBe(true);
  });

  test('storage endpoints return arrays', async () => {
    const byColl = await api.get(`${env.baseUrl}/api/v1/admin/analytics/storage/by-collection?take=20`);
    expect(byColl.ok()).toBeTruthy();
    expect(Array.isArray(await byColl.json())).toBe(true);

    const byType = await api.get(`${env.baseUrl}/api/v1/admin/analytics/storage/by-asset-type`);
    expect(byType.ok()).toBeTruthy();
    expect(Array.isArray(await byType.json())).toBe(true);
  });

  test('exposure endpoint returns array', async () => {
    const res = await api.get(`${env.baseUrl}/api/v1/admin/analytics/exposure?window=90&take=20`);
    expect(res.ok()).toBeTruthy();
    expect(Array.isArray(await res.json())).toBe(true);
  });

  test('downloads CSV export returns text/csv', async () => {
    const res = await api.get(`${env.baseUrl}/api/v1/admin/analytics/downloads/export.csv?window=90&take=20`);
    expect(res.ok()).toBeTruthy();
    expect(res.headers()['content-type']).toContain('text/csv');
    const body = await res.text();
    // Header row from the endpoint's BuildCsv helper.
    expect(body).toContain('asset_id,title,asset_type,download_count,is_deleted');
  });

  test('PDF export enqueues a job and returns 202 + job id', async () => {
    const res = await api.post(`${env.baseUrl}/api/v1/admin/analytics/export-pdf?window=90`);
    expect(res.status()).toBe(202);
    const body = await res.json();
    expect(body.jobId).toBeDefined();
    expect(['pending', 'building', 'ready', 'failed']).toContain(body.status);

    // Poll once — even if the worker isn't running, the GET should return
    // the job's current status, not 404.
    const status = await api.get(`${env.baseUrl}/api/v1/admin/analytics/export-pdf/${body.jobId}`);
    expect(status.ok()).toBeTruthy();
  });
});
