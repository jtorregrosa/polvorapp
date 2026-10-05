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
    <AppLayout
      navigation={[{ id: 'home', items: [{ to: '/', label: 'Inicio', icon: House }] }]}
      sidebarCards={cards}
    >
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

describe('AppLayout navigation counts (compliance-insights: Warning count in the navigation)', () => {
  function renderWithCount(count: number) {
    const router = createMemoryRouter(
      [
        {
          path: '/',
          element: (
            <AppLayout
              navigation={[
                {
                  id: 'main',
                  items: [
                    { to: '/', label: 'Inicio', icon: House },
                    {
                      to: '/arquebusiers',
                      label: 'Arcabuceros',
                      icon: House,
                      count,
                      countLabel: `${count} con avisos`,
                    },
                  ],
                },
              ]}
            >
              <h1>Bienvenida</h1>
            </AppLayout>
          ),
        },
      ],
      { initialEntries: ['/'] },
    );
    return renderWithProviders(<RouterProvider router={router} />);
  }

  it('makes the count part of the link name and hides the badge from assistive technology', async () => {
    await renderWithCount(5);

    const link = await screen.findByRole('link', { name: 'Arcabuceros, 5 con avisos' });
    expect(link).toBeInTheDocument();
    const badge = screen.getByText('5', { selector: '[data-sidebar="menu-badge"]' });
    expect(badge).toHaveAttribute('aria-hidden', 'true');
  });

  it('shows no badge and no count in the name for zero', async () => {
    await renderWithCount(0);

    expect(await screen.findByRole('link', { name: 'Arcabuceros' })).toBeInTheDocument();
    expect(document.querySelector('[data-sidebar="menu-badge"]')).toBeNull();
  });
});

describe('AppLayout current navigation item (platform: Application shell)', () => {
  it("marks the entry that owns an edition's sub-page, not Editions", async () => {
    const page = (
      <AppLayout
        navigation={[
          {
            id: 'festival',
            label: 'Fiestas',
            items: [
              { to: '/editions', label: 'Ediciones', icon: House },
              { to: '/distribution', label: 'Reparto', icon: House, matches: ['/editions/*/distribution'] },
            ],
          },
        ]}
      >
        <h1>Reparto de 2027</h1>
      </AppLayout>
    );
    const router = createMemoryRouter([{ path: '/editions/:id/distribution', element: page }], {
      initialEntries: ['/editions/7/distribution'],
    });
    await renderWithProviders(<RouterProvider router={router} />);

    expect(await screen.findByRole('link', { name: 'Reparto' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Ediciones' })).not.toHaveAttribute('aria-current');
  });
});

describe('AppLayout navigation sections (platform: Application shell)', () => {
  const SECTIONS = [
    { id: 'home', items: [{ to: '/', label: 'Inicio', icon: House }] },
    {
      id: 'registry',
      label: 'Registro',
      items: [
        { to: '/arquebusiers', label: 'Arcabuceros', icon: House },
        { to: '/comparsas', label: 'Comparsas', icon: House },
      ],
    },
    { id: 'festival', label: 'Fiestas', items: [{ to: '/editions', label: 'Ediciones', icon: House }] },
  ];

  async function renderSections() {
    const router = createMemoryRouter(
      [
        {
          path: '/',
          element: (
            <AppLayout navigation={SECTIONS}>
              <h1>Bienvenida</h1>
            </AppLayout>
          ),
        },
      ],
      { initialEntries: ['/'] },
    );
    return renderWithProviders(<RouterProvider router={router} />);
  }

  it('shows each labelled section as a named group with its entries, in order', async () => {
    await renderSections();

    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    const groups = within(navigation).getAllByRole('group');
    expect(
      groups.map((group) =>
        within(group)
          .getAllByRole('link')
          .map((link) => link.textContent),
      ),
    ).toEqual([['Arcabuceros', 'Comparsas'], ['Ediciones']]);
    expect(within(navigation).getByRole('group', { name: 'Registro' })).toBeInTheDocument();
    expect(within(navigation).getByRole('heading', { level: 2, name: 'Fiestas' })).toBeInTheDocument();
  });

  it('shows the unlabelled first section without a group and separates the sections', async () => {
    await renderSections();

    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    const home = within(navigation).getByRole('link', { name: 'Inicio' });
    expect(home.closest('[role="group"]')).toBeNull();
    expect(navigation.querySelectorAll('[data-sidebar="separator"]')).toHaveLength(2);
  });

  it('has no automatically detectable accessibility violations with sections', async () => {
    const { container } = await renderSections();

    expect(await axeViolations(container)).toEqual([]);
  });
});
