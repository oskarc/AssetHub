import { type Page, type Locator } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for the local ASP.NET Core Identity sign-in form.
 *
 * Sign-in is served by the application itself at /login and POSTs to
 * /auth/login — there is no redirect to an external identity provider.
 */
export class IdentityLoginPage {
  readonly page: Page;
  readonly usernameInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.usernameInput = page.locator('#userName');
    this.passwordInput = page.locator('#password');
    this.submitButton = page.locator('form[action="/auth/login"] button[type="submit"]');
    this.errorAlert = page.locator('[role="alert"]');
  }

  /**
   * Fill both fields and confirm they kept their values, refilling if not.
   *
   * The page is pre-rendered, so the inputs are visible and fillable well before
   * the circuit hydrates — and hydration reconciles the DOM, discarding anything
   * typed first. Measured: filling on domcontentloaded leaves both fields empty;
   * filling after hydration retains both.
   *
   * Waiting on `window.Blazor` is NOT sufficient — it is defined as soon as
   * blazor.web.js parses, which is well before the component hydrates, so a wait
   * on it passes and the values still get wiped. Blazor exposes no
   * hydration-complete signal, so this asserts the property actually needed —
   * "the value survived" — instead of a proxy for it.
   *
   * Without this the form submits with an empty required username, the browser
   * blocks the POST, and the failure presents as a navigation timeout rather than
   * as a login error.
   */
  private async fillCredentials(username: string, password: string) {
    for (let attempt = 1; attempt <= 5; attempt++) {
      await this.usernameInput.fill(username);
      await this.passwordInput.fill(password);
      await this.page.waitForTimeout(250);

      const [u, p] = await Promise.all([
        this.usernameInput.inputValue(),
        this.passwordInput.inputValue(),
      ]);
      if (u === username && p === password) return;
    }
    throw new Error(
      'Credential fields kept being cleared after 5 attempts — hydration is ' +
        'discarding input for longer than expected, or the form markup changed.'
    );
  }

  /** Fill credentials and submit the local sign-in form */
  async login(username: string, password: string) {
    await this.fillCredentials(username, password);
    await this.submitButton.click();
  }

  /** Full login flow: open the sign-in page, submit credentials, wait for the app */
  async fullLogin(username: string, password: string) {
    // /auth/login redirects to /login under the Identity provider; going straight
    // to the page avoids depending on that hop.
    await this.page.goto('/login?returnUrl=%2F', { waitUntil: 'domcontentloaded' });

    await this.usernameInput.waitFor({ state: 'visible', timeout: 30_000 });

    await this.login(username, password);

    // A successful POST redirects to the return URL; a failed one comes back to
    // /login?error=… , so waiting for a non-login URL is the success signal.
    await this.page.waitForURL(
      (url) => !url.pathname.startsWith('/login'),
      { timeout: env.timeouts.navigation }
    );
  }

  /** Login as the pre-seeded admin user */
  async loginAsAdmin() {
    await this.fullLogin(env.adminUser.username, env.adminUser.password);
  }

  /** Login as the pre-seeded viewer user */
  async loginAsViewer() {
    await this.fullLogin(env.viewerUser.username, env.viewerUser.password);
  }
}
