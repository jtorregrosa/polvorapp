import { defineConfig } from '@playwright/test';
import base from './playwright.config';

// Claude Code cloud sessions (docs/development.md#claude-code-cloud-sessions): the image ships one
// Chromium build of its own and no Firefox or WebKit, so the Chromium projects run on that build
// and the Firefox and WebKit projects are left to CI.
const CHROMIUM = `${process.env.PLAYWRIGHT_BROWSERS_PATH ?? '/opt/pw-browsers'}/chromium`;
const CHROMIUM_PROJECTS = new Set(['setup', 'desktop-chromium', 'mobile-360', 'serial-state']);
// The image's Chromium is older than Playwright's: the keyboard of the customizable select differs,
// so the select checks are left to CI as well.
const NEEDS_PLAYWRIGHT_CHROMIUM = /select\.spec\.ts$/;

export default defineConfig({
  ...base,
  projects: (base.projects ?? [])
    .filter((project) => project.name !== undefined && CHROMIUM_PROJECTS.has(project.name))
    .map((project) => ({
      ...project,
      testIgnore: [project.testIgnore ?? [], NEEDS_PLAYWRIGHT_CHROMIUM].flat(),
      dependencies: project.dependencies?.filter((name) => CHROMIUM_PROJECTS.has(name)),
      use: { ...project.use, launchOptions: { executablePath: CHROMIUM } },
    })),
});
