import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse, FiringChiefResponse } from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';
import { asFiringChief, CHIEF_BAJA, CHIEF_DOS, CHIEF_UNO, NORTE, OESTE } from '../test-data';

/** The comparsa page's calls, with its FiringChiefs held in `chiefs` (changed by the handlers). */
function comparsaWithChiefs(comparsa: ComparsaResponse, initial: FiringChiefResponse[]) {
  const state = { chiefs: initial };
  server.use(
    mock.get(`/api/comparsas/${comparsa.id}`, () => HttpResponse.json(comparsa)),
    mock.get(`/api/comparsas/${comparsa.id}/firing-chiefs`, () => HttpResponse.json(state.chiefs)),
    mock.get('/api/users', () => HttpResponse.json([CHIEF_UNO, CHIEF_DOS, CHIEF_BAJA])),
  );
  return state;
}

async function section() {
  return screen.findByRole('group', { name: 'Jefes de disparo' });
}

/** The table row that contains `text`. */
function rowOf(container: HTMLElement, text: string): HTMLElement {
  const row = within(container).getByText(text).closest('tr');
  if (!row) throw new Error(`No row for ${text}`);
  return row;
}

describe('FiringChiefs section of a comparsa (spec: Managing assignments from the comparsa and from the user)', () => {
  it('lists the FiringChiefs with email and status', async () => {
    comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO), asFiringChief(CHIEF_DOS)]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const table = await within(await section()).findByRole('table', { name: 'Jefes de disparo' });
    await within(table).findByText('Jefe Sintético Uno');
    const uno = rowOf(table, 'Jefe Sintético Uno');
    expect(within(uno).getByText('jefe.uno@polvorapp.example')).toBeInTheDocument();
    expect(within(uno).getByText('Activo')).toBeInTheDocument();
    expect(rowOf(table, 'Jefa Sintética Dos')).toHaveTextContent('Invitado');
  });

  it('offers only FiringChiefs who are neither deactivated nor already assigned', async () => {
    comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO)]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    const choice = await within(await section()).findByRole('combobox', {
      name: 'Jefe de disparo que añadir',
    });
    await waitFor(() => {
      expect(
        within(choice)
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Elige una opción.', 'Jefa Sintética Dos (jefa.dos@polvorapp.example)']);
    });
  });

  it('adds a FiringChief and announces it with focus', async () => {
    const user = userEvent.setup();
    const state = comparsaWithChiefs(NORTE, []);
    const assigned: string[] = [];
    server.use(
      mock.put(`/api/comparsas/${NORTE.id}/firing-chiefs/:userId`, ({ params }) => {
        assigned.push(String(params.userId));
        state.chiefs = [asFiringChief(CHIEF_DOS)];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();
    await within(group).findByRole('option', { name: /Jefa Sintética Dos/ });

    await user.selectOptions(
      within(group).getByRole('combobox', { name: 'Jefe de disparo que añadir' }),
      CHIEF_DOS.id,
    );
    await user.click(within(group).getByRole('button', { name: 'Añadir' }));

    const notice = (
      await within(group).findByText('Jefa Sintética Dos ya es jefe de disparo de esta comparsa.')
    ).closest('[data-severity]');
    await waitFor(() => {
      expect(notice).toHaveFocus();
    });
    expect(assigned).toEqual([CHIEF_DOS.id]);
    expect(await within(group).findByRole('table', { name: 'Jefes de disparo' })).toHaveTextContent(
      'Jefa Sintética Dos',
    );
  });

  it('removes a FiringChief after confirmation, keeping focus on the outcome', async () => {
    const user = userEvent.setup();
    const state = comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO)]);
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/firing-chiefs/${CHIEF_UNO.id}`, () => {
        state.chiefs = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();

    await user.click(await within(group).findByRole('button', { name: 'Quitar a Jefe Sintético Uno' }));
    const dialog = await screen.findByRole('alertdialog', {
      name: '¿Quitar a Jefe Sintético Uno de esta comparsa?',
    });
    await user.click(within(dialog).getByRole('button', { name: 'Quitar a Jefe Sintético Uno' }));

    const notice = (
      await within(group).findByText('Jefe Sintético Uno ya no es jefe de disparo de esta comparsa.')
    ).closest('[data-severity]');
    await waitFor(() => {
      expect(notice).toHaveFocus();
    });
    expect(
      await within(group).findByText('Todavía no hay ningún jefe de disparo asignado.'),
    ).toBeInTheDocument();
  });

  it('keeps the confirmation open while a slow removal runs, then announces it', async () => {
    const user = userEvent.setup();
    const state = comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO), asFiringChief(CHIEF_DOS)]);
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/firing-chiefs/${CHIEF_UNO.id}`, async () => {
        await delay(300);
        state.chiefs = [asFiringChief(CHIEF_DOS)];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();

    await user.click(await within(group).findByRole('button', { name: 'Quitar a Jefe Sintético Uno' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Quitar a Jefe Sintético Uno' }));
    await delay(150);

    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    const notice = (
      await within(group).findByText('Jefe Sintético Uno ya no es jefe de disparo de esta comparsa.')
    ).closest('[data-severity]');
    await waitFor(() => {
      expect(notice).toHaveFocus();
    });
    await waitFor(() => {
      expect(within(group).queryByText('Jefe Sintético Uno')).not.toBeInTheDocument();
    });
  });

  it('keeps a rejected removal in the dialog with its reason', async () => {
    const user = userEvent.setup();
    comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO)]);
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/firing-chiefs/${CHIEF_UNO.id}`, () =>
        problem(404, 'comparsas.notFound'),
      ),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(
      await within(await section()).findByRole('button', { name: 'Quitar a Jefe Sintético Uno' }),
    );
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Quitar a Jefe Sintético Uno' }));

    expect(await within(dialog).findByText('Esta comparsa no existe o no puedes verla.')).toBeInTheDocument();
  });

  it('shows an error instead of an empty list or no candidates when loading fails', async () => {
    comparsaWithChiefs(NORTE, []);
    server.use(mock.get('/api/users', () => problem(500, 'unexpected')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();

    expect(await within(group).findByText('Algo ha fallado. Inténtalo de nuevo.')).toBeInTheDocument();
    expect(
      within(group).queryByText('Todos los jefes de disparo que se pueden asignar ya están aquí.'),
    ).not.toBeInTheDocument();
    expect(within(group).queryByRole('combobox')).not.toBeInTheDocument();
  });

  it("shows an error instead of 'none assigned' when the FiringChiefs cannot be loaded, and still offers deletion", async () => {
    comparsaWithChiefs(NORTE, []);
    server.use(mock.get(`/api/comparsas/${NORTE.id}/firing-chiefs`, () => problem(500, 'unexpected')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();

    expect(await within(group).findByText('Algo ha fallado. Inténtalo de nuevo.')).toBeInTheDocument();
    expect(
      within(group).queryByText('Todavía no hay ningún jefe de disparo asignado.'),
    ).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eliminar comparsa' })).toBeInTheDocument();
  });

  it('shows why an assignment was rejected and refreshes the list', async () => {
    const user = userEvent.setup();
    const state = comparsaWithChiefs(NORTE, []);
    let listed = 0;
    server.use(
      mock.put(`/api/comparsas/${NORTE.id}/firing-chiefs/:userId`, () =>
        problem(409, 'assignments.userDeactivated'),
      ),
      mock.get(`/api/comparsas/${NORTE.id}/firing-chiefs`, () => {
        listed += 1;
        return HttpResponse.json(state.chiefs);
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();
    await within(group).findByRole('option', { name: /Jefe Sintético Uno/ });
    const before = listed;

    await user.selectOptions(
      within(group).getByRole('combobox', { name: 'Jefe de disparo que añadir' }),
      CHIEF_UNO.id,
    );
    await user.click(within(group).getByRole('button', { name: 'Añadir' }));

    expect(
      await within(group).findByText('Ese usuario está desactivado y no se puede asignar.'),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(listed).toBeGreaterThan(before);
    });
  });

  it('asks to choose someone before adding', async () => {
    const user = userEvent.setup();
    comparsaWithChiefs(NORTE, []);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();
    await within(group).findByRole('option', { name: /Jefe Sintético Uno/ });

    await user.click(within(group).getByRole('button', { name: 'Añadir' }));

    expect(
      await within(group).findByText('Elige una opción.', { selector: '[data-slot="form-message"], p' }),
    ).toBeInTheDocument();
  });

  it('offers no new FiringChiefs on an inactive comparsa and says why', async () => {
    comparsaWithChiefs(OESTE, [asFiringChief(CHIEF_UNO)]);
    await renderApp(`/comparsas/${OESTE.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();

    expect(
      await within(group).findByText('Reactiva la comparsa para asignar nuevos jefes de disparo.'),
    ).toBeInTheDocument();
    expect(within(group).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(group).getByRole('button', { name: 'Quitar a Jefe Sintético Uno' })).toBeInTheDocument();
  });

  it('says so when every assignable FiringChief is already there', async () => {
    comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO), asFiringChief(CHIEF_DOS)]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    expect(
      await within(await section()).findByText(
        'Todos los jefes de disparo que se pueden asignar ya están aquí.',
      ),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    comparsaWithChiefs(NORTE, [asFiringChief(CHIEF_UNO)]);
    const { container } = await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await within(await section()).findByText('Jefe Sintético Uno');

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});
