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
   * Fill credentials and submit the local sign-in form.
   *
   * A plain fill with no retry and no hydration wait, deliberately. /login is
   * statically rendered ([ExcludeFromInteractiveRouting]), so there is no circuit
   * and nothing hydrates over what was typed. If this ever needs a retry loop
   * again, the auth pages have stopped being static — fix that, not this.
   */
  async login(username: string, password: string) {
    await this.usernameInput.fill(username);
    await this.passwordInput.fill(password);
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
