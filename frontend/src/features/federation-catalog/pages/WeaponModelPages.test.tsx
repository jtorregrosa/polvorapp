import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { WeaponModelResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { sortableColumns } from '@/test/table';
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
    // Kind, side and the rentable flag are tags (spec: Tags for fixed values).
    const tagOf = (row: HTMLElement, text: string) => within(row).getByText(text).closest('[data-tag]');
    expect(tagOf(trabuco, 'Trabuco')).toHaveAttribute('data-tone', '1');
    expect(tagOf(trabuco, 'Cristiano')).toHaveAttribute('data-tone', '1');
    expect(tagOf(trabuco, 'Sí')).toHaveAttribute('data-tone', '2');
    const pistola = rowOf(table, PISTOLA.label);
    expect(within(pistola).getAllByText('—')).toHaveLength(3);
    expect(tagOf(pistola, 'Pistola')).toHaveAttribute('data-tone', '4');
    expect(tagOf(pistola, 'No')).toHaveAttribute('data-tone', 'neutral');
    expect(screen.getByRole('link', { name: 'Nuevo modelo' })).toHaveAttribute('href', '/weapon-models/new');
  });

  it('sorts by every column', async () => {
    server.use(mock.get('/api/weapon-models', () => HttpResponse.json([PISTOLA, TRABUCO])));
    await asAdmin('/weapon-models');
    const table = await screen.findByRole('table', { name: 'Modelos de arma' });
    await within(table).findByRole('link', { name: TRABUCO.label });

    expect(sortableColumns(table)).toEqual([
      'Nombre',
      'Tipo',
      'Bando',
      'Mano',
      'Tamaño',
      'Alquilable',
      'Estado',
    ]);
  });

  it.each([
    ['Tipo', [TRABUCO.label, PISTOLA.label]],
    ['Alquilable', [TRABUCO.label, PISTOLA.label]],
  ])('sorts the %s tags by the label they show', async (column, descending) => {
    const user = userEvent.setup();
    server.use(mock.get('/api/weapon-models', () => HttpResponse.json([PISTOLA, TRABUCO])));
    await asAdmin('/weapon-models');
    const table = await screen.findByRole('table', { name: 'Modelos de arma' });
    await within(table).findByRole('link', { name: TRABUCO.label });

    // Twice for descending: "Trabuco" after "Pistola", "Sí" after "No".
    await user.click(within(table).getByRole('button', { name: new RegExp(`^${column}`) }));
    await user.click(within(table).getByRole('button', { name: new RegExp(`^${column}`) }));

    expect(
      within(table)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(descending);
  });

  it.each([
    // Column, first row ascending, first row descending (none for a tie); a missing value goes first.
    ['Nombre', ARCABUZ_RETIRADO.label, TRABUCO.label],
    ['Bando', PISTOLA.label, ARCABUZ_RETIRADO.label],
    ['Mano', PISTOLA.label, null],
    ['Tamaño', PISTOLA.label, null],
    ['Estado', null, ARCABUZ_RETIRADO.label],
  ])('sorts by %s, by the value shown', async (column, ascending, descending) => {
    const user = userEvent.setup();
    server.use(mock.get('/api/weapon-models', () => HttpResponse.json([TRABUCO, PISTOLA, ARCABUZ_RETIRADO])));
    await asAdmin('/weapon-models');
    const table = await screen.findByRole('table', { name: 'Modelos de arma' });
    await within(table).findByRole('link', { name: TRABUCO.label });
    const first = () => within(table).getAllByRole('link')[0]?.textContent;
    const header = () => within(table).getByRole('button', { name: new RegExp(`^${column}`) });

    await user.click(header());
    if (ascending) expect(first()).toBe(ascending);
    await user.click(header());
    if (descending) expect(first()).toBe(descending);
  });

  it('lists every model again when "Solo activos" is cleared', async () => {
    const user = userEvent.setup();
    const queries: string[] = [];
    server.use(
      mock.get('/api/weapon-models', ({ request }) => {
        queries.push(new URL(request.url).search);
        return HttpResponse.json([TRABUCO]);
      }),
    );
    const app = await asAdmin('/weapon-models?onlyActive=true');
    await screen.findByRole('link', { name: TRABUCO.label });

    await user.click(screen.getByRole('checkbox', { name: 'Solo activos' }));

    await waitFor(() => {
      expect(queries.at(-1)).toBe('?includeInactive=true');
    });
    expect(app.location()).toBe('/weapon-models');
  });

  it('stacks each model on a phone, its kind and side as tags with their terms', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      server.use(mock.get('/api/weapon-models', () => HttpResponse.json([TRABUCO])));
      await asAdmin('/weapon-models');

      const list = await screen.findByRole('list', { name: 'Modelos de arma' });
      await within(list).findByRole('link', { name: TRABUCO.label });
      const item = within(list).getAllByRole('listitem')[0];
      if (!item) throw new Error('No item');
      expect(item).toHaveTextContent('Tipo: Trabuco');
      expect(item).toHaveTextContent('Bando: Cristiano');
      expect(within(item).getByText('Cristiano').closest('[data-tag]')).toBeInTheDocument();
      expect(item).toHaveTextContent('Zurdo · Pequeño');
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it('lists inactive models by default, asking the API for them', async () => {
    const queries: string[] = [];
    server.use(
      mock.get('/api/weapon-models', ({ request }) => {
        queries.push(new URL(request.url).search);
        return HttpResponse.json([TRABUCO, ARCABUZ_RETIRADO]);
      }),
    );
    await asAdmin('/weapon-models');

    await screen.findByRole('link', { name: ARCABUZ_RETIRADO.label });
    expect(queries).toEqual(['?includeInactive=true']);
    expect(rowOf(document.body, ARCABUZ_RETIRADO.label)).toHaveTextContent('Inactivo');
    expect(screen.getByRole('checkbox', { name: 'Solo activos' })).not.toBeChecked();
  });

  it('filters by kind and to active models only, keeping the filters in the address', async () => {
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
    await screen.findByRole('link', { name: ARCABUZ_RETIRADO.label });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Tipo' }), 'ARCABUZ');
    await user.click(screen.getByRole('checkbox', { name: 'Solo activos' }));

    // Active only is the API's default: no flag is sent.
    await waitFor(() => {
      expect(queries.at(-1)).toBe('?kind=ARCABUZ');
    });
    expect(app.location()).toBe('/weapon-models?kind=ARCABUZ&onlyActive=true');
    expect(await screen.findByText('Ningún modelo coincide con estos filtros.')).toBeInTheDocument();
  });

  it('keeps "only active" from the address', async () => {
    const queries: string[] = [];
    server.use(
      mock.get('/api/weapon-models', ({ request }) => {
        queries.push(new URL(request.url).search);
        return HttpResponse.json([TRABUCO]);
      }),
    );
    await asAdmin('/weapon-models?onlyActive=true');

    await screen.findByRole('link', { name: TRABUCO.label });
    expect(queries).toEqual(['']);
    expect(screen.getByRole('checkbox', { name: 'Solo activos' })).toBeChecked();
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

async function moreAction(user: UserEvent, name: string): Promise<void> {
  await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
  await user.click(await screen.findByRole('menuitem', { name }));
}

async function editModel(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Editar modelo de arma' }));
  return screen.findByRole('dialog', { name: 'Editar modelo de arma' });
}

/** A saved change is announced politely, without moving focus (spec: Detail pages in read mode). */
async function expectSaved(text: string): Promise<void> {
  await waitFor(() => {
    expect(screen.getByText(text, { selector: '[role=status]' })).toBeInTheDocument();
  });
}

describe('WeaponModelFormPage (spec: Weapon models)', () => {
  it('creates a trabuco with every attribute and continues on its page', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(TRABUCO, { status: 201 }));
    server.use(mock.post('/api/weapon-models', resolver));
    const app = await asAdmin('/weapon-models/new');

    await user.type(await screen.findByRole('textbox', { name: /Nombre/ }), TRABUCO.label);
    // The attributes appear under the chosen kind.
    expect(screen.queryByRole('radiogroup', { name: 'Bando' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('radio', { name: 'Trabuco' }));
    await user.click(
      within(screen.getByRole('radiogroup', { name: 'Bando' })).getByRole('radio', { name: 'Cristiano' }),
    );
    await user.click(screen.getByRole('radio', { name: 'Zurdo' }));
    await user.click(screen.getByRole('radio', { name: 'Pequeño' }));
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

  it('creates a pistol: never rentable, attributes optional and "not set" by default', async () => {
    const user = userEvent.setup();
    modelDetails(PISTOLA);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(PISTOLA, { status: 201 }));
    server.use(mock.post('/api/weapon-models', resolver));
    await asAdmin('/weapon-models/new');

    await user.click(await screen.findByRole('radio', { name: 'Trabuco' }));
    await user.click(screen.getByRole('checkbox', { name: 'Se puede alquilar' }));
    await user.click(screen.getByRole('radio', { name: 'Pistola' }));

    expect(screen.queryByRole('checkbox', { name: 'Se puede alquilar' })).not.toBeInTheDocument();
    const hint = 'Las pistolas nunca se alquilan; bando, mano y tamaño son opcionales.';
    // Announced too, since choosing a pistol changed other fields.
    expect(screen.getAllByRole('status').some((status) => status.textContent === hint)).toBe(true);
    const side = screen.getByRole('radiogroup', { name: 'Bando (opcional)' });
    expect(within(side).getByRole('radio', { name: 'Sin indicar' })).toHaveAttribute('aria-checked', 'true');
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

    await user.click(await screen.findByRole('radio', { name: 'Trabuco' }));
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

    await user.click(await screen.findByRole('radio', { name: 'Pistola' }));
    await user.type(screen.getByRole('textbox', { name: /Nombre/ }), 'PISTOLA');
    await user.click(screen.getByRole('button', { name: 'Crear modelo' }));

    expect(await screen.findByRole('group', { name: 'Hay un problema' })).toHaveTextContent(text);
  });

  it('offers to cancel back to the list', async () => {
    await asAdmin('/weapon-models/new');

    expect(await screen.findByRole('link', { name: 'Cancelar' })).toHaveAttribute('href', '/weapon-models');
  });
});

describe('WeaponModelDetailPage (specs: Weapon models, Detail pages in read mode)', () => {
  it('shows the model read-only, with "not set" for a pistol attribute', async () => {
    modelDetails(PISTOLA);
    await asAdmin(`/weapon-models/${PISTOLA.id}`);

    const heading = await screen.findByRole('heading', { level: 1, name: PISTOLA.label });
    const header = heading.closest('header');
    if (!header) throw new Error('No record header');
    expect(within(header).getByText('Pistola').closest('[data-tag]')).toHaveAttribute('data-tone', '4');
    const data = screen.getByRole('region', { name: 'Modelo de arma' });
    expect(data).toHaveTextContent('Sin indicar');
    expect(within(data).getByText('Pistola').closest('[data-tag]')).toBeInTheDocument();
    expect(within(data).getByText('No').closest('[data-tag]')).toHaveAttribute('data-tone', 'neutral');
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('edits a model in a panel and keeps its state', async () => {
    const user = userEvent.setup();
    const details = modelDetails(TRABUCO);
    const { bodies, resolver } = recordBodies(() => {
      details.set({ size: 'NORMAL', label: 'TRABUCO CRISTIANO ZURDO' });
      return HttpResponse.json({ ...TRABUCO, size: 'NORMAL', label: 'TRABUCO CRISTIANO ZURDO' });
    });
    server.use(mock.put(`/api/weapon-models/${TRABUCO.id}`, resolver));
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    const panel = await editModel(user);
    const label = within(panel).getByRole('textbox', { name: 'Nombre' });
    expect(label).toHaveValue(TRABUCO.label);
    expect(within(panel).getByRole('checkbox', { name: 'Se puede alquilar' })).toBeChecked();
    await user.clear(label);
    await user.type(label, 'TRABUCO CRISTIANO ZURDO');
    await user.click(within(panel).getByRole('radio', { name: 'Normal' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
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
    expect(
      await screen.findByRole('heading', { level: 1, name: 'TRABUCO CRISTIANO ZURDO' }),
    ).toBeInTheDocument();
  });

  it('shows a duplicate label on the label field and a duplicate combination in the panel summary', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    let conflict = 'weaponModels.labelTaken';
    server.use(mock.put(`/api/weapon-models/${TRABUCO.id}`, () => problem(409, conflict)));
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    const panel = await editModel(user);
    await user.type(within(panel).getByRole('textbox', { name: 'Nombre' }), ' BIS');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByText(/Ya existe un modelo con ese nombre\./, {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
    const summary = within(panel).getByRole('group', { name: 'Hay un problema' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });

    conflict = 'weaponModels.combinationTaken';
    await user.click(within(panel).getByRole('radio', { name: 'Normal' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(within(panel).getByRole('group', { name: 'Hay un problema' })).toHaveTextContent(
        'Ya existe un modelo con ese tipo, bando, mano y tamaño.',
      );
    });
  });

  it('clears a pistol attribute with "not set", sent as null', async () => {
    const user = userEvent.setup();
    const sided = { ...PISTOLA, side: 'MOORISH' as const };
    modelDetails(sided);
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(PISTOLA));
    server.use(mock.put(`/api/weapon-models/${PISTOLA.id}`, resolver));
    await asAdmin(`/weapon-models/${PISTOLA.id}`);

    const panel = await editModel(user);
    const side = within(panel).getByRole('radiogroup', { name: 'Bando (opcional)' });
    expect(within(side).getByRole('radio', { name: 'Moro' })).toHaveAttribute('aria-checked', 'true');
    await user.click(within(side).getByRole('radio', { name: 'Sin indicar' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved('Cambios guardados');
    expect(bodies).toEqual([
      { kind: 'PISTOL', side: null, handedness: null, size: null, rentable: false, label: 'PISTOLA' },
    ]);
  });

  it('asks for the attributes again when a pistol becomes a trabuco, which may be rented', async () => {
    const user = userEvent.setup();
    modelDetails(PISTOLA);
    let called = false;
    server.use(
      mock.put(`/api/weapon-models/${PISTOLA.id}`, () => {
        called = true;
        return HttpResponse.json(PISTOLA);
      }),
    );
    await asAdmin(`/weapon-models/${PISTOLA.id}`);

    const panel = await editModel(user);
    expect(within(panel).queryByRole('checkbox', { name: 'Se puede alquilar' })).not.toBeInTheDocument();
    await user.click(within(panel).getByRole('radio', { name: 'Trabuco' }));
    expect(within(panel).getByRole('checkbox', { name: 'Se puede alquilar' })).not.toBeChecked();
    expect(within(panel).queryByRole('radio', { name: 'Sin indicar' })).not.toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findAllByText('Elige una opción.', { selector: '[data-slot="form-message"]' }),
    ).toHaveLength(3);
    expect(called).toBe(false);
  });

  it('keeps a failed deactivation in the dialog', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    server.use(
      mock.post(`/api/weapon-models/${TRABUCO.id}/deactivate`, () => problem(404, 'weaponModels.notFound')),
    );
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await moreAction(user, 'Desactivar modelo');
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar modelo' }));

    expect(await within(dialog).findByText('Este modelo de arma no existe.')).toBeInTheDocument();
  });

  it('deactivates from "More actions" after confirmation, then reactivates', async () => {
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

    await moreAction(user, 'Desactivar modelo');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Desactivar modelo' }),
    );

    await expectSaved('Modelo de arma desactivado.');
    expect(await screen.findByText(/Este modelo está inactivo/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    await moreAction(user, 'Reactivar modelo');
    await expectSaved('Modelo de arma reactivado.');
  });

  it('sets the destructive action apart in "More actions", and Escape returns focus to it', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).getByRole('separator')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Eliminar modelo' })).toHaveAttribute(
      'data-variant',
      'destructive',
    );
    await user.click(within(menu).getByRole('menuitem', { name: 'Eliminar modelo' }));
    await screen.findByRole('alertdialog');
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    });
  });

  it('deletes after confirmation and returns to the list', async () => {
    const user = userEvent.setup();
    modelDetails(TRABUCO);
    server.use(
      mock.delete(`/api/weapon-models/${TRABUCO.id}`, () => new HttpResponse(null, { status: 204 })),
    );
    const app = await asAdmin(`/weapon-models/${TRABUCO.id}`);

    await moreAction(user, 'Eliminar modelo');
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

    await moreAction(user, 'Eliminar modelo');
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

  it('has no accessibility violations, read-only and with the panel open', async () => {
    const user = userEvent.setup();
    modelDetails(PISTOLA);
    const { container } = await asAdmin(`/weapon-models/${PISTOLA.id}`);
    await screen.findByRole('button', { name: 'Más acciones' });

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
    const panel = await editModel(user);
    await waitFor(async () => {
      expect(await axeViolations(panel)).toEqual([]);
    });
  });
});
