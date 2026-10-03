import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, OESTE, SUR } from '../test-data';

// Specs "First year (UC-07)" and "Deleting an arquebusier (UC-05, BR-14)" on the detail page
// (add-comparsa-orders). Synthetic data only.

function detail(arquebusier: ArquebusierResponse) {
  server.use(
    mock.get(`/api/arquebusiers/${arquebusier.id}`, () => HttpResponse.json(arquebusier)),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR, OESTE])),
    mock.get('/api/arquebusiers', () => HttpResponse.json([])),
  );
}

async function openDetail(arquebusier: ArquebusierResponse) {
  detail(arquebusier);
  await renderApp(`/arquebusiers/${arquebusier.id}`, { session: SYNTHETIC_FIRING_CHIEF });
  await screen.findByRole('heading', { level: 1, name: 'Arcabucero García Sintético' });
}

async function askToDelete(user: UserEvent) {
  await user.click(await screen.findByRole('button', { name: 'Más acciones' }));
  const menu = await screen.findByRole('menu');
  await user.click(within(menu).getByRole('menuitem', { name: 'Eliminar arcabucero' }));
  return screen.findByRole('alertdialog');
}

const IMPACT = {
  currentEntry: {
    editionYear: 2031,
    comparsaId: NORTE.id,
    comparsaName: 'Comparsa Sintética Norte',
    orderStatus: 'SUBMITTED',
    willBeRemoved: true,
  },
  lentWeapons: 1,
  hasPastEntries: true,
} as const;

describe('First year on the arquebusier detail (spec: First year (UC-07))', () => {
  it('shows "First year" when the arquebusier is in their first year', async () => {
    await openDetail({ ...DETAIL_UNO, firstYear: true });

    expect(screen.getByText('Primer año')).toBeInTheDocument();
  });

  it.each([[false], [null]])('shows nothing when the flag is %s', async (firstYear) => {
    await openDetail({ ...DETAIL_UNO, firstYear });

    expect(screen.queryByText('Primer año')).not.toBeInTheDocument();
  });
});

describe('Deletion impact (spec: Deleting an arquebusier (UC-05, BR-14))', () => {
  it('warns that the entry will be deleted from the open order, naming the edition, comparsa and status', async () => {
    const user = userEvent.setup();
    await openDetail({ ...DETAIL_UNO, deletionImpact: IMPACT });

    const dialog = await askToDelete(user);

    expect(dialog).toHaveAccessibleDescription(
      /Su línea se eliminará del pedido de 2031 de Comparsa Sintética Norte, que está enviado\..*No se puede deshacer/,
    );
    expect(within(dialog).getByText(/1 arma suya está cedida en esta edición/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Sus líneas de ediciones anteriores se conservan/)).toBeInTheDocument();
  });

  it('says the entry stays as history when the orders are closed', async () => {
    const user = userEvent.setup();
    await openDetail({
      ...DETAIL_UNO,
      deletionImpact: {
        ...IMPACT,
        currentEntry: { ...IMPACT.currentEntry, willBeRemoved: false },
        lentWeapons: 0,
        hasPastEntries: false,
      },
    });

    const dialog = await askToDelete(user);

    expect(
      within(dialog).getByText(
        /Su línea del pedido de 2031 de Comparsa Sintética Norte se conserva como histórico/,
      ),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText(/cedida/)).not.toBeInTheDocument();
    expect(within(dialog).queryByText(/ediciones anteriores/)).not.toBeInTheDocument();
  });

  it('adds nothing when the deletion does not affect any order', async () => {
    const user = userEvent.setup();
    await openDetail(DETAIL_UNO);

    const dialog = await askToDelete(user);

    expect(within(dialog).queryByText(/pedido/)).not.toBeInTheDocument();
  });

  it('changes nothing when cancelled', async () => {
    const user = userEvent.setup();
    let deleted = false;
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await openDetail({ ...DETAIL_UNO, deletionImpact: IMPACT });

    const dialog = await askToDelete(user);
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(deleted).toBe(false);
  });

  it('has no accessibility violations with the impact shown', async () => {
    const user = userEvent.setup();
    await openDetail({ ...DETAIL_UNO, firstYear: true, deletionImpact: IMPACT });

    await askToDelete(user);

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
