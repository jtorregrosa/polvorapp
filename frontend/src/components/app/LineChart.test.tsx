import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { stubChartSize } from '@/test/chart-size';
import { renderWithProviders } from '@/test/render';
import { LineChart, type LineChartProps } from './LineChart';

const percent = (value: number) => `${String(Math.round(value * 100))} %`;

const props: LineChartProps = {
  title: 'Mujeres en activo',
  summary: '2031: 27 % de mujeres.',
  categoryLabel: 'Edición',
  formatValue: percent,
  series: [
    { key: 'women', label: 'Mujeres' },
    { key: 'firstYear', label: 'Primer año' },
  ],
  data: [
    { id: '2029', label: '2029', values: { women: 0.24, firstYear: 0.07 } },
    { id: '2030', label: '2030', values: { women: 0.25, firstYear: 0.06 } },
    { id: '2031', label: '2031', provisional: true, values: { women: 0.27, firstYear: 0.08 } },
  ],
};

function surface(container: HTMLElement): SVGElement {
  const svg = container.querySelector<SVGElement>('svg.recharts-surface');
  if (!svg) throw new Error('No chart drawn');
  return svg;
}

/** The `d` of the drawn line of each Recharts line, in order (solid, dashed, values per series). */
function linePaths(container: HTMLElement): (string | null)[] {
  return [...container.querySelectorAll('.recharts-line')].map(
    (line) => line.querySelector('path.recharts-curve')?.getAttribute('stroke-dasharray') ?? null,
  );
}

describe('LineChart', () => {
  beforeEach(stubChartSize);

  it('draws a focusable chart named by its heading, with a legend of the series', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />);

    expect(surface(container)).toHaveAttribute('tabindex', '0');
    expect(surface(container).querySelector('title')?.textContent).toBe('Mujeres en activo');
    const legend = screen.getByRole('list', { name: 'Leyenda' });
    expect(
      within(legend)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Mujeres', 'Primer año']);
  });

  it('tells the series apart by their marker shapes', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />);

    const markers = (color: string) =>
      [...container.querySelectorAll(`.recharts-line-dots path[stroke="${color}"]`)].map(
        (marker) => marker.getAttribute('d') ?? '',
      );
    // The first series draws circles (arcs), the second squares (straight edges); the provisional
    // point's marker is hollow.
    expect(markers('var(--chart-1)')).toHaveLength(3);
    expect(markers('var(--chart-1)').every((d) => d.includes('a'))).toBe(true);
    expect(markers('var(--chart-2)').every((d) => /h.*v/.test(d))).toBe(true);
    const hollow = container.querySelectorAll('.recharts-line-dots path[fill="var(--card)"]');
    expect(hollow).toHaveLength(2);
  });

  it('dashes the segment into the provisional edition and labels it in the axis', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />);

    expect(linePaths(container).filter((dash) => dash === '5 4')).toHaveLength(2);
    const ticks = [...container.querySelectorAll('.recharts-xAxis-tick-labels text')].map(
      (text) => text.textContent,
    );
    expect(ticks).toEqual(['2029', '2030', '2031', 'provisional']);
  });

  it('shows the formatted values of a point in a tooltip from the keyboard', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />, 'ca-ES-valencia');

    const svg = surface(container);
    act(() => {
      svg.focus();
    });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });

    const heading = await within(container).findByText('2031 (provisional)');
    const tooltip = heading.parentElement;
    if (!tooltip) throw new Error('No tooltip');
    expect(tooltip).toHaveTextContent('Mujeres27 %');
    expect(tooltip).toHaveTextContent('Primer año8 %');
  });

  it('offers every value in the table, formatted', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<LineChart {...props} />);

    await user.click(screen.getByRole('button', { name: 'Mostrar tabla' }));

    const table = screen.getByRole('table', { name: 'Mujeres en activo' });
    const row = within(table)
      .getByRole('rowheader', { name: '2031 (provisional)' })
      .closest('tr') as HTMLElement;
    expect(
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['27 %', '8 %']);
  });

  it.each(['es-ES', 'ca-ES-valencia', 'en'])('has no accessibility violations in %s', async (language) => {
    const { container } = await renderWithProviders(<LineChart {...props} />, language);

    expect(await axeViolations(container)).toEqual([]);
  });
});
