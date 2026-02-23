import { test as setup, expect } from '@playwright/test';
import { env } from './config/env';
import { KeycloakLoginPage } from './pages/keycloak-login.page';
import { ensureTestFixtures } from './helpers/test-fixtures';
import * as fs from 'fs';
import * as path from 'path';

const AUTH_DIR = path.join(__dirname, '.auth');

/**
 * Helper to authenticate a user via OIDC and save browser state.
 */
async function authenticateUser(
  page: import('@playwright/test').Page,
  username: string,
  password: string,
  stateFileName: string
) {
  const keycloak = new KeycloakLoginPage(page);
  await keycloak.fullLogin(username, password);

  // Verify we're authenticated — check for user display name in the app bar
  await expect(page.locator('.mud-appbar .mud-typography-body2')).toBeVisible({
    timeout: 15_000,
  });

  // Save auth state
  await page.context().storageState({ path: path.join(AUTH_DIR, stateFileName) });
}

/**
 * Global setup: authenticate as admin and save browser state for reuse.
 */
setup('authenticate as admin', async ({ page }) => {
  // Ensure auth directory exists
  if (!fs.existsSync(AUTH_DIR)) {
    fs.mkdirSync(AUTH_DIR, { recursive: true });
  }

  // Ensure test fixture files exist
  ensureTestFixtures();

  // Wait for the application to be ready
  let ready = false;
  for (let i = 0; i < 30; i++) {
    try {
      const response = await page.request.get(`${env.baseUrl}/health`);
      if (response.ok()) {
        ready = true;
        break;
      }
    } catch {
      // Server not ready yet
    }
    await page.waitForTimeout(2000);
  }
  if (!ready) {
    throw new Error(`Application at ${env.baseUrl} is not responding. Ensure docker-compose is running.`);
  }

  // Login as admin via Keycloak
  await authenticateUser(page, env.adminUser.username, env.adminUser.password, 'admin.json');
});

/**
 * Setup: authenticate as viewer user for permission tests.
 */
setup('authenticate as viewer', async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  
  // Login as viewer via Keycloak
  await authenticateUser(page, env.viewerUser.username, env.viewerUser.password, 'viewer.json');
  
  await context.close();
});
