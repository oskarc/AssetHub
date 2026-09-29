import { type Page, type Locator } from '@playwright/test';

/**
 * Page Object for the status pages: not-found (404) and access-denied (403).
 *
 * Headings are located by role AND level 1, so a page that renders its title as
 * anything other than the one <h1> fails here rather than passing on text alone.
 * Actions are links (MudButton with Href renders an <a>), which is also what
 * lets them work before the circuit starts.
 */
export class ErrorPagesPage {
  readonly page: Page;
  readonly headings: Locator;
  readonly notFoundHeading: Locator;
  readonly accessDeniedHeading: Locator;
  readonly goHome: Locator;
  readonly signInAgain: Locator;
  readonly blazorErrorUi: Locator;
  readonly loginForm: Locator;

  constructor(page: Page) {
    this.page = page;
    this.headings = page.locator('h1');
    this.notFoundHeading = page.getByRole('heading', { level: 1, name: /page not found/i });
    this.accessDeniedHeading = page.getByRole('heading', { level: 1, name: /access denied/i });
    this.goHome = page.locator('#main-content').getByRole('link', { name: /go home/i });
    this.signInAgain = page.locator('#main-content').getByRole('link', { name: /sign in again/i });
    // Blazor's "An unhandled error has occurred" bar — shown when the circuit dies.
    this.blazorErrorUi = page.locator('#blazor-error-ui');
    this.loginForm = page.locator('#userName');
  }

  /**
   * Client-side navigation inside the running circuit. Blazor.navigateTo is not an
   * intercepted link click, so the Router handles an unknown route itself instead
   * of forcing a full page load — the path a broken in-app link takes.
   */
  async navigateInCircuit(path: string) {
    await this.page.evaluate((p) => (globalThis as any).Blazor.navigateTo(p), path);
  }
}
