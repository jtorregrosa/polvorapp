import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { AccountResponse, OrderResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE_ORDER } from '../test-data';

function serve(order: OrderResponse) {
  let current = order;
  let loads = 0;
  server.use(
    mock.get(`/api/comparsa-orders/${order.id}`, () => {
      loads += 1;
      return HttpResponse.json(current);
    }),
    mock.get(`/api/comparsas/${order.comparsa.id}`, () =>
      HttpResponse.json({ ...order.comparsa, active: true, logo: null, firingChiefs: [], version: 1 }),
    ),
  );
  return {
    set: (next: OrderResponse) => {
      current = next;
    },
    loads: () => loads,
  };
}

async function open(order: OrderResponse, session: AccountResponse) {
  const served = serve(order);
  await renderApp(`/orders/${order.id}`, { session });
  await screen.findByRole('heading', { level: 1, name: 'Pedido de Comparsa Sintética Norte' });
  return served;
}

async function dialogOf(user: UserEvent, button: string) {
  await user.click(screen.getByRole('button', { name: button }));
  return screen.findByRole('alertdialog');
}

describe('SubmitDialog (spec: Submitting an order (UC-14), Orders screens)', () => {
  it('lists the pending warnings in words and needs the attestation before submitting', async () => {
    const user = userEvent.setup();
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json({ ...NORTE_ORDER, status: 'SUBMITTED', version: 13 }),
    );
    server.use(mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/submit`, resolver));
    await open(NORTE_ORDER, SYNTHETIC_FIRING_CHIEF);

    const dialog = await dialogOf(user, 'Enviar pedido');

    expect(within(dialog).getByText(/1 arcabucero activo tiene avisos pendientes/)).toBeInTheDocument();
    expect(within(dialog).getByText('Sintético Tres, Arcabucero')).toBeInTheDocument();
    expect(within(dialog).getByText('Sin curso')).toBeInTheDocument();
    const confirm = within(dialog).getByRole('button', { name: 'Enviar' });
    expect(confirm).toBeDisabled();
    await user.click(within(dialog).getByRole('checkbox', { name: /Declaro que los arcabuceros/ }));
    expect(confirm).toBeEnabled();
    await user.click(confirm);

    await waitFor(() => {
      expect(bodies).toEqual([{ version: NORTE_ORDER.version, attestation: true }]);
    });
    expect(await screen.findByText('Pedido enviado.')).toBeInTheDocument();
  });

  it('lets an Admin submit on the comparsa’s behalf without the attestation', async () => {
    const user = userEvent.setup();
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json({ ...NORTE_ORDER, status: 'SUBMITTED' }),
    );
    server.use(mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/submit`, resolver));
    await open(NORTE_ORDER, SYNTHETIC_ADMIN);

    const dialog = await dialogOf(user, 'Enviar en nombre de la comparsa');

    expect(within(dialog).getByText(/sin la declaración del jefe de disparo/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('checkbox')).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Enviar en su nombre' }));
    await waitFor(() => {
      expect(bodies).toEqual([{ version: NORTE_ORDER.version, attestation: null }]);
    });
  });

  it('names the entries to fix and shows them in the order', async () => {
    const user = userEvent.setup();
    const first = NORTE_ORDER.entries[0];
    server.use(
      mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/submit`, () =>
        problem(409, 'orders.entriesInvalid', {
          entries: [{ entryId: first?.id, reasons: ['ownedWeaponMissing'] }],
        }),
      ),
    );
    const served = await open(NORTE_ORDER, SYNTHETIC_FIRING_CHIEF);
    served.set({
      ...NORTE_ORDER,
      entries: NORTE_ORDER.entries.map((entry) =>
        entry.id === first?.id ? { ...entry, issues: ['ownedWeaponMissing'] } : entry,
      ),
    });

    const dialog = await dialogOf(user, 'Enviar pedido');
    await user.click(within(dialog).getByRole('checkbox', { name: /Declaro/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }));

    expect(
      await within(dialog).findByText(/Hay líneas que revisar antes de seguir: Sintético Uno, Arcabucero/),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    expect(await screen.findByRole('link', { name: 'Ir a las líneas' })).toHaveAttribute('href', '#entries');
  });

  it('reloads the order when it changed meanwhile', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/submit`, () => problem(409, 'orders.modified')),
    );
    const served = await open(NORTE_ORDER, SYNTHETIC_FIRING_CHIEF);
    const before = served.loads();

    const dialog = await dialogOf(user, 'Enviar pedido');
    await user.click(within(dialog).getByRole('checkbox', { name: /Declaro/ }));
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }));

    expect(await within(dialog).findByText(/Otra persona ha cambiado el pedido/)).toBeInTheDocument();
    expect(served.loads()).toBe(before);
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => {
      expect(served.loads()).toBeGreaterThan(before);
    });
  });

  it('offers no submission once the order cannot be edited', async () => {
    await open({ ...NORTE_ORDER, canEdit: false, readOnlyReason: 'ordersClosed' }, SYNTHETIC_FIRING_CHIEF);

    expect(screen.queryByRole('button', { name: 'Enviar pedido' })).not.toBeInTheDocument();
  });
});

describe('Admin review actions (spec: Reviewing orders (UC-15))', () => {
  it.each([['DRAFT'], ['RETURNED']] as const)(
    'says when validating a %s order that the comparsa did not submit it',
    async (status) => {
      const user = userEvent.setup();
      const { bodies, resolver } = recordBodies(() =>
        HttpResponse.json({ ...NORTE_ORDER, status: 'VALIDATED' }),
      );
      server.use(mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/validate`, resolver));
      await open({ ...NORTE_ORDER, status }, SYNTHETIC_ADMIN);

      const dialog = await dialogOf(user, 'Validar');
      expect(within(dialog).getByText(/La comparsa no ha enviado este pedido/)).toBeInTheDocument();
      await user.click(within(dialog).getByRole('button', { name: 'Validar' }));

      await waitFor(() => {
        expect(bodies).toEqual([{ version: NORTE_ORDER.version }]);
      });
      expect(await screen.findByText('Pedido validado.')).toBeInTheDocument();
    },
  );

  it('needs a reason to return an order, with a counter', async () => {
    const user = userEvent.setup();
    const { bodies, resolver } = recordBodies(() =>
      HttpResponse.json({ ...NORTE_ORDER, status: 'RETURNED' }),
    );
    server.use(mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/return`, resolver));
    await open({ ...NORTE_ORDER, status: 'SUBMITTED' }, SYNTHETIC_ADMIN);

    const dialog = await dialogOf(user, 'Devolver');
    await user.click(within(dialog).getByRole('button', { name: 'Devolver' }));
    const reason = within(dialog).getByRole('textbox', { name: 'Motivo de la devolución' });
    expect(reason).toHaveAccessibleDescription(/Este campo es obligatorio/);
    expect(reason).toHaveFocus();
    expect(bodies).toEqual([]);

    await user.type(reason, 'Revisad la cantimplora.');
    expect(within(dialog).getByText('Quedan 477 caracteres')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Devolver' }));

    await waitFor(() => {
      expect(bodies).toEqual([{ version: NORTE_ORDER.version, reason: 'Revisad la cantimplora.' }]);
    });
    expect(await screen.findByText('Pedido devuelto.')).toBeInTheDocument();
  });

  it('says when the order is no longer in a state that allows the move', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/validate`, () =>
        problem(409, 'orders.invalidTransition'),
      ),
    );
    await open({ ...NORTE_ORDER, status: 'SUBMITTED' }, SYNTHETIC_ADMIN);

    const dialog = await dialogOf(user, 'Validar');
    await user.click(within(dialog).getByRole('button', { name: 'Validar' }));

    expect(
      await within(dialog).findByText('El pedido ya no está en un estado que lo permita.'),
    ).toBeInTheDocument();
  });

  it('offers return but not validation for a validated order', async () => {
    await open({ ...NORTE_ORDER, status: 'VALIDATED' }, SYNTHETIC_ADMIN);

    expect(screen.getByRole('button', { name: 'Devolver' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Validar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Enviar/ })).not.toBeInTheDocument();
  });

  it('has no accessibility violations with the return dialog open', async () => {
    const user = userEvent.setup();
    await open({ ...NORTE_ORDER, status: 'SUBMITTED' }, SYNTHETIC_ADMIN);

    await dialogOf(user, 'Devolver');

    // Once, on the open dialog: axe over the whole page is slow under load.
    expect(await axeViolations(document.body)).toEqual([]);
  }, 20_000);

  it('keeps the refusal in the dialog even when the order moved on meanwhile, and shows the new state on closing', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/validate`, () =>
        problem(409, 'orders.invalidTransition'),
      ),
    );
    const served = await open({ ...NORTE_ORDER, status: 'SUBMITTED' }, SYNTHETIC_ADMIN);
    served.set({ ...NORTE_ORDER, status: 'VALIDATED' });

    const dialog = await dialogOf(user, 'Validar');
    await user.click(within(dialog).getByRole('button', { name: 'Validar' }));
    expect(
      await within(dialog).findByText('El pedido ya no está en un estado que lo permita.'),
    ).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    expect(await screen.findByText('Validado')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Validar' })).not.toBeInTheDocument();
  });

  it('focuses the notice after a move', async () => {
    const user = userEvent.setup();
    server.use(
      mock.post(`/api/comparsa-orders/${NORTE_ORDER.id}/validate`, () =>
        HttpResponse.json({ ...NORTE_ORDER, status: 'VALIDATED' }),
      ),
    );
    await open({ ...NORTE_ORDER, status: 'SUBMITTED' }, SYNTHETIC_ADMIN);

    const dialog = await dialogOf(user, 'Validar');
    await user.click(within(dialog).getByRole('button', { name: 'Validar' }));

    const notice = await screen.findByText('Pedido validado.');
    await waitFor(() => {
      expect(notice.closest('[role="status"], [role="alert"], [tabindex]')).toHaveFocus();
    });
  });
});
