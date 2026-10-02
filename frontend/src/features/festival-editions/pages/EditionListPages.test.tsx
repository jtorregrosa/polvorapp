import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { CLOSED_2030, CURRENT_2031, DRAFT_2032, rowOf } from '../test-data';

const ORIGINAL_WIDTH = window.innerWidth;

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
}

function listing(...editions: (typeof CURRENT_2031)[]) {
  server.use(mock.get('/api/editions', () => HttpResponse.json(editions.map(rowOf))));
}

describe('EditionsPage (specs: Editions screens, Edition visibility (BR-12))', () => {
  afterEach(() => {
    setViewportWidth(ORIGINAL_WIDTH);
  });

  it('lists the editions with their festival dates, statuses and the current one marked', async () => {
    listing(DRAFT_2032, CURRENT_2031, CLOSED_2030);
    await renderApp('/editions', { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Ediciones' });
    const current = (await within(table).findByRole('link', { name: 'Fiestas 2031' })).closest('tr');
    expect(current).toHaveTextContent('Del 22 de abril de 2031 al 25 de abril de 2031');
    expect(current).toHaveTextContent('Edición actual');
    expect(current).toHaveTextContent('En curso');
    expect(current).toHaveTextContent('Pedidos abiertos');
    expect(within(table).getByRole('link', { name: 'Fiestas 2032' }).closest('tr')).toHaveTextContent(
      'En preparación',
    );
    expect(within(table).getByRole('link', { name: 'Fiestas 2030' }).closest('tr')).toHaveTextContent(
      'Cerrada',
    );
    expect(screen.getByRole('link', { name: 'Nueva edición' })).toHaveAttribute('href', '/editions/new');
  });

  it('offers no new edition to a FiringChief', async () => {
    listing(CURRENT_2031);
    await renderApp('/editions', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('link', { name: 'Fiestas 2031' });
    expect(screen.queryByRole('link', { name: 'Nueva edición' })).not.toBeInTheDocument();
  });

  it('invites an Admin to create the first edition', async () => {
    listing();
    await renderApp('/editions', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('heading', { name: 'Aún no hay ediciones' })).toBeInTheDocument();
    expect(
      screen.getByText('Crea la primera edición para preparar los pedidos de las fiestas.'),
    ).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Nueva edición' })).toHaveLength(2);
  });

  it('tells a FiringChief that the Federation has not opened any edition', async () => {
    listing();
    await renderApp('/editions', { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByText('La Federación todavía no ha abierto ninguna edición.'),
    ).toBeInTheDocument();
  });

  it('says the list could not be loaded and retries', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      mock.get('/api/editions', () => {
        calls += 1;
        return calls === 1 ? problem(500, 'unexpected') : HttpResponse.json([rowOf(CURRENT_2031)]);
      }),
    );
    await renderApp('/editions', { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('link', { name: 'Fiestas 2031' })).toBeInTheDocument();
  });

  it('stacks the editions on a phone', async () => {
    setViewportWidth(360);
    listing(CURRENT_2031);
    await renderApp('/editions', { session: SYNTHETIC_ADMIN });

    const link = await screen.findByRole('link', { name: 'Fiestas 2031' });
    expect(link.closest('table')).toBeNull();
  });

  it('has no accessibility violations', async () => {
    listing(CURRENT_2031, CLOSED_2030);
    const { container } = await renderApp('/editions', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('link', { name: 'Fiestas 2031' });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});

describe('EditionCreatePage (specs: Editions screens, New editions start from the previous one)', () => {
  it('says which edition will be copied, creates the draft and opens it', async () => {
    const user = userEvent.setup();
    listing(CURRENT_2031, CLOSED_2030);
    const created = { ...DRAFT_2032, year: 2032 };
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(created, { status: 201 }));
    server.use(
      mock.post('/api/editions', resolver),
      mock.get(`/api/editions/${created.id}`, () => HttpResponse.json(created)),
    );
    const app = await renderApp('/editions/new', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByText(/Se copiarán los precios de la edición 2031/)).toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Año' }), '2031');
    expect(await screen.findByText(/Se copiarán los precios de la edición 2030/)).toBeInTheDocument();
    await user.clear(screen.getByRole('textbox', { name: 'Año' }));
    await user.type(screen.getByRole('textbox', { name: 'Año' }), '2032');
    await setDate('Primer día de fiestas', '2032-04-22');
    await setDate('Último día de fiestas', '2032-04-25');
    await user.click(screen.getByRole('button', { name: 'Crear edición' }));

    await waitFor(() => {
      expect(app.location()).toBe(`/editions/${created.id}`);
    });
    expect(bodies).toEqual([{ year: 2032, festivalStartsOn: '2032-04-22', festivalEndsOn: '2032-04-25' }]);
    expect(await screen.findByText('Edición 2032 creada en preparación.')).toBeInTheDocument();
  });

  it('says nothing will be copied when there is no earlier edition', async () => {
    listing();
    await renderApp('/editions/new', { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText(
        'No hay ninguna edición anterior: los precios y los modelos de alquiler empiezan vacíos.',
      ),
    ).toBeInTheDocument();
  });

  it('checks the year and the festival dates before sending', async () => {
    const user = userEvent.setup();
    listing();
    await renderApp('/editions/new', { session: SYNTHETIC_ADMIN });

    await user.type(await screen.findByRole('textbox', { name: 'Año' }), '1999');
    await user.click(screen.getByRole('button', { name: 'Crear edición' }));

    expect(
      await screen.findByRole('textbox', { name: 'Año', description: /Un año entre 2000 y 2100/ }),
    ).toBeInTheDocument();
    expect(screen.getAllByText('Este campo es obligatorio').length).toBeGreaterThan(0);
  });

  it('puts a taken year on the year field', async () => {
    const user = userEvent.setup();
    listing(CURRENT_2031);
    server.use(mock.post('/api/editions', () => problem(409, 'editions.yearTaken')));
    await renderApp('/editions/new', { session: SYNTHETIC_ADMIN });

    await user.type(await screen.findByRole('textbox', { name: 'Año' }), '2031');
    await setDate('Primer día de fiestas', '2031-04-22');
    await setDate('Último día de fiestas', '2031-04-25');
    await user.click(screen.getByRole('button', { name: 'Crear edición' }));

    expect(
      await screen.findByRole('textbox', { name: 'Año', description: /Ya existe una edición de ese año/ }),
    ).toBeInTheDocument();
  });

  it('is not shown to a FiringChief', async () => {
    await renderApp('/editions/new', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
  });
});

/** jsdom does not drive date segments with userEvent: the change is dispatched on the input. */
async function setDate(label: string, value: string) {
  const input = await screen.findByLabelText(label);
  fireEvent.change(input, { target: { value } });
}
