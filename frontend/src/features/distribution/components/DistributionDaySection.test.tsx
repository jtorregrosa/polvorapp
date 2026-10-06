import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { DistributionPlanResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { ADMIN_PLAN, ESTE, FIRING_CHIEF_PLAN, POWDER_DAY } from '../test-data';

const PAGE = `/editions/${ADMIN_PLAN.editionId}/distribution`;

/** The plan API, answering `plan` until a test changes it. */
function distribution(initial: DistributionPlanResponse = ADMIN_PLAN) {
  let plan = initial;
  server.use(
    mock.get(`/api/distribution/editions/${initial.editionId}`, () => HttpResponse.json(plan)),
    mock.get(`/api/distribution/editions/${initial.editionId}/proxies`, () => HttpResponse.json([])),
  );
  return {
    set: (next: DistributionPlanResponse) => {
      plan = next;
    },
  };
}

async function daySection(name: string): Promise<HTMLElement> {
  return screen.findByRole('region', { name }, { timeout: 5000 });
}

const powder = () => daySection('Día de reparto de pólvora');
const weapons = () => daySection('Día de reparto de armas');

describe('Distribution days on the distribution page (spec: Distribution screens)', () => {
  it('shows an Admin a planned day with its facts, its slots in time order and the comparsas without one', async () => {
    distribution();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    const section = await powder();
    const facts = within(section).getByRole('list', { name: 'Datos del día de reparto de pólvora' });
    expect(facts).toHaveTextContent('18 de abril de 2031');
    expect(facts).toHaveTextContent('Paraje Sintético del Reparto');
    const table = within(section).getByRole('table', { name: 'Turnos del día de reparto de pólvora' });
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows.map((row) => row.textContent)).toEqual([`09:00${NORTE.name}`, `09:30${SUR.name}`]);
    // The comparsas without a slot, folded under their count (UI audit).
    const without = within(section).getByText('1 comparsa sin turno');
    expect(without.closest('details')).toHaveTextContent(ESTE.name);
  });

  it('says a day is not planned and offers an Admin to plan it, without a list', async () => {
    distribution({ ...ADMIN_PLAN, days: [] });
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    for (const section of [await powder(), await weapons()]) {
      expect(within(section).getByText('Sin planificar.')).toBeInTheDocument();
      expect(within(section).queryByRole('button', { name: /Descargar/ })).not.toBeInTheDocument();
    }
    expect(screen.getByRole('button', { name: 'Planificar día de reparto de armas' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Planificar día de reparto de pólvora' })).toBeInTheDocument();
  });

  it('plans a day and shows it', async () => {
    const user = userEvent.setup();
    const api = distribution();
    const { bodies, resolver } = recordBodies(() => {
      api.set({
        ...ADMIN_PLAN,
        days: [
          ...ADMIN_PLAN.days,
          {
            ...POWDER_DAY,
            id: 'w',
            type: 'WEAPONS',
            date: '2031-04-12',
            location: 'Almacén Sintético',
            slots: [],
          },
        ],
      });
      return HttpResponse.json({}, { status: 201 });
    });
    server.use(mock.post(`/api/distribution/editions/${ADMIN_PLAN.editionId}/distributions`, resolver));
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await weapons();

    await user.click(within(section).getByRole('button', { name: 'Planificar día de reparto de armas' }));
    const panel = await screen.findByRole('dialog', { name: 'Planificar el día de armas' });
    fireEvent.change(within(panel).getByLabelText('Fecha'), { target: { value: '2031-04-12' } });
    await user.type(within(panel).getByLabelText('Lugar'), '  Almacén Sintético ');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(section).findByText('Almacén Sintético')).toBeInTheDocument();
    expect(bodies).toEqual([{ type: 'WEAPONS', date: '2031-04-12', location: 'Almacén Sintético' }]);
  });

  it("puts the API's reason on the date when it is outside the edition", async () => {
    const user = userEvent.setup();
    distribution({ ...ADMIN_PLAN, days: [] });
    server.use(
      mock.post(`/api/distribution/editions/${ADMIN_PLAN.editionId}/distributions`, () =>
        problem(400, 'validation', { errors: { date: 'outOfEdition' } }),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Planificar día de reparto de pólvora' }));
    const panel = await screen.findByRole('dialog', { name: 'Planificar el día de pólvora' });
    fireEvent.change(within(panel).getByLabelText('Fecha'), { target: { value: '2031-05-02' } });
    await user.type(within(panel).getByLabelText('Lugar'), 'Paraje Sintético');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(within(panel).getByLabelText('Fecha')).toHaveAccessibleDescription(
        /Debe estar en el año de la edición y no después de las fiestas/,
      );
    });
  });

  it('edits a day with its version, and reloads it when someone else changed it', async () => {
    const user = userEvent.setup();
    const api = distribution();
    const { bodies, resolver } = recordBodies(() => {
      api.set({ ...ADMIN_PLAN, days: [{ ...POWDER_DAY, location: 'Paraje Cambiado', version: 4 }] });
      return problem(409, 'distribution.modified');
    });
    server.use(mock.put(`/api/distribution/distributions/${POWDER_DAY.id}`, resolver));
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Editar día de reparto de pólvora' }));
    const panel = await screen.findByRole('dialog', { name: 'Editar el día de pólvora' });
    const location = within(panel).getByLabelText('Lugar');
    await user.clear(location);
    await user.type(location, 'Paraje Nuevo');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByText(/Otra persona ha cambiado este día/)).toBeInTheDocument();
    expect(bodies).toEqual([
      { date: POWDER_DAY.date, location: 'Paraje Nuevo', version: POWDER_DAY.version },
    ]);
    expect(await within(section).findByText('Paraje Cambiado')).toBeInTheDocument();
  });

  it('deletes a day after a confirmation that says its slots go', async () => {
    const user = userEvent.setup();
    const api = distribution();
    const deletions: string[] = [];
    server.use(
      mock.delete(`/api/distribution/distributions/${POWDER_DAY.id}`, ({ request }) => {
        deletions.push(new URL(request.url).search);
        api.set({ ...ADMIN_PLAN, days: [] });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Eliminar el día de pólvora' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Eliminar el día de pólvora?' });
    expect(dialog).toHaveTextContent('También se eliminan sus turnos.');
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar día' }));

    expect(await within(section).findByText('Sin planificar.')).toBeInTheDocument();
    expect(deletions).toEqual([`?version=${POWDER_DAY.version}`]);
  });

  it('shows the day as it is now in the panel after a conflict, and saves it with the new version', async () => {
    const user = userEvent.setup();
    const api = distribution();
    let attempts = 0;
    const { bodies, resolver } = recordBodies(() => {
      attempts++;
      if (attempts === 1) {
        api.set({ ...ADMIN_PLAN, days: [{ ...POWDER_DAY, location: 'Paraje Cambiado', version: 4 }] });
        return problem(409, 'distribution.modified');
      }
      return HttpResponse.json({});
    });
    server.use(mock.put(`/api/distribution/distributions/${POWDER_DAY.id}`, resolver));
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Editar día de reparto de pólvora' }));
    const panel = await screen.findByRole('dialog', { name: 'Editar el día de pólvora' });
    const location = within(panel).getByLabelText('Lugar');
    await user.clear(location);
    await user.type(location, 'Paraje Nuevo');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(within(panel).getByLabelText('Lugar')).toHaveValue('Paraje Cambiado');
    });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => {
      expect(bodies).toHaveLength(2);
    });
    expect(bodies[1]).toEqual({ date: POWDER_DAY.date, location: 'Paraje Cambiado', version: 4 });
  });

  it('refreshes the day when deleting it fails because it changed, and says so', async () => {
    const user = userEvent.setup();
    const api = distribution();
    server.use(
      mock.delete(`/api/distribution/distributions/${POWDER_DAY.id}`, () => {
        api.set({ ...ADMIN_PLAN, days: [{ ...POWDER_DAY, location: 'Paraje Cambiado', version: 4 }] });
        return problem(409, 'distribution.modified');
      }),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Eliminar el día de pólvora' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar día' }));

    expect(await within(dialog).findByText(/Otra persona ha cambiado este día/)).toBeInTheDocument();
    expect(await within(section).findByText('Paraje Cambiado')).toBeInTheDocument();
  });

  it('moves focus to "Plan" once a day is deleted', async () => {
    const user = userEvent.setup();
    const api = distribution();
    server.use(
      mock.delete(`/api/distribution/distributions/${POWDER_DAY.id}`, () => {
        api.set({ ...ADMIN_PLAN, days: [] });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Eliminar el día de pólvora' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Eliminar día' }),
    );

    await waitFor(() => {
      expect(
        within(section).getByRole('button', { name: 'Planificar día de reparto de pólvora' }),
      ).toHaveFocus();
    });
  });

  it('tells an Admin why a closed edition cannot be planned, without planning actions but with the lists', async () => {
    distribution({ ...ADMIN_PLAN, editionStatus: 'CLOSED', canPlan: false });
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    expect(screen.getByText(/los días de reparto y los turnos ya no se pueden cambiar/)).toBeInTheDocument();
    expect(
      within(section).queryByRole('button', { name: /Editar|Eliminar|Planificar/ }),
    ).not.toBeInTheDocument();
    expect(
      within(section).getByRole('button', { name: 'Descargar el listado de pólvora en PDF' }),
    ).toBeInTheDocument();
  });

  it('refuses an incomplete slot time before sending it', async () => {
    const user = userEvent.setup();
    distribution();
    let sent = 0;
    server.use(
      mock.put(`/api/distribution/distributions/${POWDER_DAY.id}/slots`, () => {
        sent++;
        return HttpResponse.json({});
      }),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(
      within(section).getByRole('button', { name: 'Editar turnos del día de reparto de pólvora' }),
    );
    const panel = await screen.findByRole('dialog', { name: 'Turnos del día de pólvora' });
    const este = within(panel).getByLabelText(`Hora de ${ESTE.name} (opcional)`);
    Object.defineProperty(este, 'validity', { configurable: true, value: { badInput: true } });
    fireEvent.change(este, { target: { value: '' } });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(este).toHaveAccessibleDescription(/Escribe una hora completa/);
    });
    expect(sent).toBe(0);
  });

  it('saves the slots as one set: a new time, a cleared one and the unchanged ones', async () => {
    const user = userEvent.setup();
    const api = distribution();
    const { bodies, resolver } = recordBodies(() => {
      api.set({
        ...ADMIN_PLAN,
        days: [
          {
            ...POWDER_DAY,
            version: 4,
            slots: [
              { comparsaId: NORTE.id, comparsaName: NORTE.name, startsAt: '09:00' },
              { comparsaId: ESTE.id, comparsaName: ESTE.name, startsAt: '10:15' },
            ],
            withoutSlot: [{ id: SUR.id, name: SUR.name }],
          },
        ],
      });
      return HttpResponse.json({});
    });
    server.use(mock.put(`/api/distribution/distributions/${POWDER_DAY.id}/slots`, resolver));
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(
      within(section).getByRole('button', { name: 'Editar turnos del día de reparto de pólvora' }),
    );
    const panel = await screen.findByRole('dialog', { name: 'Turnos del día de pólvora' });
    await user.click(within(panel).getByRole('button', { name: `Borrar hora de ${SUR.name}` }));
    fireEvent.change(within(panel).getByLabelText(`Hora de ${ESTE.name} (opcional)`), {
      target: { value: '10:15' },
    });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(section).findByText('10:15')).toBeInTheDocument();
    expect(bodies).toEqual([
      {
        version: POWDER_DAY.version,
        slots: [
          { comparsaId: ESTE.id, startsAt: '10:15' },
          { comparsaId: NORTE.id, startsAt: '09:00' },
        ],
      },
    ]);
  });

  it("puts the API's slot reasons on the right comparsa", async () => {
    const user = userEvent.setup();
    distribution();
    server.use(
      mock.put(`/api/distribution/distributions/${POWDER_DAY.id}/slots`, () =>
        problem(400, 'validation', { errors: { 'slots[1].comparsaId': 'unknown' } }),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(
      within(section).getByRole('button', { name: 'Editar turnos del día de reparto de pólvora' }),
    );
    const panel = await screen.findByRole('dialog', { name: 'Turnos del día de pólvora' });
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    // Sent in name order with a time: Norte (0), Sur (1).
    await waitFor(() => {
      expect(within(panel).getByLabelText(`Hora de ${SUR.name} (opcional)`)).toHaveAccessibleDescription(
        /Comparsa desconocida/,
      );
    });
  });

  it('offers an Admin the list as Excel and PDF, with the orders left out and the numbering note', async () => {
    distribution();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    expect(
      within(section).getByRole('button', { name: 'Descargar el listado de pólvora en Excel' }),
    ).toBeInTheDocument();
    expect(
      within(section).getByRole('button', { name: 'Descargar el listado de pólvora en PDF' }),
    ).toBeInTheDocument();
    expect(within(section).getByText(/Los números se calculan en cada descarga/)).toBeInTheDocument();
    expect(within(section).getByText(`${SUR.name} (enviado)`, { exact: false })).toBeInTheDocument();
  });

  it('shows a FiringChief only their slots, without actions or lists', async () => {
    distribution(FIRING_CHIEF_PLAN);
    await renderApp(PAGE, { session: SYNTHETIC_FIRING_CHIEF });
    const section = await powder();

    // Date, time and place of their slot in one line (UI audit).
    expect(
      within(section).getByText(
        `Turno de ${NORTE.name}: 18 de abril de 2031 · 09:00 · Paraje Sintético del Reparto`,
      ),
    ).toBeInTheDocument();
    expect(within(section).queryByRole('table')).not.toBeInTheDocument();
    expect(within(section).queryByRole('button')).not.toBeInTheDocument();
    expect(within(await weapons()).queryByRole('button')).not.toBeInTheDocument();
  });

  it('has no automatically detectable accessibility violations', async () => {
    distribution();
    const { container } = await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    await powder();

    expect(await axeViolations(container)).toEqual([]);
  });
});
