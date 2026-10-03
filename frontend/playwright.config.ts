import { defineConfig, devices } from '@playwright/test';
import { ADMIN_STATE } from './e2e/identity';

/** Specs that change shared state, run by the `serial-state` project only. */
const SERIAL_STATE = /serial-state[\\/].*\.spec\.ts$/;

// E2E against the running compose stack (docs/development.md#end-to-end-tests).
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  // The CI runner has 4 vCPUs and also runs the compose stack: one is left for it.
  workers: process.env.CI ? 3 : undefined,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['github'], ['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    locale: 'es-ES',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  // `setup` signs the seeded users in once; the other projects start as the signed-in Admin.
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/, use: { ...devices['Desktop Chrome'] } },
    {
      name: 'desktop-chromium',
      testIgnore: SERIAL_STATE,
      use: { ...devices['Desktop Chrome'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'mobile-360',
      testIgnore: SERIAL_STATE,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 360, height: 740 },
        isMobile: true,
        hasTouch: true,
        storageState: ADMIN_STATE,
      },
      dependencies: ['setup'],
    },
    // Native date fields and select lists differ per engine: those checks also run in Firefox and
    // WebKit.
    {
      name: 'dates-firefox',
      testMatch: /(dates|select)\.spec\.ts$/,
      use: { ...devices['Desktop Firefox'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'dates-webkit',
      testMatch: /(dates|select)\.spec\.ts$/,
      use: { ...devices['Desktop Safari'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    // Image decoding, EXIF orientation, transparency and canvas export differ per engine: the photo
    // and logo flows too. The compliance insights also run here, for their tables, meters and
    // native selects in every engine.
    {
      name: 'photos-firefox',
      testMatch: /(photos|comparsa-logos|insights)\.spec\.ts$/,
      use: { ...devices['Desktop Firefox'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'photos-webkit',
      testMatch: /(photos|comparsa-logos|insights)\.spec\.ts$/,
      use: { ...devices['Desktop Safari'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    // Specs that change state every other spec reads (the registry lock, the current edition's
    // orders) run alone, after all the others have passed, and restore what they change
    // (add-festival-editions, design D11).
    {
      name: 'serial-state',
      testMatch: SERIAL_STATE,
      fullyParallel: false,
      // One file at a time: each changes state the others read (orders open, registry lock, Norte's order).
      workers: 1,
      use: { ...devices['Desktop Chrome'], storageState: ADMIN_STATE },
      dependencies: [
        'desktop-chromium',
        'mobile-360',
        'dates-firefox',
        'dates-webkit',
        'photos-firefox',
        'photos-webkit',
      ],
    },
  ],
});
