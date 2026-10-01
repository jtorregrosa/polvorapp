import { defineConfig, devices } from '@playwright/test';
import { ADMIN_STATE } from './e2e/identity';

// E2E against the running compose stack (docs/development.md#end-to-end-tests).
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 2 : undefined,
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
      use: { ...devices['Desktop Chrome'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'mobile-360',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 360, height: 740 },
        isMobile: true,
        hasTouch: true,
        storageState: ADMIN_STATE,
      },
      dependencies: ['setup'],
    },
    // Native date fields differ per engine: the date checks also run in Firefox and WebKit.
    {
      name: 'dates-firefox',
      testMatch: /dates\.spec\.ts$/,
      use: { ...devices['Desktop Firefox'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'dates-webkit',
      testMatch: /dates\.spec\.ts$/,
      use: { ...devices['Desktop Safari'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    // Image decoding, EXIF orientation and canvas export differ per engine: the photo flows too.
    {
      name: 'photos-firefox',
      testMatch: /photos\.spec\.ts$/,
      use: { ...devices['Desktop Firefox'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
    {
      name: 'photos-webkit',
      testMatch: /photos\.spec\.ts$/,
      use: { ...devices['Desktop Safari'], storageState: ADMIN_STATE },
      dependencies: ['setup'],
    },
  ],
});
