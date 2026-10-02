import axe from 'axe-core';

/** The axe rule tags of WCAG 2.2 A/AA (NFR-07); `wcag22aa` brings in `target-size`. */
export const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/**
 * WCAG 2.2 A/AA violations found by axe in `container` (NFR-07). jsdom has no layout, so contrast
 * and target size are measured only by the Playwright checks in a real browser.
 */
export async function axeViolations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: { type: 'tag', values: WCAG_TAGS },
  });
  return results.violations;
}
