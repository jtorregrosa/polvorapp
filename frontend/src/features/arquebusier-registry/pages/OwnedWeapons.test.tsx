import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { ARCABUZ, DETAIL_UNO, MODELS, NORTE, PISTOLA } from '../test-data';

const [WEAPON, RETIRED_WEAPON] = DETAIL_UNO.ownedWeapons as [
  ArquebusierResponse['ownedWeapons'][number],
  ArquebusierResponse['ownedWeapons'][number],
];

function detail(current: () => ArquebusierResponse = () => DETAIL_UNO) {
  server.use(
    mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(current())),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
    // Without includeInactive the catalogue lists only active models.
    mock.get('/api/weapon-models', () => HttpResponse.json(MODELS.filter((model) => model.active))),
  );
}

describe('Owned weapons section (spec: Owned weapons, Registry screens)', () => {
  it('lists the weapons with their model, translated attributes and numbers', async () => {
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const table = await screen.findByRole('table', { name: 'Armas propias de Arcabucero García Sintético' });
    const row = within(table).getByText('SINT-0001').closest('tr') as HTMLElement;
    expect(within(row).getByText('ARCABUZ MORO DIESTRO')).toBeInTheDocument();
    expect(within(row).getByText('Arcabuz')).toBeInTheDocument();
    expect(within(row).getByText('Moro · Diestro · Normal')).toBeInTheDocument();
    expect(within(row).getByText('1001')).toBeInTheDocument();
    expect(within(table).getByText('ARCABUZ MORO ZURDO (PEQUEÑO) (retirado)')).toBeInTheDocument();
    expect(
      await within(row).findByRole('link', { name: 'Editar el arma ARCABUZ MORO DIESTRO nº 1001' }),
    ).toHaveAttribute('href', `/arquebusiers/${DETAIL_UNO.id}/weapons/${WEAPON.id}`);
    expect(await screen.findByRole('link', { name: 'Añadir arma propia' })).toHaveAttribute(
      'href',
      `/arquebusiers/${DETAIL_UNO.id}/weapons/new`,
    );
  });

  it('removes a weapon after a confirmation that says it cannot be undone', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${WEAPON.id}`, () => {
        current = { ...DETAIL_UNO, ownedWeapons: [RETIRED_WEAPON] };
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(
      await screen.findByRole('button', { name: 'Quitar el arma ARCABUZ MORO DIESTRO nº 1001' }),
    );
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar el arma 1001?' });
    expect(dialog).toHaveAccessibleDescription(/no se puede deshacer/);
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }));

    expect(await screen.findByText('Arma 1001 quitada.')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByText('SINT-0001')).not.toBeInTheDocument();
    });
  });

  it('treats a weapon someone else already removed as removed', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${WEAPON.id}`, () => {
        current = { ...DETAIL_UNO, ownedWeapons: [RETIRED_WEAPON] };
        return problem(404, 'ownedWeapons.notFound');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(
      await screen.findByRole('button', { name: 'Quitar el arma ARCABUZ MORO DIESTRO nº 1001' }),
    );
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar' }));

    expect(await screen.findByText('Arma 1001 quitada.')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByText('SINT-0001')).not.toBeInTheDocument();
    });
  });

  it('says so when there are no owned weapons', async () => {
    detail(() => ({ ...DETAIL_UNO, ownedWeapons: [] }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText('No tiene armas propias registradas.')).toBeInTheDocument();
  });
});

describe('Owned weapon form (spec: Owned weapons)', () => {
  it('adds a weapon with an active model and returns to the arquebusier', async () => {
    const user = userEvent.setup();
    detail();
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json(
        { id: 'new', model: PISTOLA, weaponNumber: '7', ownershipGuideNumber: 'SINT-0009', version: 1 },
        { status: 201 },
      ),
    );
    server.use(mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons`, resolver));
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/new`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });

    const model = await screen.findByLabelText(/^Modelo/);
    await screen.findByRole('option', { name: 'PISTOLA' });
    expect(within(model).queryByRole('option', { name: /retirado/ })).not.toBeInTheDocument();
    await user.selectOptions(model, PISTOLA.id);
    await user.type(screen.getByLabelText(/^Nº de arma/), '7');
    await user.type(screen.getByLabelText(/^Nº de guía/), 'sint-0009');
    await user.click(screen.getByRole('button', { name: 'Añadir arma' }));

    await waitFor(() => {
      expect(app.location()).toBe(`/arquebusiers/${DETAIL_UNO.id}`);
    });
    expect(bodies).toEqual([
      { weaponModelId: PISTOLA.id, weaponNumber: '7', ownershipGuideNumber: 'sint-0009' },
    ]);
    expect(await screen.findByText('Arma añadida.')).toBeInTheDocument();
  });

  it('shows a guide already used elsewhere and a retired model on their fields', async () => {
    const user = userEvent.setup();
    detail();
    let reply = problem(409, 'ownedWeapons.guideTaken');
    server.use(mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons`, () => reply));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/new`, { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('option', { name: ARCABUZ.label });

    await user.selectOptions(screen.getByLabelText(/^Modelo/), ARCABUZ.id);
    await user.type(screen.getByLabelText(/^Nº de arma/), '7');
    await user.type(screen.getByLabelText(/^Nº de guía/), 'SINT-0001');
    await user.click(screen.getByRole('button', { name: 'Añadir arma' }));
    await waitFor(() => {
      expect(screen.getByLabelText(/^Nº de guía/)).toHaveAccessibleDescription(/ya está registrado/);
    });

    reply = problem(409, 'ownedWeapons.modelInactive');
    await user.click(screen.getByRole('button', { name: 'Añadir arma' }));
    await waitFor(() => {
      expect(screen.getByLabelText(/^Modelo/)).toHaveAccessibleDescription(/retirado del catálogo/);
    });
  });

  it('edits a weapon that keeps its retired model, sending the version', async () => {
    const user = userEvent.setup();
    detail();
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json({ ...RETIRED_WEAPON, weaponNumber: '2002', version: 13 }),
    );
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${RETIRED_WEAPON.id}`, resolver));
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/${RETIRED_WEAPON.id}`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });

    const model = await screen.findByLabelText(/^Modelo/);
    await waitFor(() => {
      expect(model).toHaveValue(RETIRED_WEAPON.model.id);
    });
    expect(
      within(model).getByRole('option', { name: 'ARCABUZ MORO ZURDO (PEQUEÑO) (retirado)' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Volver al arcabucero' })).toHaveAttribute(
      'href',
      `/arquebusiers/${DETAIL_UNO.id}`,
    );
    const number = screen.getByLabelText(/^Nº de arma/);
    await user.clear(number);
    await user.type(number, '2002');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(app.location()).toBe(`/arquebusiers/${DETAIL_UNO.id}`);
    });
    expect(bodies).toEqual([
      {
        weaponModelId: RETIRED_WEAPON.model.id,
        weaponNumber: '2002',
        ownershipGuideNumber: 'SINT-0002',
        version: 12,
      },
    ]);
  });

  it('keeps what the user typed when someone else changed the weapon meanwhile', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    server.use(
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${WEAPON.id}`, () => {
        current = {
          ...DETAIL_UNO,
          ownedWeapons: [{ ...WEAPON, ownershipGuideNumber: 'SINT-0077', version: 99 }, RETIRED_WEAPON],
        };
        return problem(409, 'ownedWeapons.modified');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/${WEAPON.id}`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });
    const number = await screen.findByLabelText(/^Nº de arma/);
    await waitFor(() => {
      expect(number).toHaveValue(WEAPON.weaponNumber);
    });

    await user.clear(number);
    await user.type(number, '5005');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(screen.getByRole('group', { name: 'Hay un problema' })).toHaveTextContent(
        /Otra persona ha cambiado esta arma/,
      );
    });
    await waitFor(() => {
      expect(screen.getByLabelText(/^Nº de guía/)).toHaveValue('SINT-0077');
    });
    expect(screen.getByLabelText(/^Nº de arma/)).toHaveValue('5005');
  });

  it('shows the not-found page when the weapon was removed before saving', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    server.use(
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${WEAPON.id}`, () => {
        current = { ...DETAIL_UNO, ownedWeapons: [RETIRED_WEAPON] };
        return problem(404, 'ownedWeapons.notFound');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/${WEAPON.id}`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });

    await user.click(await screen.findByRole('button', { name: 'Guardar cambios' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('still says why nothing was saved when the weapon is gone and the record cannot be reloaded', async () => {
    const user = userEvent.setup();
    let failing = false;
    server.use(
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () =>
        failing ? problem(500, 'internal') : HttpResponse.json(DETAIL_UNO),
      ),
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/weapon-models', () => HttpResponse.json(MODELS.filter((model) => model.active))),
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}/owned-weapons/${WEAPON.id}`, () => {
        failing = true;
        return problem(404, 'ownedWeapons.notFound');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/${WEAPON.id}`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });

    await user.click(await screen.findByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByRole('group', { name: 'Hay un problema' })).toBeInTheDocument();
  });

  it('says the model cannot be chosen while the catalogue cannot be loaded, and retries', async () => {
    const user = userEvent.setup();
    detail();
    let fail = true;
    server.use(
      mock.get('/api/weapon-models', () =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json(MODELS.filter((model) => model.active)),
      ),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/new`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText(/Sin el catálogo no se puede elegir el modelo/)).toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('option', { name: 'PISTOLA' })).toBeInTheDocument();
  });

  it('shows the not-found page for a weapon that is not under this arquebusier', async () => {
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/00000000-0000-4000-8000-000000000499`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });

    expect(await screen.findByRole('heading', { name: 'Página no encontrada' })).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    detail();
    const { container } = await renderApp(`/arquebusiers/${DETAIL_UNO.id}/weapons/new`, {
      session: SYNTHETIC_FIRING_CHIEF,
    });
    await screen.findByRole('option', { name: 'PISTOLA' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
