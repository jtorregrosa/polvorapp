import { expect, test, waitForShell } from './fixtures';

/**
 * Styled select (design D11 of redesign-design-system): Chromium shows the list in the menu style
 * (customizable select); Firefox and WebKit keep their native list. Selection, focus and the hidden
 * placeholder must work the same everywhere. Runs as the Admin, who chooses among the comparsas.
 */

test.beforeEach(async ({ page }) => {
  await page.goto('/arquebusiers/new');
  await waitForShell(page);
  // The comparsas load after the page: wait for them before choosing.
  await expect(
    page.getByRole('combobox', { name: /Comparsa/ }).locator('option:not([value=""])'),
  ).not.toHaveCount(0);
});

/** Whether the browser styles select lists (customizable select). */
async function stylesSelects(page: import('@playwright/test').Page): Promise<boolean> {
  return page.evaluate(() => CSS.supports('appearance', 'base-select'));
}

test('chooses a comparsa and keeps focus on the select', async ({ page, browserName }) => {
  const select = page.getByRole('combobox', { name: /Comparsa/ });
  const last = await select.locator('option').last().getAttribute('value');
  expect(last).toBeTruthy();
  await expect(select).not.toHaveValue(last ?? '');

  await select.focus();
  // Playwright's WebKit build reports the styled list but does not drive it like Safari: the
  // keyboard path is checked in Chromium, the others choose as the browser does.
  if (browserName === 'chromium' && (await stylesSelects(page))) {
    // The styled list opens from the keyboard; the arrows and End move, and Enter chooses.
    await page.keyboard.press('Enter');
    expect(await select.evaluate((element) => element.matches(':open'))).toBe(true);
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');
  } else {
    // Native lists of Firefox and WebKit live outside the page: choose as the browser does.
    await select.selectOption(last ?? '');
  }

  await expect(select).toHaveValue(last ?? '');
  await expect(select).toBeFocused();
});

test('shows the placeholder but never offers it as a choice', async ({ page }) => {
  const select = page.getByRole('combobox', { name: /Comparsa/ });
  const placeholder = select.locator('option[value=""]');

  await expect(select).toHaveValue('');
  await expect(placeholder).toBeDisabled();
  await expect(placeholder).toHaveAttribute('hidden', '');
  expect(await select.evaluate((element: HTMLSelectElement) => element.selectedOptions[0]?.text)).toBe(
    'Elige una opción.',
  );
});

test('shows the list in the menu style where the browser supports it', async ({ page }) => {
  test.skip(!(await stylesSelects(page)), 'This browser has no customizable select');
  const select = page.getByRole('combobox', { name: /Comparsa/ });

  expect(await select.evaluate((element) => getComputedStyle(element).appearance)).toBe('base-select');
  await select.focus();
  await page.keyboard.press('Enter');
  const picker = await select.evaluate((element) => {
    const style = getComputedStyle(element, '::picker(select)');
    return { radius: style.borderTopLeftRadius, shadow: style.boxShadow };
  });
  expect(picker.radius).not.toBe('0px');
  expect(picker.shadow).not.toBe('none');
});
