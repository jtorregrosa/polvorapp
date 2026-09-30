import AxeBuilder from '@axe-core/playwright';
import { test as base, expect, type Page } from '@playwright/test';

const CSP_MARKER = 'CSP-VIOLATION';

interface Fixtures {
  /** Fails the test on any Content-Security-Policy violation or uncaught page error. */
  pageProblems: string[];
  /** WCAG 2.1 A/AA violations on the current page, or on `target` (NFR-07). */
  axeViolations: (target?: Page) => Promise<string[]>;
}

export const test = base.extend<Fixtures>({
  pageProblems: [
    async ({ page }, use) => {
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
      page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));
      await use(problems);
      expect(problems, 'CSP violations or page errors').toEqual([]);
    },
    { auto: true },
  ],
  axeViolations: async ({ page }, use) => {
    await use(async (target = page) => {
      const results = await new AxeBuilder({ page: target })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        .analyze();
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
