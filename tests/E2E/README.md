# AssetHub E2E Tests (Playwright)

Comprehensive end-to-end test suite for the AssetHub Digital Asset Management application.

## Prerequisites

- **Node.js** 18+ 
- **Docker Compose** (to run the app stack)
- The AssetHub application running at `http://localhost:7252` (API at `/api/v1/`)

## Quick Start

```bash
# 1. Start the application
cd <project-root>
docker-compose up -d

# 2. Install dependencies  
cd tests/E2E
npm install

# 3. Install Playwright browsers
npx playwright install chromium

# 4. Run all tests
npm test
```

## Test Scripts

| Script | Description |
|--------|-------------|
| `npm test` | Run all tests (headless) |
| `npm run test:headed` | Run tests in browser (visible) |
| `npm run test:ui` | Launch Playwright UI mode |
| `npm run test:debug` | Debug mode (step through tests) |
| `npm run test:smoke` | Run smoke tests only |
| `npm run test:auth` | Run authentication tests |
| `npm run test:collections` | Run collection management tests |
| `npm run test:assets` | Run asset management tests |
| `npm run test:shares` | Run share feature tests |
| `npm run test:admin` | Run admin panel tests |
| `npm run test:api` | Run API integration tests |
| `npm run report` | Open the HTML test report |

## Test Structure

```
tests/E2E/
├── playwright.config.ts        # Playwright configuration
├── package.json
├── tests/
│   ├── global.setup.ts         # Auth setup (Keycloak login → saved state)
│   ├── config/
│   │   └── env.ts              # Environment variables, credentials, constants
│   ├── pages/                  # Page Object Models
│   │   ├── keycloak-login.page.ts
│   │   ├── login.page.ts
│   │   ├── layout.page.ts
│   │   ├── assets.page.ts
│   │   ├── asset-detail.page.ts
│   │   ├── all-assets.page.ts
│   │   ├── admin.page.ts
│   │   └── share.page.ts
│   ├── helpers/                # Shared utilities
│   │   ├── api-helper.ts       # Direct API calls for setup/teardown
│   │   ├── dialog-helper.ts    # MudBlazor dialog/snackbar helpers
│   │   └── test-fixtures.ts    # Test file generators (PNG, PDF)
│   ├── fixtures/               # Generated test files (auto-created)
│   └── specs/                  # Test specifications
│       ├── 01-auth.spec.ts         # Authentication & login flows
│       ├── 02-navigation.spec.ts   # Layout, nav, routing
│       ├── 03-collections.spec.ts  # Collection CRUD
│       ├── 04-assets.spec.ts       # Asset upload, browse, detail, edit
│       ├── 05-shares.spec.ts       # Share creation, public access, revocation
│       ├── 06-admin.spec.ts        # Admin panel: shares, ACLs, users
│       ├── 07-all-assets.spec.ts   # Cross-collection asset browser
│       ├── 08-api.spec.ts          # API endpoint integration tests
│       ├── 09-acl.spec.ts          # Access control & permissions
│       ├── 10-viewer-role.spec.ts  # Viewer role restrictions
│       ├── 11-edge-cases.spec.ts   # Error handling, 404s, back/forward
│       ├── 12-responsive-a11y.spec.ts  # Responsive design, accessibility
│       ├── 13-workflows.spec.ts    # Full end-to-end workflow scenarios
│       ├── 14-language.spec.ts     # Locale switching, translations
│       └── 15-ui-features.spec.ts  # Image editor, export presets, migrations
```

## Test Coverage

### Features Tested

| Feature | Test File | Tests |
|---------|-----------|-------|
| **Authentication** | 01-auth | Login/logout, Keycloak flow, invalid credentials, protected routes |
| **Navigation** | 02-navigation | AppBar, drawer, nav links, dark mode, direct URLs |
| **Collections** | 03-collections | CRUD, tree, breadcrumbs, context menu, sub-collections |
| **Assets** | 04-assets | Upload, browse, search, filter, sort, view modes, detail, edit, delete |
| **Shares** | 05-shares | Create, password protection, public access, revocation |
| **Admin Panel** | 06-admin | Share management, collection access, user management, export presets, migrations |
| **All Assets** | 07-all-assets | Cross-collection search, filters, pagination, card actions |
| **API** | 08-api | Health, CRUD endpoints, file endpoints, auth guards |
| **ACL** | 09-acl | Grant/revoke access, role upgrades, UI visibility |
| **Role Restrictions** | 10-viewer-role | Viewer nav restrictions, admin page blocks |
| **Edge Cases** | 11-edge-cases | 404s, invalid GUIDs, rapid navigation, browser back/forward |
| **Responsive/A11y** | 12-responsive-a11y | Mobile/tablet viewports, keyboard navigation, theme persistence |
| **Workflows** | 13-workflows | Full create→upload→edit→share→admin workflows |
| **Language** | 14-language | Locale switching, Swedish/English translations |
| **UI Features** | 15-ui-features | Image editor, export presets, bulk migration UI |

### Seeded Test Accounts

| User | Role | Username | Password |
|------|------|----------|----------|
| Admin | admin | `mediaadmin` | `mediaadmin123` |
| Viewer | viewer | `testuser` | `testuser123` |

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `BASE_URL` | `http://localhost:7252` | Application URL |
| `KC_URL` | `http://localhost:8080` | Keycloak URL |
| `CI` | - | Set in CI to enable retries and JUnit reporter |

## Tagging

Tests are tagged for selective execution:

- `@smoke` — Critical path tests
- `@auth` — Authentication tests
- `@collections` — Collection management
- `@assets` — Asset management
- `@shares` — Share features
- `@admin` — Admin panel
- `@api` — API integration
- `@acl` — Access control
- `@e2e` — Full workflow tests
- `@ui` — Responsive/accessibility
- `@edge-cases` — Error handling

Run tagged tests: `npx playwright test --grep @smoke`

## Browser Targets

Tests run across 4 browser configurations:

| Project | Browser | Viewport |
|---------|---------|----------|
| `chromium` | Desktop Chrome | 1280x720 |
| `firefox` | Desktop Firefox | 1280x720 |
| `webkit` | Desktop Safari | 1280x720 |
| `mobile-chrome` | Mobile Chrome | Pixel 5 (393x851) |
