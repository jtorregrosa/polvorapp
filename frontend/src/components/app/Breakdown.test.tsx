import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Breakdown } from './Breakdown';

const GENDER = {
  title: 'Género',
  categoryLabel: 'Género',
  columns: [{ id: 'count', label: 'Arcabuceros' }],
  rows: [
    { id: 'FEMALE', label: 'Mujeres', counts: [3] },
    { id: 'MALE', label: 'Hombres', counts: [8] },
    { id: 'UNSPECIFIED', label: 'Sin definir', counts: [1] },
  ],
  total: 12,
};

const BRACKETS = {
  title: 'Tramos de edad',
  categoryLabel: 'Tramo',
  columns: [
    { id: 'FEMALE', label: 'Mujeres' },
    { id: 'MALE', label: 'Hombres' },
    { id: 'UNSPECIFIED', label: 'Sin definir' },
  ],
  rows: [
    { id: 'UNDER_25', label: 'Menos de 25', counts: [1, 2, 0] },
    { id: 'FROM_25_TO_34', label: '25 a 34', counts: [1000, 2, 1] },
  ],
  total: 1006,
  showRowTotal: true,
};

describe('Breakdown', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('shows each count with its share as text, and a bar hidden from assistive technology', async () => {
    const { container } = await renderWithProviders(<Breakdown {...GENDER} />);

    const row = screen.getByRole('row', { name: /Mujeres/ });
    expect(within(row).getByRole('cell', { name: '3' })).toBeInTheDocument();
    expect(within(row).getByText('25 %')).toBeInTheDocument();
    expect(screen.queryByRole('meter')).not.toBeInTheDocument();
    const meters = container.querySelectorAll('meter');
    expect(meters).toHaveLength(3);
    expect(meters[0]).toHaveAttribute('aria-hidden', 'true');
    expect(meters[0]).toHaveAttribute('value', '25');
  });

  it('caps the bar at 100 while the text keeps the figure', async () => {
    const { container } = await renderWithProviders(
      <Breakdown {...GENDER} total={2} rows={[{ id: 'FEMALE', label: 'Mujeres', counts: [3] }]} />,
    );

    expect(screen.getByText('150 %')).toBeInTheDocument();
    expect(container.querySelector('meter')).toHaveAttribute('value', '100');
  });

  it('names the table by its title and associates every cell with its row and column headers', async () => {
    await renderWithProviders(<Breakdown {...BRACKETS} />);

    const table = screen.getByRole('table', { name: 'Tramos de edad' });
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((h) => h.textContent);
    expect(headers).toEqual(['Tramo', 'Mujeres', 'Hombres', 'Sin definir', 'Total', '% del total']);
    expect(
      within(table)
        .getAllByRole('columnheader')
        .every((h) => h.getAttribute('scope') === 'col'),
    ).toBe(true);
    expect(within(table).getByRole('rowheader', { name: 'Menos de 25' })).toHaveAttribute('scope', 'row');
  });

  it('formats counts and shares in the active language', async () => {
    const { unmount } = await renderWithProviders(<Breakdown {...BRACKETS} />, 'es-ES');
    expect(screen.getByRole('cell', { name: '1000' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '1003' })).toBeInTheDocument();
    unmount();

    await renderWithProviders(<Breakdown {...BRACKETS} />, 'en');
    expect(screen.getByRole('cell', { name: '1,000' })).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: '% of total' })).toBeInTheDocument();
  });

  it('shows the counts without shares or meters when the total is zero', async () => {
    await renderWithProviders(
      <Breakdown {...GENDER} total={0} rows={GENDER.rows.map((row) => ({ ...row, counts: [0] }))} />,
    );

    expect(document.querySelector('meter')).toBeNull();
    expect(screen.queryByRole('columnheader', { name: '% del total' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('cell', { name: '0' })).toHaveLength(3);
  });

  it('scrolls inside its own focusable, named region', async () => {
    await renderWithProviders(<Breakdown {...BRACKETS} />);

    const region = screen.getByRole('region', { name: 'Tramos de edad' });
    expect(region).toHaveAttribute('tabindex', '0');
    expect(region.className).toContain('overflow-x-auto');
  });

  it.each(['light', 'dark'])('has no axe violations in the %s theme', async (theme) => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    const { container } = await renderWithProviders(<Breakdown {...BRACKETS} />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
