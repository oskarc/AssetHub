/**
 * Environment configuration for E2E tests.
 * Centralizes all URLs, credentials, and test constants.
 */

const processEnv =
  (
    globalThis as {
      process?: { env?: Record<string, string | undefined> };
    }
  ).process?.env ?? {};

function required(name: string): string {
  const value = processEnv[name];
  if (!value) throw new Error(`Missing required environment variable: ${name}`);
  return value;
}

export const env = {
  baseUrl: processEnv.BASE_URL || 'https://assethub.local:7252',

  /** Pre-seeded admin user */
  adminUser: {
    username: required('ADMIN_USERNAME'),
    password: required('ADMIN_PASSWORD'),
    displayName: 'Media Admin',
    email: 'admin@media.local',
  },

  /** Pre-seeded viewer user */
  viewerUser: {
    username: required('VIEWER_USERNAME'),
    password: required('VIEWER_PASSWORD'),
    displayName: 'Test User',
    email: 'test@example.com',
  },

  /** Timeouts */
  timeouts: {
    upload: 30_000,
    processing: 60_000,
    navigation: 15_000,
    animation: 1_000,
    debounce: 1_500,
  },

  /** Test data constants */
  testData: {
    collectionPrefix: 'E2E-Test',
    assetTitlePrefix: 'E2E-Asset',
    sharePasswordDefault: required('SHARE_PASSWORD'),
  },
} as const;
