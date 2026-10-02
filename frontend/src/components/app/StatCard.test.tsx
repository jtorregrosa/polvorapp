import { screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { StatCard } from './StatCard';

describe('StatCard', () => {
  it('is a link named by its label and figure, described by its context, with a decorative chevron', async () => {
    const { container } = await renderWithProviders(
      <MemoryRouter>
        <StatCard
          label="Con avisos"
          value="17"
          description="Necesitan atención"
          to="/arquebusiers?warning=ANY"
        />
      </MemoryRouter>,
    );

    const link = screen.getByRole('link', { name: 'Con avisos 17' });
    expect(link).toHaveAttribute('href', '/arquebusiers?warning=ANY');
    expect(link).toHaveAccessibleDescription('Necesitan atención');
    const chevron = container.querySelector('[data-stat-card] > svg');
    expect(chevron).toHaveAttribute('aria-hidden', 'true');
    // The focus ring is drawn on the whole card, not only around the text.
    expect(container.querySelector('[data-stat-card]')?.className).toContain(
      'has-[a:focus-visible]:outline-2',
    );
  });

  it('is plain text without a destination, with no chevron', async () => {
    const { container } = await renderWithProviders(<StatCard label="Arcabuceros" value="42" />);

    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(container.querySelector('svg')).toBeNull();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <MemoryRouter>
        <StatCard label="Con avisos" value="17" to="/arquebusiers?warning=ANY" />
      </MemoryRouter>,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
