import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { stubChartSize } from '@/test/chart-size';
import { renderWithProviders } from '@/test/render';
import { BarChart, type BarChartProps } from './BarChart';

const props: BarChartProps = {
  title: 'Procedencia del arma',
  summary: 'En 2031, la mayoría lleva arma propia.',
  categoryLabel: 'Edición',
  layout: 'percent',
  series: [
    { key: 'owned', label: 'Propia' },
    { key: 'rental', label: 'Alquiler' },
    { key: 'loan', label: 'Préstamo' },
    { key: 'none', label: 'Sin arma' },
  ],
  data: [
    { id: '2030', label: '2030', values: { owned: 1306, rental: 60, loan: 20, none: 13 } },
    { id: '2031', label: '2031', provisional: true, values: { owned: 1318, rental: 61, loan: 21, none: 12 } },
  ],
};

function surface(container: HTMLElement): SVGElement {
  const svg = container.querySelector<SVGElement>('svg.recharts-surface');
  if (!svg) throw new Error('No chart drawn');
  return svg;
}

describe('BarChart', () => {
  beforeEach(stubChartSize);

  it('draws a focusable chart named by its heading and described by its summary', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    const svg = surface(container);
    expect(svg).toHaveAttribute('tabindex', '0');
    expect(svg.querySelector('title')?.textContent).toBe('Procedencia del arma');
    expect(svg.querySelector('desc')?.textContent).toBe(props.summary);
    expect(screen.getByRole('heading', { level: 2, name: 'Procedencia del arma' })).toBeInTheDocument();
  });

  it('names every series in the legend and gives each its own pattern', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    const legend = screen.getByRole('list', { name: 'Leyenda' });
    expect(
      within(legend)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Propia', 'Alquiler', 'Préstamo', 'Sin arma']);
    const fills = props.series.map((item) => {
      const bar = container.querySelector(`path.recharts-rectangle[name="${item.label}"]`);
      return bar?.getAttribute('fill') ?? '';
    });
    expect(new Set(fills).size).toBe(4);
    for (const fill of fills) {
      const id = /^url\(#(.+)\)$/.exec(fill)?.[1] ?? '';
      expect(container.querySelector(`pattern[id="${id}"]`)).not.toBeNull();
    }
  });

  it('marks the provisional category in the axis and with a dashed outline', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    const ticks = [...container.querySelectorAll('.recharts-xAxis-tick-labels text')].map(
      (text) => text.textContent,
    );
    expect(ticks).toEqual(['2030', '2031', 'provisional']);
    const bars = [...container.querySelectorAll('path.recharts-rectangle[name="Propia"]')];
    expect(bars.map((bar) => bar.getAttribute('stroke-dasharray'))).toEqual([null, '4 2']);
  });

  it('shows the values of a category in a tooltip from the keyboard, in the user language', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />, 'en');

    const svg = surface(container);
    act(() => {
      svg.focus();
    });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });

    const heading = await within(container).findByText('2031 (provisional)');
    const tooltip = heading.parentElement;
    if (!tooltip) throw new Error('No tooltip');
    expect(within(tooltip).getByText('Propia').parentElement?.nextElementSibling?.textContent).toBe('1,318');
    expect(tooltip).toHaveTextContent('Sin arma12');
  });

  it('offers every value in the table', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<BarChart {...props} />);

    await user.click(screen.getByRole('button', { name: 'Mostrar tabla' }));

    const table = screen.getByRole('table', { name: 'Procedencia del arma' });
    expect(
      within(table)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['1306', '60', '20', '13', '1318', '61', '21', '12']);
  });

  it('fades in only when the user allows motion and never animates the bars', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    const frame = surface(container).closest('[data-slot="chart"]')?.parentElement;
    expect(frame?.className.split(' ')).toEqual(
      expect.arrayContaining(['motion-safe:animate-in', 'motion-safe:fade-in-0', 'motion-safe:duration-200']),
    );
    expect(frame?.className).not.toMatch(/(?:^|\s)(?:animate-in|duration-\d+)/);
  });

  it.each(['es-ES', 'ca-ES-valencia', 'en'])('has no accessibility violations in %s', async (language) => {
    const { container } = await renderWithProviders(<BarChart {...props} />, language);

    expect(await axeViolations(container)).toEqual([]);
  });
});
