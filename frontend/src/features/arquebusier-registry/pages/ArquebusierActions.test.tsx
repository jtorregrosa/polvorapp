import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierResponse, ComparsaResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, OESTE, SUR } from '../test-data';

function detail(
  current: () => ArquebusierResponse = () => DETAIL_UNO,
  comparsas: ComparsaResponse[] = [NORTE, SUR, OESTE],
) {
  server.use(
    mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(current())),
    mock.get('/api/comparsas', () => HttpResponse.json(comparsas)),
    mock.get('/api/arquebusiers', () => HttpResponse.json([])),
  );
}

describe('Transfer (spec: Transfer between comparsas, Registry screens)', () => {
  it('lets an Admin move the arquebusier to another active comparsa after naming both', async () => {
    const user = userEvent.setup();
    let current = DETAIL_UNO;
    detail(() => current);
    const { bodies, resolver } = recordBodies(() => {
      current = { ...DETAIL_UNO, comparsaId: SUR.id, comparsaName: SUR.name };
      return new HttpResponse(null, { status: 204 });
    });
    server.use(mock.post(`/api/arquebusiers/${DETAIL_UNO.id}/transfer`, resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });

    const target = await screen.findByRole('combobox', { name: 'Comparsa de destino' });
    await screen.findByRole('option', { name: SUR.name });
    expect(within(target).queryByRole('option', { name: NORTE.name })).not.toBeInTheDocument();
    expect(within(target).queryByRole('option', { name: OESTE.name })).not.toBeInTheDocument();
    await user.selectOptions(target, SUR.id);
    await user.click(screen.getByRole('button', { name: 'Trasladar' }));
    const dialog = await screen.findByRole('alertdialog', {
      name: `¿Trasladar a Arcabucero García Sintético a ${SUR.name}?`,
    });
    expect(dialog).toHaveAccessibleDescription(new RegExp(`Pasará de ${NORTE.name} a ${SUR.name}`));
    await user.click(within(dialog).getByRole('button', { name: 'Trasladar' }));

    expect(
      await screen.findByText(`Arcabucero García Sintético pertenece ahora a ${SUR.name}.`),
    ).toBeInTheDocument();
    expect(bodies).toEqual([{ comparsaId: SUR.id }]);
  });

  it('is not offered to a FiringChief', async () => {
    detail();
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('button', { name: 'Eliminar arcabucero' });
    expect(screen.queryByRole('combobox', { name: 'Comparsa de destino' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Trasladar' })).not.toBeInTheDocument();
  });
});

describe('Deletion (spec: Deleting an arquebusier)', () => {
  it('deletes after a confirmation that names the person, says it cannot be undone and suggests Reserve', async () => {
    const user = userEvent.setup();
    detail();
    let deleted = false;
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));
    const dialog = await screen.findByRole('alertdialog', {
      name: '¿Eliminar a Arcabucero García Sintético?',
    });
    expect(dialog).toHaveAccessibleDescription(/^No se puede deshacer.*Reserva/);
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }));

    await waitFor(() => {
      expect(app.location()).toBe('/arquebusiers');
    });
    expect(deleted).toBe(true);
    const notice = await screen.findByText('Arcabucero García Sintético se ha eliminado del registro.');
    await waitFor(() => {
      expect(notice.closest('[role="status"], [role="alert"], [tabindex]')).toHaveFocus();
    });
  });

  it('treats an arquebusier someone else already deleted as deleted', async () => {
    const user = userEvent.setup();
    detail();
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => problem(404, 'arquebusiers.notFound')),
    );
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Eliminar' }),
    );

    await waitFor(() => {
      expect(app.location()).toBe('/arquebusiers');
    });
    expect(
      await screen.findByText('Arcabucero García Sintético se ha eliminado del registro.'),
    ).toBeInTheDocument();
  });

  it('changes nothing when the confirmation is cancelled', async () => {
    const user = userEvent.setup();
    detail();
    let deleted = false;
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const app = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancelar' }),
    );

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(deleted).toBe(false);
    expect(app.location()).toBe(`/arquebusiers/${DETAIL_UNO.id}`);
  });

  it('says when the comparsa is inactive and keeps the arquebusier editable', async () => {
    detail(() => ({ ...DETAIL_UNO, comparsaActive: false }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByText(/está inactiva: no admite nuevos arcabuceros/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeEnabled();
  });

  it('has no accessibility violations on the detail page', async () => {
    detail();
    const { container } = await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('option', { name: SUR.name });

    expect(await axeViolations(container)).toEqual([]);
  });
});
