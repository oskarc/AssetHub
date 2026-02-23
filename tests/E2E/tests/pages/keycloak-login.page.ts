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
    // Navigate directly to auth/login endpoint which triggers OIDC challenge
    // This bypasses the JS button click and goes straight to the OIDC flow
    await this.page.goto('/auth/login?returnUrl=%2F', { waitUntil: 'domcontentloaded' });
    
    // Wait for Keycloak login page to load (check for username field)
    await this.usernameInput.waitFor({ state: 'visible', timeout: 30_000 });
    
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
