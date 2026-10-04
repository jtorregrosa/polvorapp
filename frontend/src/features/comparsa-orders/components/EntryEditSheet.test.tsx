import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { OrderResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { personName } from '../orderText';
import { ARCABUZ, NORTE_ORDER, TRABUCO } from '../test-data';

const ORIGINAL_WIDTH = window.innerWidth;
const UNO = 'Sintético Uno, Arcabucero';
const CINCO = 'Sintético Cinco, Arcabucero';

function setViewportWidth(width: number) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: width });
}

function serve(order: OrderResponse) {
  let current = order;
  server.use(
    mock.get(`/api/comparsa-orders/${order.id}`, () => HttpResponse.json(current)),
    mock.get(`/api/comparsas/${order.comparsa.id}`, () =>
      HttpResponse.json({ ...order.comparsa, active: true, logo: null, firingChiefs: [], version: 1 }),
    ),
    mock.get('/api/weapon-models', () =>
      HttpResponse.json([
        {
          ...ARCABUZ,
          kind: 'ARCABUZ',
          side: 'MOORISH',
          handedness: 'RIGHT',
          size: 'NORMAL',
          active: true,
          rentable: true,
          version: 1,
        },
        {
          ...TRABUCO,
          kind: 'TRABUCO',
          side: 'CHRISTIAN',
          handedness: 'RIGHT',
          size: 'NORMAL',
          active: true,
          rentable: true,
          version: 1,
        },
      ]),
    ),
  );
  return {
    set: (next: OrderResponse) => {
      current = next;
    },
  };
}

async function openPanel(user: UserEvent, name: string, order: OrderResponse = NORTE_ORDER) {
  const served = serve(order);
  const app = await renderApp(`/orders/${order.id}`, { session: SYNTHETIC_FIRING_CHIEF });
  await user.click(await screen.findByRole('button', { name: `Editar la línea de ${name}` }));
  const dialog = await screen.findByRole('dialog', { name: `Línea de ${name}` });
  return { app, dialog, served };
}

const save = (user: UserEvent, dialog: HTMLElement) =>
  user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

describe('EntryEditSheet (spec: Orders screens, Edition entries (BR-05, BR-07))', () => {
  afterEach(() => {
    setViewportWidth(ORIGINAL_WIDTH);
  });

  it('offers status and powder as cards, and the arquebusier’s own weapons, the offered models and a loan', async () => {
    const user = userEvent.setup();
    const { dialog } = await openPanel(user, UNO);

    expect(within(dialog).getByRole('radiogroup', { name: 'Estado en esta edición' })).toBeInTheDocument();
    expect(within(dialog).getByRole('radio', { name: '2 kg' })).toBeChecked();
    const weapon = within(dialog).getByRole('radiogroup', { name: 'Arma' });
    expect(
      within(weapon)
        .getAllByRole('radio')
        .map((radio) => radio.getAttribute('value')),
    ).toEqual(['OWNED', 'RENTAL', 'LOAN', 'NONE']);
    expect(within(dialog).getByRole('combobox', { name: 'Arma propia' })).toHaveDisplayValue(
      'TRABUCO CRISTIANO DIESTRO 1001',
    );

    await user.click(within(dialog).getByRole('radio', { name: 'Alquiler' }));
    const models = within(dialog).getByRole('combobox', { name: 'Modelo de alquiler' });
    expect(
      within(models)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toContain('ARCABUZ MORO DIESTRO');
  });

  it('clears and disables the rest when the entry becomes a reserve, and saves it so', async () => {
    const user = userEvent.setup();
    const { bodies, resolver } = recordBodies(() => HttpResponse.json({ ...NORTE_ORDER, version: 13 }));
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/${NORTE_ORDER.entries[0]?.id}`, resolver),
    );
    const { dialog } = await openPanel(user, UNO);

    await user.click(within(dialog).getByRole('radio', { name: /^Reserva/ }));

    expect(within(dialog).getByRole('radio', { name: '0 kg' })).toBeChecked();
    expect(within(dialog).getByRole('radio', { name: '0 kg' })).toBeDisabled();
    expect(within(dialog).getByRole('textbox', { name: 'Cajas de pistones' })).toHaveValue('0');
    expect(within(dialog).getByRole('textbox', { name: 'Cajas de pistones' })).toBeDisabled();
    expect(within(dialog).getByRole('radio', { name: /^Sin arma/ })).toBeChecked();
    expect(within(dialog).getByRole('radio', { name: 'Sin cantimplora' })).toBeChecked();
    await save(user, dialog);

    await waitFor(() => {
      expect(bodies).toEqual([
        {
          version: NORTE_ORDER.entries[0]?.version,
          status: 'RESERVE',
          powderKg: 0,
          capsBoxes: 0,
          capsType: null,
          weaponSource: 'NONE',
          ownedWeaponId: null,
          rentalWeaponModelId: null,
          loan: null,
          flask: 'NONE',
        },
      ]);
    });
    expect(await screen.findAllByText('Cambios guardados')).not.toHaveLength(0);
  });

  it('asks for the caps type once there are boxes, before sending anything', async () => {
    const user = userEvent.setup();
    let sent = false;
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () => {
        sent = true;
        return HttpResponse.json(NORTE_ORDER);
      }),
    );
    const { dialog } = await openPanel(user, 'Sintético Tres, Arcabucero');

    await user.type(within(dialog).getByRole('textbox', { name: 'Cajas de pistones' }), '{Backspace}4');
    await save(user, dialog);

    expect(await within(dialog).findByText('Hay un problema')).toBeInTheDocument();
    expect(within(dialog).getByRole('combobox', { name: 'Tipo de pistones' })).toHaveAccessibleDescription(
      /Elige el tipo de pistones/,
    );
    expect(sent).toBe(false);
  });

  it('puts a server 400 on its fields', async () => {
    const user = userEvent.setup();
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () =>
        problem(400, 'validation', { errors: { rentalWeaponModelId: 'notOffered' } }),
      ),
    );
    const { dialog } = await openPanel(user, 'Sintético Tres, Arcabucero');

    await save(user, dialog);

    await waitFor(() => {
      expect(
        within(dialog).getByRole('combobox', { name: 'Modelo de alquiler' }),
      ).toHaveAccessibleDescription(/Este modelo no se alquila en la edición/);
    });
  });

  it('keeps the panel open and reloads the entry when it changed meanwhile', async () => {
    const user = userEvent.setup();
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () =>
        problem(409, 'entries.modified'),
      ),
    );
    const { dialog, served } = await openPanel(user, UNO);
    served.set({
      ...NORTE_ORDER,
      entries: NORTE_ORDER.entries.map((entry, index) =>
        index === 0 ? { ...entry, powderKg: 1, version: 9 } : entry,
      ),
    });

    await save(user, dialog);

    expect(await within(dialog).findByText(/Otra persona ha cambiado la línea/)).toBeInTheDocument();
    await waitFor(() => {
      expect(within(dialog).getByRole('radio', { name: '1 kg' })).toBeChecked();
    });
  });

  it('closes the panel and shows the order read-only once the orders are closed', async () => {
    const user = userEvent.setup();
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () =>
        problem(409, 'orders.closed'),
      ),
    );
    const { dialog, served } = await openPanel(user, UNO);
    served.set({
      ...NORTE_ORDER,
      canEdit: false,
      readOnlyReason: 'ordersClosed',
      edition: { ...NORTE_ORDER.edition, ordersOpen: false },
    });

    await save(user, dialog);

    expect(
      await screen.findByText('Los pedidos están cerrados: ya no se puede modificar.'),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(screen.queryByRole('button', { name: /^Editar/ })).not.toBeInTheDocument();
  });

  it('closes the panel and shows the entry read-only once the person was erased (spec: Erased entries)', async () => {
    const user = userEvent.setup();
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () =>
        problem(409, 'orders.entryErased'),
      ),
    );
    const { dialog, served } = await openPanel(user, UNO);
    served.set({
      ...NORTE_ORDER,
      entries: NORTE_ORDER.entries.map((entry) =>
        personName(entry.arquebusier) === UNO
          ? {
              ...entry,
              erased: true,
              arquebusier: {
                ...entry.arquebusier,
                firstName: null,
                lastName: null,
                nationalId: null,
                federationId: null,
              },
            }
          : entry,
      ),
    });

    await save(user, dialog);

    expect(
      await screen.findByText('Esta línea es de una persona borrada: ya no se puede cambiar.'),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const erased = screen.getByRole('rowheader', { name: 'Persona borrada' }).closest('tr');
    if (!erased) throw new Error('The rowheader is in a row.');
    expect(within(erased).queryByRole('button', { name: /^Editar/ })).not.toBeInTheDocument();
  });

  it('explains that saving a submitted order sends it back to draft', async () => {
    const user = userEvent.setup();
    const submitted = { ...NORTE_ORDER, status: 'SUBMITTED' as const };
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () =>
        HttpResponse.json({ ...NORTE_ORDER, status: 'DRAFT', version: 14 }),
      ),
    );
    const { dialog } = await openPanel(user, UNO, submitted);
    expect(within(dialog).getByText(/al guardar vuelve a borrador/)).toBeInTheDocument();

    await user.click(within(dialog).getByRole('radio', { name: '1 kg' }));
    await save(user, dialog);

    expect(await screen.findAllByText(/El pedido ha vuelto a borrador/)).not.toHaveLength(0);
  });

  it('opens as a bottom sheet on a phone', async () => {
    setViewportWidth(360);
    const user = userEvent.setup();
    const { dialog } = await openPanel(user, UNO);

    expect(dialog).toHaveAttribute('data-side', 'bottom');
  });

  it('has no accessibility violations', async () => {
    const user = userEvent.setup();
    await openPanel(user, CINCO);

    // Once, on the open dialog: axe over the whole page is slow under load.
    expect(await axeViolations(document.body)).toEqual([]);
  }, 20_000);
});

describe('LoanFields (spec: Weapon loans (UC-13, BR-09), Lender lookup (UC-13, BR-12))', () => {
  async function chooseNewLoan(user: UserEvent) {
    const { app, dialog } = await openPanel(user, CINCO);
    await user.click(within(dialog).getByRole('button', { name: 'Cambiar el propietario' }));
    return { app, dialog };
  }

  it('keeps the current loan until the owner is changed', async () => {
    const user = userEvent.setup();
    const { dialog } = await openPanel(user, CINCO);

    expect(
      within(dialog).getByText('Cesión de Sintética Seis, Arcabucera (Comparsa Sintética Sur)'),
    ).toBeInTheDocument();
  });

  it('checks the DNI/NIE before looking it up', async () => {
    const user = userEvent.setup();
    let asked = false;
    server.use(
      mock.post('/api/comparsa-orders/lender-lookup', () => {
        asked = true;
        return HttpResponse.json({ registered: false, lender: null });
      }),
    );
    const { dialog } = await chooseNewLoan(user);

    await user.type(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }), '00000092A');
    await user.click(within(dialog).getByRole('button', { name: 'Buscar propietario' }));

    await waitFor(() => {
      expect(
        within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }),
      ).toHaveAccessibleDescription(/La letra del DNI\/NIE no corresponde al número/);
    });
    expect(asked).toBe(false);
  });

  it('offers a registered owner’s weapons, sending the DNI/NIE in the body only', async () => {
    const user = userEvent.setup();
    const lookups = recordBodies(() =>
      HttpResponse.json({
        registered: true,
        lender: {
          firstName: 'Arcabucera',
          lastName: 'Sintética Seis',
          comparsaName: 'Comparsa Sintética Sur',
          weapons: [{ id: 'w-3', weaponModel: ARCABUZ, weaponNumber: '1003' }],
        },
      }),
    );
    const saves = recordBodies(() => HttpResponse.json(NORTE_ORDER));
    server.use(
      mock.post('/api/comparsa-orders/lender-lookup', lookups.resolver),
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, saves.resolver),
    );
    const { app, dialog } = await chooseNewLoan(user);

    await user.type(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }), '00000006-y');
    await user.click(within(dialog).getByRole('button', { name: 'Buscar propietario' }));
    expect(await within(dialog).findByRole('status', { name: '' })).toBeDefined();
    expect(
      await within(dialog).findByText('Encontrado: Sintética Seis, Arcabucera, de Comparsa Sintética Sur.'),
    ).toBeInTheDocument();
    await user.click(await within(dialog).findByRole('radio', { name: 'ARCABUZ MORO DIESTRO 1003' }));
    await save(user, dialog);
    expect(await screen.findAllByText('Cambios guardados')).not.toHaveLength(0);

    await waitFor(() => {
      expect(saves.bodies).toHaveLength(1);
    });
    expect(lookups.bodies).toEqual([{ nationalId: '00000006Y' }]);
    expect(saves.bodies[0]).toMatchObject({
      weaponSource: 'LOAN',
      loan: { ownedWeaponId: 'w-3', external: null },
    });
    expect(app.location()).not.toContain('00000006');
  });

  it('asks for the external owner and weapon when no arquebusier has the DNI/NIE', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/comparsa-orders/lender-lookup', () =>
        HttpResponse.json({ registered: false, lender: null }),
      ),
    );
    const { dialog } = await chooseNewLoan(user);

    await user.type(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }), '00000092T');
    await user.click(within(dialog).getByRole('button', { name: 'Buscar propietario' }));

    for (const field of [
      'Nombre del propietario',
      'Apellidos del propietario',
      'Número del arma',
      'Número de la guía de pertenencia',
    ]) {
      expect(await within(dialog).findByRole('textbox', { name: field })).toBeInTheDocument();
    }
    expect(within(dialog).getByRole('combobox', { name: 'Modelo del arma' })).toBeInTheDocument();
  });

  it('says when there were too many lookups', async () => {
    const user = userEvent.setup();
    server.use(mock.post('/api/comparsa-orders/lender-lookup', () => problem(429, 'rateLimited')));
    const { dialog } = await chooseNewLoan(user);

    await user.type(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }), '00000092T');
    await user.click(within(dialog).getByRole('button', { name: 'Buscar propietario' }));

    expect(await within(dialog).findByText(/Has hecho demasiadas búsquedas/)).toBeInTheDocument();
  });

  it('shows the server’s lenderRegistered and ownWeapon reasons at the fields', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/comparsa-orders/lender-lookup', () =>
        HttpResponse.json({ registered: false, lender: null }),
      ),
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () =>
        problem(400, 'validation', { errors: { 'loan.nationalId': 'lenderRegistered' } }),
      ),
    );
    const { dialog } = await chooseNewLoan(user);
    await user.type(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }), '00000092T');
    await user.click(within(dialog).getByRole('button', { name: 'Buscar propietario' }));
    await user.type(
      await within(dialog).findByRole('textbox', { name: 'Nombre del propietario' }),
      'Propietaria',
    );
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Apellidos del propietario' }),
      'Externa Sintética',
    );
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Modelo del arma' }), TRABUCO.id);
    await user.type(within(dialog).getByRole('textbox', { name: 'Número del arma' }), 'EXT-1');
    await user.type(
      within(dialog).getByRole('textbox', { name: 'Número de la guía de pertenencia' }),
      'SINT-EXT-1',
    );
    await save(user, dialog);

    await waitFor(() => {
      expect(
        within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }),
      ).toHaveAccessibleDescription(/Este DNI\/NIE es de un arcabucero registrado/);
    });
  });

  it('keeps the lookup when another source is chosen and Loan again, and forgets it when the DNI/NIE changes', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post('/api/comparsa-orders/lender-lookup', () =>
        HttpResponse.json({
          registered: true,
          lender: {
            firstName: 'Arcabucera',
            lastName: 'Sintética Seis',
            comparsaName: 'Comparsa Sintética Sur',
            weapons: [{ id: 'w-3', weaponModel: ARCABUZ, weaponNumber: '1003' }],
          },
        }),
      ),
    );
    const { dialog } = await chooseNewLoan(user);
    const dni = within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' });
    await user.type(dni, '00000006Y{Enter}');
    await user.click(await within(dialog).findByRole('radio', { name: 'ARCABUZ MORO DIESTRO 1003' }));

    await user.click(within(dialog).getByRole('radio', { name: /^Sin arma/ }));
    await user.click(within(dialog).getByRole('radio', { name: /^Cesión/ }));
    expect(within(dialog).getByRole('radio', { name: 'ARCABUZ MORO DIESTRO 1003' })).toBeChecked();

    await user.type(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }), '1');
    expect(
      within(dialog).queryByRole('radio', { name: 'ARCABUZ MORO DIESTRO 1003' }),
    ).not.toBeInTheDocument();
  });

  it('asks to look up the owner at the DNI/NIE when saving a new loan without a lookup', async () => {
    const user = userEvent.setup();
    let sent = false;
    server.use(
      mock.put(`/api/comparsa-orders/${NORTE_ORDER.id}/entries/:entryId`, () => {
        sent = true;
        return HttpResponse.json(NORTE_ORDER);
      }),
    );
    const { dialog } = await chooseNewLoan(user);

    await save(user, dialog);

    await waitFor(() => {
      expect(
        within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' }),
      ).toHaveAccessibleDescription(/Busca primero el propietario/);
    });
    expect(sent).toBe(false);
  });

  it('moves focus to the DNI/NIE after "Change the owner"', async () => {
    const user = userEvent.setup();
    const { dialog } = await chooseNewLoan(user);

    await waitFor(() => {
      expect(within(dialog).getByRole('textbox', { name: 'DNI/NIE del propietario' })).toHaveFocus();
    });
  });
});
