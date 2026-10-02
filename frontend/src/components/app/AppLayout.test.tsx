import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { House } from 'lucide-react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { AppLayout, type SidebarCard } from './AppLayout';
import { ComparsaLogo } from './ComparsaLogo';

const LONG_NAME = 'Comparsa Sintética con un nombre larguísimo que no cabe en dos líneas de la barra lateral';

const CARDS: SidebarCard[] = [
  {
    to: '/comparsas/1',
    label: 'Comparsa Sintética Norte',
    media: <ComparsaLogo src="/logo-norte.png" size="md" />,
  },
  { to: '/comparsas/2', label: LONG_NAME, media: <ComparsaLogo src={null} size="md" /> },
];

function renderLayout(cards: readonly SidebarCard[] | undefined, path = '/') {
  const layout = (heading: string) => (
    <AppLayout navigation={[{ to: '/', label: 'Inicio', icon: House }]} sidebarCards={cards}>
      <h1>{heading}</h1>
    </AppLayout>
  );
  const router = createMemoryRouter(
    [
      { path: '/', element: layout('Bienvenida') },
      { path: '/comparsas/:id', element: layout('Detalle de comparsa') },
    ],
    { initialEntries: [path] },
  );
  return renderWithProviders(<RouterProvider router={router} />);
}

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
}

const ORIGINAL_WIDTH = window.innerWidth;

describe('AppLayout sidebar cards (platform: Application shell)', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
    setViewportWidth(ORIGINAL_WIDTH);
  });

  it('shows the cards under the mark, in the given order, inside a named navigation', async () => {
    await renderLayout(CARDS);

    const cards = screen.getByRole('navigation', { name: 'Mis comparsas' });
    const links = within(cards).getAllByRole('link');
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/comparsas/1', '/comparsas/2']);
    expect(links[0]).toHaveAccessibleName('Comparsa Sintética Norte');
    // Under the mark and before the main navigation in reading order (both scroll together).
    const mark = screen.getByRole('link', { name: 'PolvorApp' });
    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(mark.compareDocumentPosition(cards)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(cards.compareDocumentPosition(navigation)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect(cards.closest('[data-slot="sidebar-content"]')).toBe(
      navigation.closest('[data-slot="sidebar-content"]'),
    );
  });

  it('opens the comparsa when a card is chosen', async () => {
    const user = userEvent.setup();
    await renderLayout(CARDS);

    await user.click(screen.getByRole('link', { name: 'Comparsa Sintética Norte' }));

    expect(await screen.findByRole('heading', { name: 'Detalle de comparsa' })).toBeInTheDocument();
  });

  it('shows a long name in full, wrapping instead of clipping it (1.4.10, 1.4.12)', async () => {
    await renderLayout(CARDS);

    const card = screen.getByRole('link', { name: LONG_NAME });
    expect(card).not.toHaveAttribute('title');
    expect(within(card).getByText(LONG_NAME)).not.toHaveClass('line-clamp-2');
  });

  it('marks the open comparsa like the current navigation item', async () => {
    await renderLayout(CARDS, '/comparsas/2');

    const cards = screen.getByRole('navigation', { name: 'Mis comparsas' });
    expect(within(cards).getByRole('link', { name: LONG_NAME })).toHaveAttribute('aria-current', 'page');
    expect(within(cards).getByRole('link', { name: 'Comparsa Sintética Norte' })).not.toHaveAttribute(
      'aria-current',
    );
  });

  it('renders no cards when there are none', async () => {
    await renderLayout([]);

    expect(screen.queryByRole('navigation', { name: 'Mis comparsas' })).not.toBeInTheDocument();
  });

  it('closes the navigation drawer after choosing a card on a small screen', async () => {
    setViewportWidth(360);
    const user = userEvent.setup();
    await renderLayout(CARDS);

    await user.click(screen.getByRole('button', { name: 'Mostrar u ocultar la navegación' }));
    const drawer = await screen.findByRole('dialog');
    await user.click(within(drawer).getByRole('link', { name: 'Comparsa Sintética Norte' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(await screen.findByRole('heading', { name: 'Detalle de comparsa' })).toBeInTheDocument();
  });

  // Structure and names only: jsdom has no CSS, contrast is checked by styles/contrast.test.ts.
  it('has no automatically detectable accessibility violations', async () => {
    const { container } = await renderLayout(CARDS, '/comparsas/1');

    expect(await axeViolations(container)).toEqual([]);
  });
});
