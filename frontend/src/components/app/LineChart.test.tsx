import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { stubChartSize } from '@/test/chart-size';
import { renderWithProviders } from '@/test/render';
import { LineChart, type LineChartProps } from './LineChart';

// The drawing is checked through Recharts' own class names (recharts is pinned, design D4).

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

/** The marker paths drawn in a series colour. */
function markers(container: HTMLElement, color: string): string[] {
  return [...container.querySelectorAll(`.recharts-line-dots path[stroke="${color}"]`)].map(
    (marker) => marker.getAttribute('d') ?? '',
  );
}

/** How many drawn lines are dashed. */
function dashedLines(container: HTMLElement): number {
  return [...container.querySelectorAll('.recharts-line path.recharts-curve')].filter(
    (curve) => curve.getAttribute('stroke-dasharray') === '5 4',
  ).length;
}

describe('LineChart', () => {
  beforeEach(stubChartSize);

  it('draws a focusable chart named by its heading, with a legend of the series and of the provisional marking', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />);

    expect(surface(container)).toHaveAttribute('tabindex', '0');
    expect(surface(container)).toHaveAccessibleName('Mujeres en activo');
    const legend = screen.getByRole('list', { name: 'Leyenda: Mujeres en activo' });
    expect(
      within(legend)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Mujeres', 'Primer año', 'Línea discontinua y marca hueca: provisional']);
  });

  it('tells the series apart by their marker shapes and hollows the provisional point', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />);

    // The first series draws circles (arcs), the second squares (straight edges).
    expect(markers(container, 'var(--chart-1)')).toHaveLength(3);
    expect(markers(container, 'var(--chart-1)').every((d) => d.includes('a'))).toBe(true);
    expect(markers(container, 'var(--chart-2)').every((d) => /h.*v/.test(d))).toBe(true);
    expect(container.querySelectorAll('.recharts-line-dots path[fill="var(--card)"]')).toHaveLength(2);
  });

  it('dashes every segment that touches a provisional point, also one in the middle', async () => {
    const { container } = await renderWithProviders(
      <LineChart
        {...props}
        series={props.series.slice(0, 1)}
        data={[
          { id: '2029', label: '2029', values: { women: 0.24 } },
          { id: '2030', label: '2030', provisional: true, values: { women: 0.25 } },
          { id: '2031', label: '2031', values: { women: 0.27 } },
        ]}
      />,
    );

    // A solid line for the settled points and a dashed one through the middle provisional point.
    expect(dashedLines(container)).toBe(1);
    const dashed = [...container.querySelectorAll('.recharts-line path.recharts-curve')].find(
      (curve) => curve.getAttribute('stroke-dasharray') === '5 4',
    );
    // Three points, so two segments (one "L" after the start).
    expect(dashed?.getAttribute('d')?.match(/L/g)).toHaveLength(2);
  });

  it('shows and says the formatted values of a point from the keyboard', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />, 'ca-ES-valencia');

    const svg = surface(container);
    act(() => {
      svg.focus();
    });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });

    const heading = await within(container).findByText('2031 (provisional)');
    expect(heading.parentElement).toHaveTextContent('Mujeres27 %');
    expect(screen.getByRole('status')).toHaveTextContent('2031 (provisional): Mujeres 27 % i Primer año 8 %');
  });

  it('fades in only when the user allows motion and hides the tooltip with Escape', async () => {
    const { container } = await renderWithProviders(<LineChart {...props} />);
    const svg = surface(container);
    const frame = svg.closest('[data-slot="chart"]')?.parentElement;
    expect(frame?.className.split(' ')).toEqual(
      expect.arrayContaining(['motion-safe:animate-in', 'motion-safe:fade-in-0', 'motion-safe:duration-200']),
    );

    act(() => {
      svg.focus();
    });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });
    expect(await within(container).findByText('2030')).toBeInTheDocument();
    fireEvent.keyDown(svg, { key: 'Escape' });

    await waitFor(() => {
      expect(container.querySelector('.recharts-tooltip-wrapper')?.textContent ?? '').toBe('');
    });
  });

  it('offers every value in the table, formatted', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<LineChart {...props} />);

    await user.click(screen.getByRole('button', { name: 'Tabla de datos: Mujeres en activo' }));

    const table = screen.getByRole('table', { name: 'Mujeres en activo: datos' });
    const row = within(table).getByRole('rowheader', { name: '2031 (provisional)' }).closest('tr');
    if (!row) throw new Error('No row');
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
