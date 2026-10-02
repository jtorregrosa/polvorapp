import { fireEvent, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ComparsaLogo } from './ComparsaLogo';

const LOGO_URL = '/api/comparsas/00000000-0000-4000-8000-000000000101/logo?v=1';

/** The tile wrapping the logo or its placeholder. */
function tile(container: HTMLElement): HTMLElement {
  const element = container.querySelector<HTMLElement>('[data-slot="comparsa-logo"]');
  if (!element) throw new Error('No logo tile rendered');
  return element;
}

/** The logo image inside the tile. */
function image(container: HTMLElement): HTMLImageElement {
  const element = tile(container).querySelector('img');
  if (!element) throw new Error('No logo image rendered');
  return element;
}

describe('ComparsaLogo', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('is decorative by default, because the comparsa name is next to it', async () => {
    const { container } = await renderWithProviders(<ComparsaLogo src={LOGO_URL} size="md" />);

    const image = tile(container).querySelector('img');
    expect(image).toHaveAttribute('src', LOGO_URL);
    expect(image).toHaveAttribute('alt', '');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('describes the logo when it stands alone', async () => {
    await renderWithProviders(
      <ComparsaLogo src={LOGO_URL} size="lg" alt="Logo de Comparsa Sintética Norte" />,
    );

    expect(screen.getByRole('img', { name: 'Logo de Comparsa Sintética Norte' })).toBeInTheDocument();
  });

  it('shows a placeholder without a logo', async () => {
    const { container } = await renderWithProviders(<ComparsaLogo src={null} size="sm" />);

    expect(tile(container).querySelector('img')).toBeNull();
    expect(tile(container).querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  });

  it('keeps the name of a standalone logo when only the placeholder can be shown', async () => {
    const { container } = await renderWithProviders(
      <ComparsaLogo src={LOGO_URL} size="lg" alt="Logo de Comparsa Sintética Norte" />,
    );

    fireEvent.error(image(container));

    expect(screen.getByRole('img', { name: 'Logo de Comparsa Sintética Norte' })).toBe(tile(container));
  });

  it('falls back to the placeholder when the logo cannot be loaded, never a broken image', async () => {
    const { container } = await renderWithProviders(<ComparsaLogo src={LOGO_URL} size="md" />);

    fireEvent.error(image(container));

    expect(tile(container).querySelector('img')).toBeNull();
    expect(tile(container).querySelector('svg')).toBeInTheDocument();
  });

  it('tries again when the logo changes after a failure', async () => {
    const { container, rerender } = await renderWithProviders(<ComparsaLogo src={LOGO_URL} size="md" />);
    fireEvent.error(image(container));

    rerender(<ComparsaLogo src={`${LOGO_URL}2`} size="md" />);

    expect(tile(container).querySelector('img')).toHaveAttribute('src', `${LOGO_URL}2`);
  });

  it.each([
    ['sm', 'size-8'],
    ['md', 'size-10'],
    ['lg', 'size-16'],
  ] as const)('renders the %s size (%s) on the light tile', async (size, sizeClass) => {
    const { container } = await renderWithProviders(<ComparsaLogo src={LOGO_URL} size={size} />);

    expect(tile(container)).toHaveClass(sizeClass, 'bg-logo-tile');
  });

  // Structure and names only: jsdom has no CSS, contrast is checked by styles/contrast.test.ts.
  it('has no automatically detectable accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <div>
        <ComparsaLogo src={LOGO_URL} size="md" />
        <ComparsaLogo src={null} size="md" />
        <ComparsaLogo src={LOGO_URL} size="lg" alt="Logo de Comparsa Sintética Norte" />
      </div>,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
