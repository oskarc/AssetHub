import { test as setup, expect } from '@playwright/test';
import { env } from './config/env';
import { KeycloakLoginPage } from './pages/keycloak-login.page';
import { ensureTestFixtures } from './helpers/test-fixtures';
import * as fs from 'fs';
import * as path from 'path';

const AUTH_DIR = path.join(__dirname, '.auth');

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

  // Login via Keycloak
  const keycloak = new KeycloakLoginPage(page);
  await keycloak.loginAsAdmin();

  // Verify we're authenticated
  await expect(page.getByText(/sign out/i).or(page.getByText(/media admin/i).or(page.locator('.mud-drawer')))).toBeVisible({
    timeout: 15_000,
  });

  // Save auth state
  await page.context().storageState({ path: path.join(AUTH_DIR, 'admin.json') });
});
