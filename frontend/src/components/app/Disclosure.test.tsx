import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Disclosure } from './Disclosure';

// Synthetic data only.
describe('Disclosure', () => {
  it('folds its details under the summary, a focusable control that opens them', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(
      <Disclosure summary="3 comparsas sin turno">
        <p>Comparsa Sintética Norte</p>
      </Disclosure>,
    );
    const details = container.querySelector('details');
    expect(details).not.toHaveAttribute('open');

    await user.tab();
    expect(screen.getByText('3 comparsas sin turno')).toHaveFocus();
    // Enter and Space are the browser's own behaviour for a summary; jsdom only clicks.
    await user.click(screen.getByText('3 comparsas sin turno'));

    expect(details).toHaveAttribute('open');
    expect(screen.getByText('Comparsa Sintética Norte')).toBeVisible();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <Disclosure summary="Ver las comparsas" defaultOpen>
        <p>Comparsa Sintética Sur</p>
      </Disclosure>,
    );
    expect(await axeViolations(container)).toEqual([]);
  });
});
