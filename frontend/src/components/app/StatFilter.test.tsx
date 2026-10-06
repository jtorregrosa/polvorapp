import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { StatFilter } from './StatFilter';

function Counters() {
  const [pressed, setPressed] = useState(false);
  return (
    <StatFilter
      label="Resumen de arcabuceros"
      items={[
        {
          id: 'expired',
          label: 'Licencia caducada',
          count: 3,
          tone: 'warning',
          pressed,
          onPressedChange: setPressed,
        },
        {
          id: 'pending',
          label: 'Licencia en trámite',
          count: 0,
          pressed: false,
          onPressedChange: () => undefined,
        },
      ]}
    />
  );
}

describe('StatFilter', () => {
  it('names each counter by its figure and label, and toggles its pressed state', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Counters />);

    const expired = screen.getByRole('button', { name: '3 Licencia caducada' });
    expect(expired).toHaveAttribute('aria-pressed', 'false');
    await user.click(expired);

    expect(expired).toHaveAttribute('aria-pressed', 'true');
  });

  it('marks a pressed counter with a check besides its colour (WCAG 1.4.1)', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(<Counters />);
    expect(container.querySelector('[data-pressed-mark]')).toBeNull();

    await user.click(screen.getByRole('button', { name: '3 Licencia caducada' }));

    const mark = screen
      .getByRole('button', { name: '3 Licencia caducada' })
      .querySelector('[data-pressed-mark]');
    expect(mark).not.toBeNull();
    expect(mark).toHaveAttribute('aria-hidden', 'true');
    expect(
      screen.getByRole('button', { name: '0 Licencia en trámite' }).querySelector('[data-pressed-mark]'),
    ).toBeNull();
  });

  it('has no accessibility violations, pressed or not', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(<Counters />);
    expect(await axeViolations(container)).toEqual([]);

    await user.click(screen.getByRole('button', { name: '3 Licencia caducada' }));
    expect(await axeViolations(container)).toEqual([]);
  });

  it.each([
    [5, 'lg:grid-cols-5'],
    [6, 'xl:grid-cols-6'],
  ])('sets %i counters in rows without a lone card (UI audit T6)', async (count, columns) => {
    const items = Array.from({ length: count }, (_, index) => ({
      id: `c${String(index)}`,
      label: `Contador ${String(index)}`,
      count: index,
      pressed: false,
      onPressedChange: () => undefined,
    }));
    await renderWithProviders(<StatFilter label="Resumen" items={items} />);

    expect(screen.getByRole('group', { name: 'Resumen' })).toHaveClass('sm:grid-cols-3', columns);
  });
});
