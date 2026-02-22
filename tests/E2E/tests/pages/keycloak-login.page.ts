import { type Page, type Locator, expect } from '@playwright/test';
import { env } from '../config/env';

/**
 * Page Object for Keycloak login form.
 * Handles authentication through the Keycloak OIDC flow.
 */
export class KeycloakLoginPage {
  readonly page: Page;
  readonly usernameInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.usernameInput = page.locator('#username');
    this.passwordInput = page.locator('#password');
    this.submitButton = page.locator('#kc-login');
  }

  /** Fill credentials and submit the Keycloak login form */
  async login(username: string, password: string) {
    await this.usernameInput.fill(username);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }

  /** Full login flow: navigate to app, trigger OIDC, fill Keycloak form, wait for redirect back */
  async fullLogin(username: string, password: string) {
    // Navigate to login page
    await this.page.goto('/login');
    // Click sign-in (triggers OIDC redirect) — target the large primary button
    await this.page.locator('button.mud-button-filled-primary.mud-button-filled-size-large').click();
    // Wait for Keycloak login page
    await this.page.waitForURL(/.*keycloak.*|.*8443.*/);
    // Fill and submit
    await this.login(username, password);
    // Wait for redirect back to the app
    await this.page.waitForURL(/.*assethub\.local.*/, { timeout: env.timeouts.navigation });
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
