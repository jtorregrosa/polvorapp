import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { stubChartSize } from '@/test/chart-size';
import { renderWithProviders } from '@/test/render';
import { BarChart, type BarChartProps } from './BarChart';

// The drawing is checked through Recharts' own class names (recharts is pinned, design D4).

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

function bars(container: HTMLElement, series: string): Element[] {
  return [...container.querySelectorAll(`path.recharts-rectangle[name="${series}"]`)];
}

function yTicks(container: HTMLElement): string[] {
  // Intl writes "50 %" with a no-break space in Spanish.
  return [...container.querySelectorAll('.recharts-yAxis-tick-labels text')].map((text) =>
    text.textContent.replace(/\s/g, ' '),
  );
}

describe('BarChart', () => {
  beforeEach(stubChartSize);

  it('draws a focusable chart named by its heading and described by how to use it', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    const svg = surface(container);
    expect(svg).toHaveAttribute('tabindex', '0');
    expect(svg).toHaveAccessibleName('Procedencia del arma');
    expect(svg).toHaveAttribute('aria-roledescription', 'gráfico');
    expect(svg).toHaveAccessibleDescription(
      'Usa las flechas izquierda y derecha para oír los valores de cada categoría.',
    );
    // An empty <title>, so browsers show no native tooltip over the whole chart.
    expect(svg.querySelector('title')?.textContent).toBe('');
  });

  it('names every series in the legend, keys the provisional outline and gives each series its own pattern', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    const legend = screen.getByRole('list', { name: 'Leyenda: Procedencia del arma' });
    expect(
      within(legend)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Propia', 'Alquiler', 'Préstamo', 'Sin arma', 'Contorno discontinuo: provisional']);
    const fills = props.series.map((item) => bars(container, item.label)[0]?.getAttribute('fill') ?? '');
    expect(new Set(fills).size).toBe(4);
    for (const fill of fills) {
      const id = /^url\(#(.+)\)$/.exec(fill)?.[1] ?? '';
      expect(container.querySelector(`pattern[id="${id}"]`)).not.toBeNull();
    }
  });

  it('outlines the provisional category with a dashed line', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    expect(bars(container, 'Propia').map((bar) => bar.getAttribute('stroke-dasharray'))).toEqual([
      null,
      '4 2',
    ]);
  });

  it('stacks to 100 % with percentage ticks', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />);

    expect(yTicks(container)).toEqual(['0 %', '25 %', '50 %', '75 %', '100 %']);
  });

  it.each([
    ['grouped', false],
    ['stacked', true],
  ] as const)('draws %s bars', async (layout, sharesBase) => {
    const { container } = await renderWithProviders(<BarChart {...props} layout={layout} />);

    // Stacked series start where the one below ends; grouped ones sit side by side on the axis.
    const [owned] = bars(container, 'Propia');
    const [rental] = bars(container, 'Alquiler');
    const top = (bar: Element | undefined) => Number(bar?.getAttribute('y'));
    const bottom = (bar: Element | undefined) => top(bar) + Number(bar?.getAttribute('height'));
    expect(Math.abs(bottom(rental) - top(owned)) < 0.01).toBe(sharesBase);
    expect(yTicks(container).every((tick) => !tick.includes('%'))).toBe(true);
  });

  it('shows and says the values of a category from the keyboard, in the user language', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />, 'en');

    const svg = surface(container);
    act(() => {
      svg.focus();
    });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });

    const heading = await within(container).findByText('2031 (provisional)');
    expect(heading.parentElement).toHaveTextContent('Propia1,318');
    expect(screen.getByRole('status')).toHaveTextContent(
      '2031 (provisional): Propia 1,318, Alquiler 61, Préstamo 21 and Sin arma 12',
    );
  });

  it('moves back with the left arrow, stops at the ends and hides the tooltip with Escape', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />, 'en');
    const svg = surface(container);
    const tooltip = () => container.querySelector('.recharts-tooltip-wrapper');
    act(() => {
      svg.focus();
    });

    fireEvent.keyDown(svg, { key: 'ArrowRight' });
    fireEvent.keyDown(svg, { key: 'ArrowRight' });
    expect(await within(container).findByText('2031 (provisional)')).toBeInTheDocument();
    fireEvent.keyDown(svg, { key: 'ArrowLeft' });
    expect(await within(container).findByText('2030')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/^2030: /);

    fireEvent.keyDown(svg, { key: 'Escape' });
    await waitFor(() => {
      expect(tooltip()?.textContent ?? '').toBe('');
    });

    fireEvent.keyDown(svg, { key: 'ArrowRight' });
    expect(await within(container).findByText('2031 (provisional)')).toBeInTheDocument();
  });

  it('shows the values of a category when the pointer is over it', async () => {
    const { container } = await renderWithProviders(<BarChart {...props} />, 'en');
    const wrapper = container.querySelector('.recharts-wrapper');
    if (!wrapper) throw new Error('No chart drawn');

    fireEvent.mouseMove(wrapper, { clientX: 150, clientY: 120 });

    expect(await within(container).findByText('2030')).toBeInTheDocument();
  });

  it('offers every value in the table', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<BarChart {...props} />);

    await user.click(screen.getByRole('button', { name: 'Tabla de datos: Procedencia del arma' }));

    const table = screen.getByRole('table', { name: 'Procedencia del arma: datos' });
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
