import AxeBuilder from '@axe-core/playwright';
import { test as base, expect, type Page } from '@playwright/test';

const CSP_MARKER = 'CSP-VIOLATION';

const WEBKIT_ACCESS_CONTROL = ' due to access control checks.';

/**
 * WebKit reports a fetch that a navigation aborts as an access-control error. The app calls its own
 * API on the same origin, where no access control applies, so such a report for an `/api/` request
 * can only be an aborted request (the next page loads its own data), never a real failure.
 * Every other page error still fails the test.
 */
function isAbortedOwnApiRequest(message: string, baseURL: string | undefined): boolean {
  if (!baseURL || !message.endsWith(WEBKIT_ACCESS_CONTROL)) return false;
  const target = message.slice(0, -WEBKIT_ACCESS_CONTROL.length).split(' ').at(-1) ?? '';
  // The message reaches Playwright without the scheme (`/localhost:8080/api/...`): match host and path.
  return target.replace(/^(?:https?:)?\/+/, '').startsWith(`${new URL(baseURL).host}/api/`);
}

interface Fixtures {
  /** Fails the test on any Content-Security-Policy violation or uncaught page error. */
  pageProblems: string[];
  /** WCAG 2.2 A/AA violations of the page, or only of `include` (a CSS selector, e.g. an open dialog). */
  axeViolations: (target?: Page, include?: string) => Promise<string[]>;
}

export const test = base.extend<Fixtures>({
  pageProblems: [
    async ({ page, baseURL }, use) => {
      const problems: string[] = [];
      // `securitypolicyviolation` is the standard signal; the console message is Chromium's.
      await page.addInitScript((marker) => {
        document.addEventListener('securitypolicyviolation', (event) => {
          console.error(`${marker} ${event.violatedDirective} ${event.blockedURI}`);
        });
      }, CSP_MARKER);
      page.on('console', (message) => {
        const text = message.text();
        if (text.includes(CSP_MARKER) || text.includes('Content Security Policy')) problems.push(text);
      });
      page.on('pageerror', (error) => {
        if (!isAbortedOwnApiRequest(error.message, baseURL)) problems.push(`pageerror: ${error.message}`);
      });
      await use(problems);
      expect(problems, 'CSP violations or page errors').toEqual([]);
    },
    { auto: true },
  ],
  axeViolations: async ({ page }, use) => {
    await use(async (target = page, include) => {
      const builder = new AxeBuilder({ page: target }).withTags([
        'wcag2a',
        'wcag2aa',
        'wcag21a',
        'wcag21aa',
        'wcag22aa',
      ]);
      const results = await (include ? builder.include(include) : builder).analyze();
      return results.violations.map(
        (v) => `${v.id}: ${v.help} (${v.nodes.map((node) => node.target.join(' ')).join(', ')})`,
      );
    });
  },
});

export { expect };

/** Waits until the page heading is shown and the API calls of the shell have settled. */
export async function waitForShell(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.waitForLoadState('networkidle');
}

/** Opens the signed-in user's menu in the top bar, which holds the language and theme switchers. */
export async function openUserMenu(page: Page) {
  await page
    .getByRole('banner')
    .getByRole('button', { name: /^(Menú de|Menu for) / })
    .click();
  const menu = page.getByRole('menu');
  await expect(menu).toBeVisible();
  return menu;
}

/** Chooses a UI language (by its own name, e.g. "Valencià") in the user menu. */
export async function chooseLanguage(page: Page, name: string): Promise<void> {
  const menu = await openUserMenu(page);
  await menu.getByRole('menuitemradio', { name }).click();
}

/** Chooses a theme (by its label in the current language, e.g. "Oscuro") in the user menu. */
export async function chooseTheme(page: Page, name: string): Promise<void> {
  const menu = await openUserMenu(page);
  await menu.getByRole('menuitemradio', { name }).click();
}

/**
 * The primary navigation: visible in the sidebar on wide screens, in a drawer opened from the
 * top bar on small screens. Opens the drawer when needed and returns the navigation's container.
 */
export async function openNavigation(page: Page) {
  const sidebar = page.getByRole('navigation', {
    name: /^(Navegación principal|Navegació principal|Main navigation)$/,
  });
  if (!(await sidebar.isVisible())) {
    await page
      .getByRole('button', {
        name: /^(Mostrar u ocultar la navegación|Mostra o amaga la navegació|Show or hide navigation)$/,
      })
      .click();
    const drawer = page.getByRole('dialog');
    await expect(drawer).toBeVisible();
    return drawer;
  }
  return page.locator('[data-slot="sidebar"]');
}
