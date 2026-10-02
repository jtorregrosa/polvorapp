import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, OESTE, SUR } from '../test-data';

// Specs "Registering and editing arquebusiers", "Registry screens" and "Form fields and validation
// messages" (design-system). Synthetic data only.

function comparsas(list: ComparsaResponse[]) {
  server.use(mock.get('/api/comparsas', () => HttpResponse.json(list)));
}

/** jsdom does not drive date segments with userEvent: changes are dispatched directly. */
function setDate(label: RegExp | string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

const summary = () => screen.getByRole('group', { name: 'Hay un problema' });

async function fillRequired(user: UserEvent) {
  await user.type(screen.getByLabelText(/ID Unión/), '900001');
  await user.type(screen.getByLabelText(/^DNI\/NIE/), '12345678z');
  await user.type(screen.getByLabelText(/^Nombre/), 'Arcabucera');
  await user.type(screen.getByLabelText(/^Apellidos/), 'Sintética Nueva');
  setDate(/Fecha de nacimiento/, '1990-05-01');
  await user.click(screen.getByRole('radio', { name: 'Mujer' }));
}

async function chooseLicense(user: UserEvent, name: RegExp | string) {
  await user.click(screen.getByRole('radio', { name }));
}

describe('Registering an arquebusier (spec: Registering and editing, Registry screens)', () => {
  it('pre-selects the only active comparsa and offers only active comparsas in scope', async () => {
    comparsas([NORTE, OESTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    const comparsa = await screen.findByRole('combobox', { name: 'Comparsa' });
    await waitFor(() => {
      expect(comparsa).toHaveValue(NORTE.id);
    });
    expect(within(comparsa).queryByRole('option', { name: OESTE.name })).not.toBeInTheDocument();
  });

  it('marks only the optional fields, with "(opcional)", and says once that the rest are required', async () => {
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    expect(screen.getByRole('textbox', { name: 'Correo electrónico (opcional)' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Teléfono (opcional)' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Nombre' })).toBeRequired();
    expect(
      screen.getAllByText('Todos los campos son obligatorios salvo los marcados como opcionales.'),
    ).toHaveLength(1);
    expect(document.querySelector('main')?.textContent).not.toContain('*');
  });

  it('offers gender, status and license type as radio options, not selects', async () => {
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    for (const group of ['Género', 'Estado', 'Tipo de licencia']) {
      expect(screen.getByRole('radiogroup', { name: group })).toBeInTheDocument();
    }
    expect(within(screen.getByRole('radiogroup', { name: 'Género' })).getAllByRole('radio')).toHaveLength(3);
  });

  it('keeps fields at the width of what they expect', async () => {
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    const width = (label: RegExp) => screen.getByLabelText(label).closest('[data-slot="form-field-control"]');
    expect(width(/^DNI\/NIE/)).toHaveClass('max-w-field-id');
    expect(width(/^Nombre/)).toHaveClass('max-w-field-name');
    expect(width(/Fecha de nacimiento/)).toHaveClass('max-w-field-short');
    expect(width(/Correo electrónico/)).toHaveClass('max-w-field-long');
  });

  it('shows the license dates only under a license type, and only while it is not pending', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });
    expect(screen.queryByLabelText(/Fecha de expedición/)).not.toBeInTheDocument();

    await chooseLicense(user, /^AE/);
    expect(screen.getByLabelText(/Fecha de expedición/)).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /En trámite/ })).toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: /En trámite/ }));
    expect(screen.queryByLabelText(/Fecha de expedición/)).not.toBeInTheDocument();

    await chooseLicense(user, 'Sin licencia');
    expect(screen.queryByRole('checkbox', { name: /En trámite/ })).not.toBeInTheDocument();
  });

  it('flags a wrong check letter on submit, without calling the server', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    let requests = 0;
    server.use(
      mock.post('/api/arquebusiers', () => {
        requests += 1;
        return new HttpResponse(null, { status: 500 });
      }),
    );
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await user.type(screen.getByLabelText(/^DNI\/NIE/), '12345678A');
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    expect(await screen.findAllByText(/La letra no corresponde a los números\./)).not.toHaveLength(0);
    expect(requests).toBe(0);
  });

  it('lists the missing nationalId and birth date in a focused summary that links to them', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(summary()).toHaveFocus();
    });
    const links = within(summary())
      .getAllByRole('link')
      .map((link) => link.textContent);
    expect(links).toEqual(
      expect.arrayContaining([
        expect.stringMatching(/^DNI\/NIE: /),
        expect.stringMatching(/^Fecha de nacimiento: /),
      ]),
    );
    expect(screen.getByLabelText(/^DNI\/NIE/)).toHaveAttribute('aria-invalid', 'true');

    await user.click(within(summary()).getByRole('link', { name: /^Fecha de nacimiento: / }));
    expect(screen.getByLabelText(/Fecha de nacimiento/)).toHaveFocus();

    // A fixed field clears at once, without submitting again.
    await user.type(screen.getByLabelText(/^DNI\/NIE/), '12345678Z');
    expect(screen.getByLabelText(/^DNI\/NIE/)).toHaveAttribute('aria-invalid', 'false');
  });

  it('pre-fills the license expiry from the issue date and type until it is edited, and announces it', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await chooseLicense(user, /^AE/);
    setDate(/Fecha de expedición/, '2024-02-29');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2029-02-28');
    expect(screen.getByText('Fecha de caducidad calculada: 28/2/2029.')).toHaveAttribute('role', 'status');

    await chooseLicense(user, /^A-PROF/);
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2025-02-28');

    setDate(/Fecha de caducidad/, '2026-12-31');
    setDate(/Fecha de expedición/, '2024-03-10');
    expect(screen.getByLabelText(/Fecha de caducidad/)).toHaveValue('2026-12-31');
  });

  it('gives the dates back when a pending license is unticked, and says so', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });
    await chooseLicense(user, /^AE/);
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
    await screen.findByRole('combobox', { name: 'Comparsa' });
    await chooseLicense(user, /^AE/);
    setDate(/Fecha de caducidad/, '2026-12-31');
    await chooseLicense(user, 'Sin licencia');
    await chooseLicense(user, /^A-PROF/);
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
      expect(screen.getByRole('combobox', { name: 'Comparsa' })).toHaveAccessibleDescription(
        /Elige una opción/,
      );
    });
    await waitFor(() => {
      expect(summary()).toHaveFocus();
    });
    expect(screen.getByRole('link', { name: /^Comparsa: / })).toBeInTheDocument();
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
      expect(screen.getByRole('combobox', { name: 'Comparsa' })).toHaveValue(NORTE.id);
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

  it('puts the primary action last in the action bar, after "Cancel"', async () => {
    comparsas([NORTE]);
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    const bar = screen
      .getByRole('button', { name: 'Registrar arcabucero' })
      .closest('[data-slot="action-bar"]');
    const actions = [...(bar?.querySelectorAll('a, button') ?? [])].map((element) => element.textContent);
    expect(actions).toEqual(['Cancelar', 'Registrar arcabucero']);
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
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await fillRequired(user);
    await chooseLicense(user, /^AE/);
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
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    const nationalId = screen.getByLabelText(/^DNI\/NIE/);
    await waitFor(() => {
      expect(nationalId).toHaveAccessibleDescription(/contacta con la Federación/);
    });
    await waitFor(() => {
      expect(summary()).toHaveFocus();
    });
  });

  it('lists a refusal about no field in the summary', async () => {
    const user = userEvent.setup();
    comparsas([NORTE]);
    server.use(mock.post('/api/arquebusiers', () => problem(503, 'registry.busy')));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(summary()).toHaveFocus();
    });
    expect(within(summary()).queryByRole('link')).not.toBeInTheDocument();
  });

  it('tells an Admin to search the registry for a duplicate', async () => {
    const user = userEvent.setup();
    comparsas([NORTE, SUR]);
    server.use(mock.post('/api/arquebusiers', () => problem(409, 'arquebusiers.federationIdTaken')));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_ADMIN });
    await screen.findByRole('option', { name: SUR.name });
    await user.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), SUR.id);

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(screen.getByLabelText(/ID Unión/)).toHaveAccessibleDescription(/Búscalo en el registro/);
    });
  });

  it('has no accessibility violations', async () => {
    comparsas([NORTE]);
    const { container } = await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
