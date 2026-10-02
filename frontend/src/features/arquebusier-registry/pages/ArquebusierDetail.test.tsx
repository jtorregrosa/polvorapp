import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, SUR } from '../test-data';

// Specs "Registry screens" and "Detail pages in read mode": a read-only record whose sections are
// edited in side panels. Synthetic data only.

function detail(current: () => ArquebusierResponse = () => DETAIL_UNO) {
  server.use(
    mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(current())),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
    mock.get('/api/arquebusiers', () => HttpResponse.json([])),
  );
}

const section = (name: string) => screen.getByRole('region', { name });

async function editPersonal(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Editar datos personales' }));
  return screen.findByRole('dialog', { name: 'Editar datos personales' });
}

describe('Arquebusier detail in read mode (spec: Registry screens)', () => {
  it('shows a header with the comparsa and side, the name and the statuses, then the key facts', async () => {
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Arcabucero García Sintético' }),
    ).toBeInTheDocument();
    expect(await screen.findByText(`${NORTE.name} · Cristiano`)).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Foto de carnet' })).toBeInTheDocument();
    const facts = screen.getByRole('list', { name: 'Datos clave' });
    expect(within(facts).getByText('10/03/2030')).toBeInTheDocument();
    expect(within(facts).getByRole('meter', { name: /de la vigencia de la licencia/ })).toBeInTheDocument();
    expect(within(facts).getByText('00000001R')).toHaveClass('font-mono');
    expect(within(facts).getByText('2')).toBeInTheDocument();
    // The course state is in the header too (spec: Registry screens).
    expect(screen.getByRole('heading', { level: 1 }).closest('header')).toHaveTextContent('Curso hecho');
  });

  it('shows each section read-only, with "No consta" for what is not given', async () => {
    detail(() => ({ ...DETAIL_UNO, phone: null }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const personal = await screen.findByRole('region', { name: 'Datos personales' });
    expect(within(personal).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(personal).getByText('arcabucero.01@polvorapp.example')).toBeInTheDocument();
    expect(within(personal).getByText('No consta')).toBeInTheDocument();
    expect(within(section('Licencia')).getByText('AE (avancarga)')).toBeInTheDocument();
    expect(within(section('Curso de arcabucería')).getByText('Hecho el 15/11/2025')).toBeInTheDocument();
  });

  it('warns about an expired license and a missing course without blocking anything (BR-04)', async () => {
    detail(() => ({
      ...DETAIL_UNO,
      trainingCompletedOn: null,
      license: {
        type: 'AE',
        pending: false,
        issuedOn: '2020-03-10',
        expiresOn: '2025-03-10',
        status: 'EXPIRED',
      },
    }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const warning = await screen.findByText('Avisos de cumplimiento');
    const banner = warning.closest('[data-severity]');
    expect(banner).toHaveAttribute('data-severity', 'warning');
    expect(banner).toHaveTextContent('La licencia no está vigente.');
    expect(banner).toHaveTextContent('No consta el curso de arcabucería.');
    expect(screen.getByRole('button', { name: 'Editar datos personales' })).toBeEnabled();
  });

  it('says when the comparsa is inactive and keeps the sections editable', async () => {
    detail(() => ({ ...DETAIL_UNO, comparsaActive: false }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText(/está inactiva: no admite nuevos arcabuceros/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar datos personales' })).toBeEnabled();
  });
});

describe('Editing a section (spec: Detail pages in read mode)', () => {
  it('saves the section merged into the record with its version, announces it and returns focus to "Edit"', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    const { bodies, resolver } = recordBodies(() => {
      current = { ...DETAIL_UNO, phone: '+34 600 000 009', version: 8 };
      return HttpResponse.json(current);
    });
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await editPersonal(user);
    const phone = within(panel).getByRole('textbox', { name: 'Teléfono (opcional)' });
    expect(phone).toHaveValue('+34 600 000 001');
    await user.clear(phone);
    await user.type(phone, '+34 600 000 009');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(bodies[0]).toMatchObject({
      phone: '+34 600 000 009',
      version: 7,
      license: { type: 'AE', pending: false, issuedOn: '2025-03-10', expiresOn: '2030-03-10' },
      trainingCompletedOn: '2025-11-15',
    });
    expect(bodies[0]).not.toHaveProperty('comparsaId');
    expect(await within(section('Datos personales')).findByText('+34 600 000 009')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByText('Cambios guardados', { selector: '[role=status]' })).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Editar datos personales' })).toHaveFocus();
  });

  it('discards the changes with Escape and sends nothing', async () => {
    const user = userEvent.setup();
    detail();
    let requests = 0;
    server.use(
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        requests += 1;
        return HttpResponse.json(DETAIL_UNO);
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await editPersonal(user);
    await user.clear(within(panel).getByRole('textbox', { name: 'Teléfono (opcional)' }));
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(requests).toBe(0);
    expect(within(section('Datos personales')).getByText('+34 600 000 001')).toBeInTheDocument();
  });

  it('keeps the panel open with the error summary when the last name is emptied', async () => {
    const user = userEvent.setup();
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await editPersonal(user);
    await user.clear(within(panel).getByRole('textbox', { name: 'Apellidos' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(within(panel).getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });
    expect(within(panel).getByRole('textbox', { name: 'Apellidos' })).toHaveAttribute('aria-invalid', 'true');
  });

  it('is not blocked by an invalid value stored in another section', async () => {
    const user = userEvent.setup();
    // A stored course date in the future would fail the course rules, not the personal ones.
    detail(() => ({ ...DETAIL_UNO, trainingCompletedOn: '2999-01-01' }));
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(DETAIL_UNO));
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await editPersonal(user);
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
  });

  it('stays open with the reason and the current values when someone else changed the record', async () => {
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

    const panel = await editPersonal(user);
    await user.type(within(panel).getByRole('textbox', { name: 'Nombre' }), 'X');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    const summary = await within(panel).findByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveTextContent(/Otra persona ha cambiado este arcabucero/);
    await waitFor(() => {
      expect(within(panel).getByRole('textbox', { name: 'Teléfono (opcional)' })).toHaveValue(
        '+34 600 000 077',
      );
    });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('keeps what was typed and says the values may be outdated when the record cannot be reloaded after a conflict', async () => {
    const user = userEvent.setup();
    let failing = false;
    server.use(
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () =>
        failing ? problem(500, 'internal') : HttpResponse.json(DETAIL_UNO),
      ),
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        failing = true;
        return problem(409, 'arquebusiers.modified');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await editPersonal(user);
    const firstName = within(panel).getByRole('textbox', { name: 'Nombre' });
    await user.type(firstName, 'X');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    const summary = await within(panel).findByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveTextContent(/Otra persona ha cambiado este arcabucero.*puede no estar actualizado/);
    expect(firstName).toHaveValue(`${DETAIL_UNO.firstName}X`);
  });

  it('edits the license in its own panel, showing the dates under the chosen type', async () => {
    const user = userEvent.setup();
    detail();
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(DETAIL_UNO));
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Editar licencia' }));
    const panel = await screen.findByRole('dialog', { name: 'Editar licencia' });
    expect(within(panel).getByRole('radio', { name: /^AE/ })).toHaveAttribute('aria-checked', 'true');
    fireEvent.change(within(panel).getByLabelText(/Fecha de expedición/), {
      target: { value: '2026-01-15' },
    });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(bodies[0]).toMatchObject({
        license: { type: 'AE', issuedOn: '2026-01-15', expiresOn: '2031-01-15' },
        phone: DETAIL_UNO.phone,
      });
    });
  });

  it('offers the same sections to a FiringChief as to an Admin', async () => {
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    for (const name of ['Editar datos personales', 'Editar licencia', 'Editar curso']) {
      expect(await screen.findByRole('button', { name })).toBeInTheDocument();
    }
  });

  it('has no accessibility violations, read-only and with a panel open', async () => {
    const user = userEvent.setup();
    detail();
    const { container } = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('heading', { level: 1, name: 'Arcabucero García Sintético' });
    expect(await axeViolations(container)).toEqual([]);

    await editPersonal(user);
    expect(await axeViolations(document.body)).toEqual([]);
  });
});
