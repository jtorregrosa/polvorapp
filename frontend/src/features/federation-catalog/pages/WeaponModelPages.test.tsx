import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { WeaponModelResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { ARCABUZ_RETIRADO, PISTOLA, TRABUCO } from '../test-data';

function asAdmin(path: string) {
  return renderApp(path, { session: SYNTHETIC_ADMIN });
}

/** The table row that contains `text`. */
function rowOf(container: HTMLElement, text: string): HTMLElement {
  const row = within(container).getByText(text).closest('tr');
  if (!row) throw new Error(`No row for ${text}`);
  return row;
}

async function expectFocusedNotice(text: string): Promise<void> {
  const notice = (await screen.findByText(text)).closest('[data-severity]');
  await waitFor(() => {
    expect(notice).toHaveFocus();
  });
}

/** `GET /api/weapon-models/:id` answers with the model, updated by later calls to `set`. */
function modelDetails(initial: WeaponModelResponse) {
  let current = initial;
  server.use(
    mock.get(`/api/weapon-models/${initial.id}`, () => HttpResponse.json(current)),
    mock.get('/api/weapon-models', () => HttpResponse.json([current])),
  );
  return {
    set: (next: Partial<WeaponModelResponse>) => {
      current = { ...current, ...next };
    },
  };
}

describe('WeaponModelsPage (specs: Weapon models, Weapon catalogue access)', () => {
  it('lists models with translated attributes, a dash for a pistol, and whether they can be rented', async () => {
    server.use(mock.get('/api/weapon-models', () => HttpResponse.json([PISTOLA, TRABUCO])));
    await asAdmin('/weapon-models');

    const table = await screen.findByRole('table', { name: 'Modelos de arma' });
    await within(table).findByRole('link', { name: TRABUCO.label });
    const trabuco = rowOf(table, TRABUCO.label);
    expect(trabuco).toHaveTextContent('Trabuco');
    expect(trabuco).toHaveTextContent('Cristiano');
    expect(trabuco).toHaveTextContent('Zurdo');
    expect(trabuco).toHaveTextContent('Pequeño');
    expect(trabuco).toHaveTextContent('Sí');
    const pistola = rowOf(table, PISTOLA.label);
    expect(within(pistola).getAllByText('—')).toHaveLength(3);
    expect(pistola).toHaveTextContent('No');
    expect(screen.getByRole('link', { name: 'Nuevo modelo' })).toHaveAttribute('href', '/weapon-models/new');
  });

  it('filters by kind and includes inactive models through the API', async () => {
    const user = userEvent.setup();
    const queries: string[] = [];
    server.use(
      mock.get('/api/weapon-models', ({ request }) => {
        const url = new URL(request.url);
        queries.push(url.search);
        return HttpResponse.json(
          url.searchParams.get('includeInactive') === 'true' ? [ARCABUZ_RETIRADO] : [],
        );
      }),
    );
    const app = await asAdmin('/weapon-models');
    await screen.findByRole('table', { name: 'Modelos de arma' });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Tipo' }), 'ARCABUZ');
    await user.click(screen.getByRole('checkbox', { name: 'Incluir inactivos' }));

    await screen.findByRole('link', { name: ARCABUZ_RETIRADO.label });
    expect(queries).toContain('?kind=ARCABUZ&includeInactive=true');
    expect(app.location()).toBe('/weapon-models?kind=ARCABUZ&includeInactive=true');
    expect(rowOf(document.body, ARCABUZ_RETIRADO.label)).toHaveTextContent('Inactivo');
  });

  it('shows an error, not an empty list, when the catalogue cannot be loaded', async () => {
    server.use(mock.get('/api/weapon-models', () => problem(500, 'unexpected')));
    await asAdmin('/weapon-models');

    expect(await screen.findByText('Algo ha fallado. Inténtalo de nuevo.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('is not shown to a FiringChief', async () => {
    await renderApp('/weapon-models', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    server.use(mock.get('/api/weapon-models', () => HttpResponse.json([PISTOLA, TRABUCO])));
    const { container } = await asAdmin('/weapon-models');
    await screen.findByRole('link', { name: TRABUCO.label });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});

describe('WeaponModelFormPage (spec: Weapon models)', () => {
  it('creates a trabuco with every attribute and continues on its page', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(TRABUCO, { status: 201 }));
    server.use(mock.post('/api/weapon-models', resolver));
    const app = await asAdmin('/weapon-models/new');

    await user.type(await screen.findByRole('textbox', { name: /Nombre/ }), TRABUCO.label);
    await user.selectOptions(screen.getByRole('combobox', { name: /Tipo/ }), 'TRABUCO');
    await user.selectOptions(screen.getByRole('combobox', { name: /Bando/ }), 'CHRISTIAN');
    await user.selectOptions(screen.getByRole('combobox', { name: /Mano/ }), 'LEFT');
    await user.selectOptions(screen.getByRole('combobox', { name: /Tamaño/ }), 'SMALL');
    await user.click(screen.getByRole('checkbox', { name: 'Se puede alquilar' }));
    await user.click(screen.getByRole('button', { name: 'Crear modelo' }));

    await expectFocusedNotice('Modelo de arma creado.');
    expect(bodies).toEqual([
      {
        kind: 'TRABUCO',
        side: 'CHRISTIAN',
        handedness: 'LEFT',
        size: 'SMALL',
        rentable: true,
        label: TRABUCO.label,
      },
    ]);
    expect(app.location()).toBe(`/weapon-models/${TRABUCO.id}`);
  });

  it('creates a pistol: never rentable, attributes optional', async () => {
    const user = userEvent.setup();
    modelDetails(PISTOLA);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(PISTOLA, { status: 201 }));
    server.use(mock.post('/api/weapon-models', resolver));
    await asAdmin('/weapon-models/new');

    await user.selectOptions(await screen.findByRole('combobox', { name: /Tipo/ }), 'TRABUCO');
    await user.click(screen.getByRole('checkbox', { name: 'Se puede alquilar' }));
    await user.selectOptions(screen.getByRole('combobox', { name: /Tipo/ }), 'PISTOL');
    const rentable = screen.getByRole('checkbox', { name: 'Se puede alquilar' });
    expect(rentable).toBeDisabled();
    expect(rentable).not.toBeChecked();
    const hint = 'Las pistolas nunca se alquilan; bando, mano y tamaño son opcionales.';
    expect(rentable).toHaveAccessibleDescription(hint);
    // Announced too, since choosing a pistol changed other fields.
    expect(screen.getAllByRole('status').some((status) => status.textContent === hint)).toBe(true);
    await user.type(screen.getByRole('textbox', { name: /Nombre/ }), 'PISTOLA');
    await user.click(screen.getByRole('button', { name: 'Crear modelo' }));

    await screen.findByText('Modelo de arma creado.');
    expect(bodies).toEqual([
      { kind: 'PISTOL', side: null, handedness: null, size: null, rentable: false, label: 'PISTOLA' },
    ]);
  });

  it('asks for every attribute of a trabuco before calling the API', async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      mock.post('/api/weapon-models', () => {
        called = true;
        return HttpResponse.json(TRABUCO, { status: 201 });
      }),
    );
    await asAdmin('/weapon-models/new');

    await user.selectOptions(await screen.findByRole('combobox', { name: /Tipo/ }), 'TRABUCO');
    await user.click(screen.getByRole('button', { name: 'Crear modelo' }));

    // All at once: the three missing attributes and the missing name.
    expect(
      await screen.findAllByText('Elige una opción.', { selector: '[data-slot="form-message"]' }),
    ).toHaveLength(3);
    expect(
      screen.getByText('Este campo es obligatorio.', { selector: '[data-slot="form-message"]' }),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it.each([
    ['weaponModels.labelTaken', 'Ya existe un modelo con ese nombre.'],
    ['weaponModels.combinationTaken', 'Ya existe un modelo con ese tipo, bando, mano y tamaño.'],
  ])('explains a %s conflict', async (code, text) => {
    const user = userEvent.setup();
    server.use(mock.post('/api/weapon-models', () => problem(409, code)));
    await asAdmin('/weapon-models/new');

    await user.selectOptions(await screen.findByRole('combobox', { name: /Tipo/ }), 'PISTOL');
    await user.type(screen.getByRole('textbox', { name: /Nombre/ }), 'PISTOLA');
    await user.click(screen.getByRole('button', { name: 'Crear modelo' }));

    expect(await screen.findByText(text)).toBeInTheDocument();
  });

  it('edits a model and keeps its state', async () => {
    const user = userEvent.setup();
    const details = modelDetails(TRABUCO);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ size: 'NORMAL', label: 'TRABUCO CRISTIANO ZURDO' });
      return HttpResponse.json({ ...TRABUCO, size: 'NORMAL', label: 'TRABUCO CRISTIANO ZURDO' });
    });
    server.use(mock.put(`/api/weapon-models/${TRABUCO.id}`, resolver));
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    const label = await screen.findByRole('textbox', { name: /Nombre/ });
    await waitFor(() => {
      expect(label).toHaveValue(TRABUCO.label);
    });
    expect(screen.getByRole('checkbox', { name: 'Se puede alquilar' })).toBeChecked();
    await user.clear(label);
    await user.type(label, 'TRABUCO CRISTIANO ZURDO');
    await user.selectOptions(screen.getByRole('combobox', { name: /Tamaño/ }), 'NORMAL');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await expectFocusedNotice('Cambios guardados.');
    expect(bodies).toEqual([
      {
        kind: 'TRABUCO',
        side: 'CHRISTIAN',
        handedness: 'LEFT',
        size: 'NORMAL',
        rentable: true,
        label: 'TRABUCO CRISTIANO ZURDO',
      },
    ]);
  });

  it('shows a duplicate label on the label field and a duplicate combination on the page when an edit is rejected', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    let conflict = 'weaponModels.labelTaken';
    server.use(mock.put(`/api/weapon-models/${TRABUCO.id}`, () => problem(409, conflict)));
    await asAdmin(`/weapon-models/${TRABUCO.id}`);
    const label = await screen.findByRole('textbox', { name: /Nombre/ });
    await waitFor(() => {
      expect(label).toHaveValue(TRABUCO.label);
    });

    await user.type(label, ' BIS');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Ya existe un modelo con ese nombre.')).toHaveAttribute(
      'data-slot',
      'form-message',
    );
    await waitFor(() => {
      expect(label).toHaveFocus();
    });

    conflict = 'weaponModels.combinationTaken';
    await user.selectOptions(screen.getByRole('combobox', { name: /Tamaño/ }), 'NORMAL');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await expectFocusedNotice('Ya existe un modelo con ese tipo, bando, mano y tamaño.');
  });

  it('keeps a failed deactivation in the dialog', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    server.use(
      mock.post(`/api/weapon-models/${TRABUCO.id}/deactivate`, () => problem(404, 'weaponModels.notFound')),
    );
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await user.click(await screen.findByRole('button', { name: 'Desactivar modelo' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar modelo' }));

    expect(await within(dialog).findByText('Este modelo de arma no existe.')).toBeInTheDocument();
  });

  it('deactivates after confirmation, then reactivates', async () => {
    const user = userEvent.setup();
    const details = modelDetails(TRABUCO);
    server.use(
      mock.post(`/api/weapon-models/${TRABUCO.id}/deactivate`, () => {
        details.set({ active: false });
        return HttpResponse.json({ ...TRABUCO, active: false });
      }),
      mock.post(`/api/weapon-models/${TRABUCO.id}/reactivate`, () => {
        details.set({ active: true });
        return HttpResponse.json(TRABUCO);
      }),
    );
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await user.click(await screen.findByRole('button', { name: 'Desactivar modelo' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Desactivar modelo' }),
    );

    await expectFocusedNotice('Modelo de arma desactivado.');
    expect(await screen.findByText(/Este modelo está inactivo/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reactivar modelo' }));
    await expectFocusedNotice('Modelo de arma reactivado.');
  });

  it('deletes after confirmation and returns to the list', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    server.use(
      mock.delete(`/api/weapon-models/${TRABUCO.id}`, () => new HttpResponse(null, { status: 204 })),
    );
    const app = await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await user.click(await screen.findByRole('button', { name: 'Eliminar modelo' }));
    const dialog = await screen.findByRole('alertdialog', { name: `¿Eliminar ${TRABUCO.label}?` });
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar modelo' }));

    await expectFocusedNotice('Modelo de arma eliminado.');
    expect(app.location()).toBe('/weapon-models');
  });

  it('keeps a model in use and says to deactivate it instead', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    server.use(mock.delete(`/api/weapon-models/${TRABUCO.id}`, () => problem(409, 'weaponModels.inUse')));
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await user.click(await screen.findByRole('button', { name: 'Eliminar modelo' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar modelo' }));

    expect(
      await within(dialog).findByText(
        'Otros registros usan este modelo, así que no se puede eliminar. Desactívalo en su lugar.',
      ),
    ).toBeInTheDocument();
  });

  it('shows the not-found page for an unknown model', async () => {
    server.use(mock.get(`/api/weapon-models/${TRABUCO.id}`, () => problem(404, 'weaponModels.notFound')));
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Página no encontrada' }),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    modelDetails(PISTOLA);
    const { container } = await asAdmin(`/weapon-models/${PISTOLA.id}`);
    await screen.findByRole('button', { name: 'Eliminar modelo' });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});
