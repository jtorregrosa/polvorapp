import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, OESTE, SUR } from '../test-data';

function comparsas(list: ComparsaResponse[]) {
  server.use(mock.get('/api/comparsas', () => HttpResponse.json(list)));
}

/** jsdom does not drive date segments with userEvent: changes are dispatched directly. */
function setDate(label: RegExp | string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

async function fillRequired(user: UserEvent) {
  await user.type(screen.getByLabelText(/ID Unión/), '900001');
  await user.type(screen.getByLabelText(/^DNI\/NIE/), '12345678z');
  await user.type(screen.getByLabelText(/^Nombre/), 'Arcabucera');
  await user.type(screen.getByLabelText(/^Apellidos/), 'Sintética Nueva');
  setDate(/Fecha de nacimiento/, '1990-05-01');
  await user.selectOptions(screen.getByLabelText(/Género/), 'FEMALE');
}

describe('Registering an arquebusier (spec: Registering and editing, Registry screens)', () => {
  it('pre-selects the only active comparsa and offers only active comparsas in scope', async () => {
    comparsas([NORTE, OESTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    const comparsa = await screen.findByLabelText(/^Comparsa/);
    await waitFor(() => {
      expect(comparsa).toHaveValue(NORTE.id);
    });
    expect(within(comparsa).queryByRole('option', { name: OESTE.name })).not.toBeInTheDocument();
  });

  it('flags a wrong check letter when the DNI/NIE field is left, without calling the server', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await user.type(await screen.findByLabelText(/^DNI\/NIE/), '12345678A');
    await user.tab();

    expect(await screen.findByText('La letra no corresponde a los números.')).toBeInTheDocument();
  });

  it('pre-fills the license expiry from the issue date and type until it is edited, and announces it', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await user.selectOptions(await screen.findByLabelText(/Tipo de licencia/), 'AE');
    setDate(/Fecha de expedición/, '2024-02-29');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2029-02-28');
    expect(screen.getByText('Fecha de caducidad calculada: 28/2/2029.')).toHaveAttribute('role', 'status');

    await user.selectOptions(screen.getByLabelText(/Tipo de licencia/), 'A_PROF');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2025-02-28');

    setDate(/Fecha de caducidad/, '2026-12-31');
    setDate(/Fecha de expedición/, '2024-03-10');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2026-12-31');
  });

  it('disables and clears the dates of a pending license', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await user.selectOptions(await screen.findByLabelText(/Tipo de licencia/), 'AE');
    setDate(/Fecha de expedición/, '2024-03-10');

    await user.click(screen.getByRole('checkbox', { name: /En trámite/ }));

    expect(screen.getByLabelText(/Fecha de expedición/)).toBeDisabled();
    expect(screen.getByLabelText(/Fecha de expedición/)).toHaveValue('');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toBeDisabled();
  });

  it('gives the dates back when a pending license is unticked, and says so', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await user.selectOptions(await screen.findByLabelText(/Tipo de licencia/), 'AE');
    setDate(/Fecha de expedición/, '2024-03-10');
    const pending = screen.getByRole('checkbox', { name: /En trámite/ });

    await user.click(pending);
    expect(screen.getByText('Fechas de la licencia vaciadas.')).toHaveAttribute('role', 'status');
    await user.click(pending);

    expect(screen.getByText('Fechas de la licencia recuperadas.')).toHaveAttribute('role', 'status');
    expect(screen.getByLabelText(/Fecha de expedición/)).toHaveValue('2024-03-10');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2029-03-10');
  });

  it('fills the default expiry again after the dates were cleared, even if one had been typed', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    const type = await screen.findByLabelText(/Tipo de licencia/);
    await user.selectOptions(type, 'AE');
    setDate(/Fecha de caducidad/, '2026-12-31');
    await user.selectOptions(type, '');
    await user.selectOptions(type, 'A_PROF');
    setDate(/Fecha de caducidad/, '');

    setDate(/Fecha de expedición/, '2025-05-01');

    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2026-05-01');
  });

  it('requires choosing the comparsa when there are several', async () => {
    const user = userEvent.setup();
    comparsas([NORTE, SUR]);
    let requests = 0;
    server.use(
      mock.post('/api/arquebusiers', () => {
        requests += 1;
        return new HttpResponse(null, { status: 500 });
      }),
    );
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('option', { name: SUR.name });
    await fillRequired(user);

    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(screen.getByLabelText(/^Comparsa/)).toHaveAccessibleDescription(/Elige una opción/);
    });
    expect(screen.getByLabelText(/^Comparsa/)).toHaveFocus();
    expect(requests).toBe(0);
  });

  it('does not offer to register while the comparsas cannot be loaded, and retries', async () => {
    const user = userEvent.setup();
    let fail = true;
    server.use(
      mock.get('/api/comparsas', () =>
        fail ? new HttpResponse(null, { status: 500 }) : HttpResponse.json([NORTE]),
      ),
    );
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText(/Sin la lista de comparsas no se puede registrar/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar arcabucero' })).toBeDisabled();
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    await waitFor(() => {
      expect(screen.getByLabelText(/^Comparsa/)).toHaveValue(NORTE.id);
    });
    expect(screen.getByRole('button', { name: 'Registrar arcabucero' })).toBeEnabled();
  });

  it('explains that there is no active comparsa to register in', async () => {
    comparsas([]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText('No hay ninguna comparsa activa en la que puedas registrar arcabuceros.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar arcabucero' })).toBeDisabled();
  });

  it('registers with the normalised request and goes to the new arquebusier', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(DETAIL_UNO, { status: 201 }));
    server.use(
      mock.post('/api/arquebusiers', resolver),
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(DETAIL_UNO)),
    );
    const app = await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByLabelText(/^Comparsa/);

    await fillRequired(user);
    await user.selectOptions(screen.getByLabelText(/Tipo de licencia/), 'AE');
    setDate(/Fecha de expedición/, '2024-03-10');
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(app.location()).toBe(`/arquebusiers/${DETAIL_UNO.id}`);
    });
    expect(bodies).toEqual([
      {
        comparsaId: NORTE.id,
        federationId: 900001,
        nationalId: '12345678z',
        firstName: 'Arcabucera',
        lastName: 'Sintética Nueva',
        birthDate: '1990-05-01',
        email: null,
        phone: null,
        gender: 'FEMALE',
        status: 'ACTIVE',
        trainingCompletedOn: null,
        license: { type: 'AE', pending: false, issuedOn: '2024-03-10', expiresOn: '2029-03-10' },
      },
    ]);
    expect(await screen.findByText(/registrado/)).toBeInTheDocument();
  });

  it("puts the server's field errors and duplicate conflicts on their fields", async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    server.use(mock.post('/api/arquebusiers', () => problem(409, 'arquebusiers.nationalIdTaken')));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByLabelText(/^Comparsa/);

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    const nationalId = screen.getByLabelText(/^DNI\/NIE/);
    await waitFor(() => {
      expect(nationalId).toHaveAccessibleDescription(/contacta con la Federación/);
    });
    expect(nationalId).toHaveFocus();
  });

  it('tells an Admin to search the registry for a duplicate', async () => {
    const user = userEvent.setup();
    comparsas([NORTE, SUR]);
    server.use(mock.post('/api/arquebusiers', () => problem(409, 'arquebusiers.federationIdTaken')));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('option', { name: SUR.name });
    await user.selectOptions(screen.getByLabelText(/^Comparsa/), SUR.id);

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(screen.getByLabelText(/ID Unión/)).toHaveAccessibleDescription(/Búscalo en el registro/);
    });
  });

  it('has no accessibility violations', async () => {
    comparsas([NORTE]);
    const { container } = await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByLabelText(/^Comparsa/);

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Editing an arquebusier (spec: Registering and editing)', () => {
  function detail(current: () => ArquebusierResponse) {
    server.use(
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(current())),
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
    );
  }

  it('shows the current values and saves every field with the version', async () => {
    const user = userEvent.setup();
    detail(() => DETAIL_UNO);
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json({ ...DETAIL_UNO, phone: '+34 600 000 009', version: 8 }),
    );
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const phone = await screen.findByLabelText(/Teléfono/);
    await waitFor(() => {
      expect(phone).toHaveValue('+34 600 000 001');
    });
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2030-03-10');
    await user.clear(phone);
    await user.type(phone, '+34 600 000 009');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await screen.findByText('Cambios guardados.');
    expect(bodies[0]).toMatchObject({
      phone: '+34 600 000 009',
      version: 7,
      license: { type: 'AE', pending: false, issuedOn: '2025-03-10', expiresOn: '2030-03-10' },
    });
    expect(bodies[0]).not.toHaveProperty('comparsaId');
  });

  it('reloads and explains when someone else changed the arquebusier meanwhile', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    server.use(
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        current = { ...DETAIL_UNO, phone: '+34 600 000 077', version: 9 };
        return problem(409, 'arquebusiers.modified');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });
    const phone = await screen.findByLabelText(/Teléfono/);
    await waitFor(() => {
      expect(phone).toHaveValue('+34 600 000 001');
    });

    await user.type(screen.getByLabelText(/^Nombre/), 'X');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText(/Otra persona ha cambiado este arcabucero/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByLabelText(/Teléfono/)).toHaveValue('+34 600 000 077');
    });
  });

  it('keeps the page and what was typed when refreshing after a save fails', async () => {
    const user = userEvent.setup();
    let failing = false;
    server.use(
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () =>
        failing ? problem(503, 'registry.busy') : HttpResponse.json(DETAIL_UNO),
      ),
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        failing = true;
        return HttpResponse.json(DETAIL_UNO);
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });
    const phone = await screen.findByLabelText(/Teléfono/);
    await waitFor(() => {
      expect(phone).toHaveValue('+34 600 000 001');
    });

    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText(/Lo que ves puede no estar actualizado/)).toBeInTheDocument();
    expect(screen.getByText('Cambios guardados.')).toBeInTheDocument();
    expect(screen.getByLabelText(/Teléfono/)).toHaveValue('+34 600 000 001');
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('shows the not-found page when the arquebusier was deleted before saving', async () => {
    const user = userEvent.setup();
    let deleted = false;
    server.use(
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () =>
        deleted ? problem(404, 'arquebusiers.notFound') : HttpResponse.json(DETAIL_UNO),
      ),
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        deleted = true;
        return problem(404, 'arquebusiers.notFound');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Guardar cambios' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });
});
