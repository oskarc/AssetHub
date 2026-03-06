import { type Page, type Locator, expect } from '@playwright/test';

/**
 * Helper functions for Blazor Server interactivity.
 *
 * Blazor Server uses SignalR for interactivity. After the initial SSR render,
 * the page needs time to establish the SignalR connection and wire up event
 * handlers before UI interactions work. Checking for Blazor JS objects alone
 * is NOT sufficient — the circuit may be initializing but handlers not yet attached.
 */

/**
 * Wait for Blazor Server to become interactive.
 *
 * Waits for the Blazor script to initialize, then adds a buffer for handler
 * wiring. This is a best-effort wait — callers doing button clicks that open
 * dialogs/popovers should prefer the retry helpers (DialogHelper.clickAndWaitForDialog,
 * clickAndWaitForPopover, etc.) which are resilient to timing issues.
 */
export async function waitForBlazorInteractive(page: Page, timeout = 15_000): Promise<void> {
  if (page.isClosed()) return;

  try {
    await page.waitForFunction(
      () => {
        const w = window as any;
        if (w.Blazor?._internal?.navigationManager) return true;
        if (w.Blazor?._internal?.circuitId) return true;
        if (w.Blazor?._internal?.attachWebRendererInterop) return true;
        if (w.Blazor?.circuit) return true;
        if (w.Blazor && typeof w.Blazor.reconnect === 'function') return true;
        return false;
      },
      { timeout }
    );
  } catch (e) {
    if (page.isClosed()) return;
    console.warn('waitForBlazorInteractive: Blazor circuit not detected within timeout — proceeding anyway.', e);
  }

  // Buffer for event handler wiring after circuit is established
  if (!page.isClosed()) {
    await page.waitForTimeout(1000);
  }
}

/**
 * Click a locator and wait for a popover to appear (e.g. MudSelect dropdown).
 * Retries if Blazor isn't interactive yet.
 */
export async function clickAndWaitForPopover(
  page: Page,
  trigger: Locator,
  timeout = 15_000
): Promise<Locator> {
  const popover = page.locator('.mud-popover-open');

  await expect(async () => {
    await trigger.click();
    await expect(popover).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout });

  return popover;
}

/**
 * Click a locator and retry until an expected side-effect occurs.
 * Generic version for any Blazor interaction that may not fire on first click.
 */
export async function clickUntil(
  button: Locator,
  assertion: () => Promise<void>,
  timeout = 15_000
): Promise<void> {
  await expect(async () => {
    await button.click();
    await assertion();
  }).toPass({ timeout });
}
