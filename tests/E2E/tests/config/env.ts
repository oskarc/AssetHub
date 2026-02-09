/**
 * Environment configuration for E2E tests.
 * Centralizes all URLs, credentials, and test constants.
 */
export const env = {
  baseUrl: process.env.BASE_URL || 'http://localhost:7252',
  keycloakUrl: process.env.KC_URL || 'http://localhost:8080',
  keycloakRealm: 'media',
  keycloakClientId: 'assethub-app',
  keycloakClientSecret: 'VxBiV29QVchYHFzD5N62l43fTXbTMzSl',

  /** Pre-seeded admin user */
  adminUser: {
    username: 'mediaadmin',
    password: 'mediaadmin123',
    displayName: 'Media Admin',
    email: 'admin@media.local',
  },

  /** Pre-seeded viewer user */
  viewerUser: {
    username: 'testuser',
    password: 'testuser123',
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
    sharePasswordDefault: 'TestShare123!',
  },
} as const;
