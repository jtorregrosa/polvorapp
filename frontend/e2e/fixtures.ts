import AxeBuilder from '@axe-core/playwright';
import { test as base, expect, type Page } from '@playwright/test';

const CSP_MARKER = 'CSP-VIOLATION';

interface Fixtures {
  /** Fails the test on any Content-Security-Policy violation or uncaught page error. */
  pageProblems: string[];
  /** WCAG 2.1 A/AA violations on the current page (NFR-07). */
  axeViolations: () => Promise<string[]>;
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
    await use(async () => {
      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        .analyze();
      return results.violations.map((v) => `${v.id}: ${v.help}`);
    });
  },
});

export { expect };

/** Waits until the footer shows the API version, i.e. the shell and the API are both up. */
export async function waitForShell(page: Page): Promise<void> {
  await expect(page.getByRole('contentinfo')).toContainText(/\d+\.\d+/);
}
