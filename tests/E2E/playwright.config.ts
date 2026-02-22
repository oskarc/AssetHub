/// <reference types="node" />

import { defineConfig, devices } from '@playwright/test';

/**
 * AssetHub E2E Test Configuration
 *
 * Environment variables:
 *   BASE_URL   - Application URL (default: https://assethub.local:7252)
 *   KC_URL     - Keycloak URL (default: https://keycloak.assethub.local:8443)
 *   HEADED     - Run in headed mode (set to "true")
 *   CI         - Running in CI environment
 */

const BASE_URL = process.env.BASE_URL || 'https://assethub.local:7252';
const isCI = !!process.env.CI;

export default defineConfig({
  testDir: './tests',
  fullyParallel: false, // Sequential for stateful DAM operations
  forbidOnly: isCI,
  retries: isCI ? 2 : 0,
  workers: 1, // Single worker — tests share state (collections, assets, shares)
  reporter: [
    ['html', { open: isCI ? 'never' : 'on-failure' }],
    ['list'],
    ...(isCI ? [['junit', { outputFile: 'test-results/results.xml' }] as const] : []),
  ],
  timeout: 60_000,
  expect: {
    timeout: 15_000,
  },
  use: {
    baseURL: BASE_URL,
    ignoreHTTPSErrors: true,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'on-first-retry',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  projects: [
    // Auth setup — creates stored auth state for reuse
    {
      name: 'auth-setup',
      testMatch: /global\.setup\.ts/,
    },
    // Main test suite
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: './tests/.auth/admin.json',
      },
      dependencies: ['auth-setup'],
    },
  ],
  // Web server — optionally start the app via docker-compose
  // Uncomment if you want Playwright to manage the app lifecycle:
  // webServer: {
  //   command: 'docker-compose up -d && sleep 10',
  //   url: BASE_URL,
  //   reuseExistingServer: !isCI,
  //   timeout: 120_000,
  // },
});
