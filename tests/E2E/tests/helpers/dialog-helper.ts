import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Shared dialog helpers for MudBlazor dialog interactions.
 */
export class DialogHelper {
  readonly page: Page;

  constructor(page: Page) {
    this.page = page;
  }

  /** Get the currently visible dialog */
  get dialog(): Locator {
    return this.page.locator('.mud-dialog').last();
  }

  /** Wait for a dialog to appear */
  async waitForDialog(timeout = 10_000) {
    await expect(this.dialog).toBeVisible({ timeout });
  }

  /**
   * Click a button and wait for a dialog to appear, retrying if Blazor
   * interactivity isn't ready yet. Use this instead of `btn.click()` + `waitForDialog()`.
   */
  async clickAndWaitForDialog(button: Locator, timeout = 15_000) {
    await expect(async () => {
      await button.click();
      await expect(this.dialog).toBeVisible({ timeout: 2_000 });
    }).toPass({ timeout });
  }

  /** Close dialog via cancel/close button */
  async closeDialog() {
    const closeBtn = this.dialog.getByRole('button', { name: /cancel|close/i });
    if (await closeBtn.isVisible()) {
      await closeBtn.click();
    } else {
      // Try clicking the overlay backdrop
      await this.page.locator('.mud-overlay').click({ position: { x: 10, y: 10 } });
    }
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Confirm/submit a dialog */
  async confirmDialog(buttonName: string | RegExp = /ok|confirm|create|save|delete|yes|submit|share|revoke/i) {
    await this.dialog.getByRole('button', { name: buttonName }).click();
    await this.page.waitForTimeout(env.timeouts.animation);
  }

  /** Fill a text field in the dialog by label or index */
  async fillInput(labelOrIndex: string | number, value: string) {
    let input: Locator;
    if (typeof labelOrIndex === 'number') {
      input = this.dialog.locator('input, textarea').nth(labelOrIndex);
    } else {
      input = this.dialog.getByLabel(labelOrIndex).or(
        this.dialog.locator(`input[placeholder*="${labelOrIndex}" i], textarea[placeholder*="${labelOrIndex}" i]`)
      );
    }
    await input.clear();
    await input.fill(value);
  }

  /** Check if dialog has an error alert */
  async expectError() {
    await expect(this.dialog.locator('.mud-alert-error, .mud-alert')).toBeVisible();
  }

  /** Check if dialog has a success state */
  async expectSuccess() {
    await expect(
      this.dialog.locator('.mud-alert-success, .mud-icon-root').filter({ hasText: /success|created|saved/i })
    ).toBeVisible();
  }
}

/**
 * Snackbar/notification helper.
 */
export class SnackbarHelper {
  readonly page: Page;

  constructor(page: Page) {
    this.page = page;
  }

  get snackbar(): Locator {
    return this.page.locator('.mud-snackbar');
  }

  async expectSuccess(message?: string | RegExp) {
    const snack = this.page.locator('.mud-snackbar-success, .mud-alert-success, .mud-snackbar').filter({
      hasText: message || /.+/
    });
    await expect(snack).toBeVisible({ timeout: 10_000 });
  }

  async expectError(message?: string | RegExp) {
    const snack = this.page.locator('.mud-snackbar-error, .mud-alert-error, .mud-snackbar').filter({
      hasText: message || /.+/
    });
    await expect(snack).toBeVisible({ timeout: 10_000 });
  }

  async expectWarning(message?: string | RegExp) {
    const snack = this.page.locator('.mud-snackbar-warning, .mud-alert-warning, .mud-snackbar').filter({
      hasText: message || /.+/
    });
    await expect(snack).toBeVisible({ timeout: 10_000 });
  }

  async dismissAll() {
    const snackbars = this.page.locator('.mud-snackbar .mud-icon-button, .mud-snackbar-close');
    const count = await snackbars.count();
    for (let i = 0; i < count; i++) {
      await snackbars.nth(i).click().catch(() => {});
    }
  }
}
