import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PersonLookupResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';

// Spec audit-privacy "GDPR request screens" and "Looking up / Exporting / Erasing a person's data"
// (UC-26; design D12). Synthetic data only: 00000000T is a valid but made-up DNI.
const DNI = '00000000T';
const NORTE = 'Comparsa Sintética Norte';

const FOUND: PersonLookupResponse = {
  found: true,
  registry: {
    arquebusierId: '00000000-0000-4000-8000-000000000301',
    firstName: 'Arcabucera',
    lastName: 'Sintética Borrable',
    comparsaName: NORTE,
    status: 'ACTIVE',
    ownedWeapons: 1,
    photos: 2,
  },
  entries: [
    {
      editionYear: 2031,
      editionStatus: 'IN_PROGRESS',
      ordersOpen: false,
      comparsaName: NORTE,
      orderStatus: 'VALIDATED',
      erased: false,
    },
    {
      editionYear: 2029,
      editionStatus: 'CLOSED',
      ordersOpen: false,
      comparsaName: NORTE,
      orderStatus: 'VALIDATED',
      erased: false,
    },
  ],
  lenderLoans: [{ editionYear: 2029, loans: 2 }],
  pickupProxies: 1,
  warnings: [
    { kind: 'listsChange', editionYear: 2031, comparsaName: NORTE, orderStatus: 'VALIDATED' },
    { kind: 'entryRemoved', editionYear: 2032, comparsaName: 'Comparsa Sintética Sur', orderStatus: 'DRAFT' },
  ],
};

const NOTHING: PersonLookupResponse = {
  found: false,
  registry: null,
  entries: [],
  lenderLoans: [],
  pickupProxies: 0,
  warnings: [],
};

let saved: string | undefined;

beforeEach(() => {
  saved = undefined;
  // jsdom has no object URLs: a stand-in URL class, undone after each test.
  vi.stubGlobal(
    'URL',
    class extends URL {
      static override createObjectURL = vi.fn(() => 'blob:personal-data');
      static override revokeObjectURL = vi.fn();
    },
  );
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved = this.download;
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  document.documentElement.classList.remove('dark');
});

/** Answers lookups with each response in turn (the last one again after that). */
function lookups(...responses: PersonLookupResponse[]) {
  const recorded = recordBodies(() => HttpResponse.json(responses.shift() ?? NOTHING));
  server.use(mock.post('/api/privacy/people/lookup', recorded.resolver));
  return recorded.bodies;
}

async function lookUp(user: UserEvent, dni = DNI) {
  await user.type(await screen.findByRole('textbox', { name: 'DNI/NIE' }), dni);
  await user.click(screen.getByRole('button', { name: 'Buscar' }));
}

const asAdmin = () => renderApp('/privacy', { session: SYNTHETIC_ADMIN });

describe('PrivacyRequestsPage (spec: GDPR request screens)', () => {
  it('looks a person up by DNI/NIE in the request body, never in the address, and shows what is held', async () => {
    const user = userEvent.setup();
    const bodies = lookups(FOUND);
    const app = await asAdmin();

    await lookUp(user);

    const summary = await screen.findByRole('region', { name: 'Datos que guarda PolvorApp' });
    expect(bodies).toEqual([{ nationalId: DNI }]);
    expect(app.location()).toBe('/privacy');
    expect(summary).toHaveTextContent('Arcabucera Sintética Borrable');
    expect(summary).toHaveTextContent('2029: 2 cesiones');
    expect(summary).toHaveTextContent('1 autorización');
    const entries = within(summary).getByRole('table', {
      name: 'Líneas de pedido de la persona, por edición',
    });
    expect(within(entries).getAllByRole('row')).toHaveLength(3);
    expect(within(entries).getByText('2031')).toBeInTheDocument();
  });

  it('moves focus to the result, forgets it when the DNI/NIE changes, and back to the field on "New lookup"', async () => {
    const user = userEvent.setup();
    lookups(FOUND, FOUND);
    await asAdmin();

    await lookUp(user);
    const summary = await screen.findByRole('region', { name: 'Datos que guarda PolvorApp' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });

    await user.click(screen.getByRole('button', { name: 'Nueva búsqueda' }));
    const field = screen.getByRole('textbox', { name: 'DNI/NIE' });
    expect(field).toHaveFocus();
    expect(field).toHaveValue('');

    await lookUp(user);
    await screen.findByRole('region', { name: 'Datos que guarda PolvorApp' });
    await user.type(screen.getByRole('textbox', { name: 'DNI/NIE' }), 'X');
    expect(screen.queryByRole('region', { name: 'Datos que guarda PolvorApp' })).not.toBeInTheDocument();
  });

  it('says when nothing is held, and checks the DNI/NIE before asking', async () => {
    const user = userEvent.setup();
    const bodies = lookups(NOTHING);
    await asAdmin();

    await lookUp(user, '00000000A');
    expect(await screen.findByText('La letra no corresponde al número.')).toBeInTheDocument();
    expect(bodies).toEqual([]);

    await user.clear(screen.getByRole('textbox', { name: 'DNI/NIE' }));
    await lookUp(user);
    expect(await screen.findByText('PolvorApp no guarda ningún dato de esta persona.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Borrar datos' })).not.toBeInTheDocument();
  });

  it('downloads the data with a reference, explaining what not to type in it', async () => {
    const user = userEvent.setup();
    lookups(FOUND);
    const exported = recordBodies(
      () =>
        new HttpResponse(new Uint8Array([0x50, 0x4b]), {
          headers: { 'Content-Disposition': 'attachment; filename=polvorapp-personal-data-20310714.zip' },
        }),
    );
    server.use(mock.post('/api/privacy/people/export', exported.resolver));
    await asAdmin();
    await lookUp(user);

    await user.click(await screen.findByRole('button', { name: 'Descargar datos' }));
    const dialog = await screen.findByRole('alertdialog', { name: 'Descargar los datos personales' });
    const reference = within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' });
    expect(reference).toHaveAccessibleDescription(/No escribas el nombre ni el DNI\/NIE/);
    await user.click(within(dialog).getByRole('button', { name: 'Descargar' }));
    expect(await within(dialog).findByText('Escribe la referencia de la solicitud.')).toBeInTheDocument();
    expect(exported.bodies).toEqual([]);

    await user.type(reference, 'REQ-2031-07');
    await user.click(within(dialog).getByRole('button', { name: 'Descargar' }));

    await waitFor(() => {
      expect(saved).toBe('polvorapp-personal-data-20310714.zip');
    });
    expect(exported.bodies).toEqual([{ nationalId: DNI, reference: 'REQ-2031-07' }]);
    expect(await screen.findByText('Se ha descargado el archivo.')).toBeInTheDocument();
  });

  it('erases after a confirmation that names the person, lists each warning and starts on Cancel', async () => {
    const user = userEvent.setup();
    lookups(FOUND, NOTHING);
    const erased = recordBodies(() =>
      HttpResponse.json({
        counts: { arquebusiersDeleted: 1, entriesAnonymised: 2, pickupProxiesRemoved: 0 },
        filesPending: 0,
      }),
    );
    server.use(mock.post('/api/privacy/people/erasure', erased.resolver));
    await asAdmin();
    await lookUp(user);

    await user.click(await screen.findByRole('button', { name: 'Borrar datos' }));
    const dialog = await screen.findByRole('alertdialog', {
      name: '¿Borrar los datos de Arcabucera Sintética Borrable?',
    });
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toHaveFocus();
    expect(dialog).toHaveTextContent('No se puede deshacer.');
    expect(dialog).toHaveTextContent(
      `Las listas de 2031 de ${NORTE} (pedido Validado) dejarán de nombrar a esta persona.`,
    );
    expect(dialog).toHaveTextContent('Su línea del pedido de 2032 de Comparsa Sintética Sur se quitará');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' }),
      'REQ-2031-08',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));

    const outcome = await screen.findByText(/Datos borrados\./);
    expect(outcome).toHaveTextContent('1 ficha del registro eliminada');
    expect(outcome).toHaveTextContent('2 líneas de pedido anonimizadas');
    expect(outcome).not.toHaveTextContent('autorizaciones');
    expect(erased.bodies).toEqual([{ nationalId: DNI, reference: 'REQ-2031-08' }]);
    expect(screen.queryByRole('region', { name: 'Datos que guarda PolvorApp' })).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'DNI/NIE' })).toHaveValue('');
  });

  it('cancelling the erasure changes nothing', async () => {
    const user = userEvent.setup();
    lookups(FOUND);
    let erasures = 0;
    server.use(
      mock.post('/api/privacy/people/erasure', () => {
        erasures += 1;
        return HttpResponse.json({ counts: {}, filesPending: 0 });
      }),
    );
    await asAdmin();
    await lookUp(user);

    await user.click(await screen.findByRole('button', { name: 'Borrar datos' }));
    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(erasures).toBe(0);
    expect(screen.getByRole('region', { name: 'Datos que guarda PolvorApp' })).toBeInTheDocument();
  });

  it('says when the person was erased meanwhile and when there were too many lookups', async () => {
    const user = userEvent.setup();
    lookups(FOUND);
    server.use(mock.post('/api/privacy/people/erasure', () => problem(404, 'privacy.notFound')));
    await asAdmin();
    await lookUp(user);
    await user.click(await screen.findByRole('button', { name: 'Borrar datos' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' }),
      'REQ-2031-12',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    expect(
      await within(dialog).findByText('PolvorApp no guarda ningún dato de esta persona.'),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    server.use(mock.post('/api/privacy/people/lookup', () => problem(429, 'tooManyRequests')));
    await user.click(screen.getByRole('button', { name: 'Buscar' }));
    expect(
      await screen.findByText('Demasiadas solicitudes. Espera un minuto y vuelve a intentarlo.'),
    ).toBeInTheDocument();
  });

  it('says which stored files are still to be erased', async () => {
    const user = userEvent.setup();
    lookups(FOUND, NOTHING);
    server.use(
      mock.post('/api/privacy/people/erasure', () =>
        HttpResponse.json({ counts: { arquebusiersDeleted: 1, photosDeleted: 2 }, filesPending: 2 }),
      ),
    );
    await asAdmin();
    await lookUp(user);
    await user.click(await screen.findByRole('button', { name: 'Borrar datos' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' }),
      'REQ-2031-13',
    );
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));

    expect(
      await screen.findByText(
        /Quedan 2 archivos por borrar del almacenamiento: se borrarán automáticamente\./,
      ),
    ).toBeInTheDocument();
  });

  it('shows translated refusals: a reference with a DNI on its field, a busy erasure in the dialog', async () => {
    const user = userEvent.setup();
    lookups(FOUND);
    let answer = problem(400, 'validation', { errors: { reference: 'invalid' } });
    server.use(mock.post('/api/privacy/people/erasure', () => answer));
    await asAdmin();
    await lookUp(user);
    await user.click(await screen.findByRole('button', { name: 'Borrar datos' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Referencia de la solicitud' }),
      'Solicitud 0000 0000 T',
    );

    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    expect(await within(dialog).findByText('Usa una línea, sin correos ni DNI/NIE.')).toBeInTheDocument();

    answer = problem(503, 'privacy.busy');
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    expect(
      await within(dialog).findByText(
        'Otra persona está cambiando estos datos. Vuelve a intentarlo en unos segundos.',
      ),
    ).toBeInTheDocument();
  });

  it.each(['light', 'dark'])(
    'has no accessibility violations in the %s theme, with the result shown',
    async (theme) => {
      document.documentElement.classList.toggle('dark', theme === 'dark');
      const user = userEvent.setup();
      lookups(FOUND);
      const { container } = await asAdmin();
      await lookUp(user);
      await screen.findByRole('region', { name: 'Datos que guarda PolvorApp' });

      expect(await axeViolations(container)).toEqual([]);
    },
  );

  it('is not allowed for a FiringChief and is in the Admin navigation', async () => {
    const chief = await renderApp('/privacy', { session: SYNTHETIC_FIRING_CHIEF });
    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
    chief.unmount();

    await asAdmin();
    const navigation = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Privacidad' })).toHaveAttribute('href', '/privacy');
  });
});
