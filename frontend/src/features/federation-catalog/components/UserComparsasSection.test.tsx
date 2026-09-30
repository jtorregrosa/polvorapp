import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComparsaResponse, UserResponse } from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';
import { CHIEF_BAJA, CHIEF_UNO, NORTE, OESTE, OTRA_ADMIN, SUR } from '../test-data';

/** A user's page, with their comparsas held in `comparsas` (changed by the handlers). */
function userWithComparsas(user: UserResponse, initial: ComparsaResponse[]) {
  const state = { comparsas: initial };
  server.use(
    mock.get(`/api/users/${user.id}`, () => HttpResponse.json(user)),
    mock.get(`/api/firing-chiefs/${user.id}/comparsas`, () => HttpResponse.json(state.comparsas)),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE, SUR])),
  );
  return state;
}

async function section() {
  return screen.findByRole('group', { name: 'Comparsas' });
}

describe('Comparsas section of a user (spec: Managing assignments from the comparsa and from the user)', () => {
  it("lists a FiringChief's comparsas, active or not, and offers only active unassigned ones", async () => {
    userWithComparsas(CHIEF_UNO, [NORTE, OESTE]);
    await renderApp(`/users/${CHIEF_UNO.id}`, { session: SYNTHETIC_ADMIN });

    const group = await section();
    const table = await within(group).findByRole('table', { name: 'Comparsas' });
    expect(await within(table).findByText('Comparsa Sintética Oeste')).toBeInTheDocument();
    expect(within(table).getByText('Comparsa Sintética Norte')).toBeInTheDocument();
    const choice = within(group).getByRole('combobox', { name: 'Comparsa que añadir' });
    await waitFor(() => {
      expect(
        within(choice)
          .getAllByRole('option')
          .map((option) => option.textContent),
      ).toEqual(['Elige una opción.', 'Comparsa Sintética Sur']);
    });
  });

  it('assigns a comparsa from the user page and announces it with focus', async () => {
    const user = userEvent.setup();
    const state = userWithComparsas(CHIEF_UNO, []);
    const assigned: string[] = [];
    server.use(
      mock.put(`/api/comparsas/:id/firing-chiefs/${CHIEF_UNO.id}`, ({ params }) => {
        assigned.push(String(params.id));
        state.comparsas = [SUR];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/users/${CHIEF_UNO.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();
    await within(group).findByRole('option', { name: 'Comparsa Sintética Sur' });

    await user.selectOptions(within(group).getByRole('combobox', { name: 'Comparsa que añadir' }), SUR.id);
    await user.click(within(group).getByRole('button', { name: 'Añadir' }));

    const notice = (await within(group).findByText('Comparsa Comparsa Sintética Sur asignada.')).closest(
      '[data-severity]',
    );
    await waitFor(() => {
      expect(notice).toHaveFocus();
    });
    expect(assigned).toEqual([SUR.id]);
    expect(await within(group).findByRole('table', { name: 'Comparsas' })).toHaveTextContent(
      'Comparsa Sintética Sur',
    );
  });

  it('shows why an assignment was rejected and shows the assignments the server now holds', async () => {
    const user = userEvent.setup();
    const state = userWithComparsas(CHIEF_UNO, []);
    server.use(
      mock.put(`/api/comparsas/:id/firing-chiefs/${CHIEF_UNO.id}`, () => {
        // Meanwhile someone else assigned Norte; this request was for an inactive comparsa.
        state.comparsas = [NORTE];
        return problem(409, 'assignments.comparsaInactive');
      }),
    );
    await renderApp(`/users/${CHIEF_UNO.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();
    await within(group).findByRole('option', { name: 'Comparsa Sintética Sur' });

    await user.selectOptions(within(group).getByRole('combobox', { name: 'Comparsa que añadir' }), SUR.id);
    await user.click(within(group).getByRole('button', { name: 'Añadir' }));

    expect(
      await within(group).findByText('La comparsa está inactiva y no admite nuevos jefes de disparo.'),
    ).toBeInTheDocument();
    expect(await within(group).findByRole('table', { name: 'Comparsas' })).toHaveTextContent(
      'Comparsa Sintética Norte',
    );
  });

  it('removes a comparsa after confirmation', async () => {
    const user = userEvent.setup();
    const state = userWithComparsas(CHIEF_UNO, [NORTE]);
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/firing-chiefs/${CHIEF_UNO.id}`, () => {
        state.comparsas = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/users/${CHIEF_UNO.id}`, { session: SYNTHETIC_ADMIN });
    const group = await section();

    await user.click(await within(group).findByRole('button', { name: 'Quitar Comparsa Sintética Norte' }));
    await user.click(
      within(
        await screen.findByRole('alertdialog', { name: '¿Quitar la comparsa Comparsa Sintética Norte?' }),
      ).getByRole('button', {
        name: 'Quitar Comparsa Sintética Norte',
      }),
    );

    expect(
      await within(group).findByText('Este jefe de disparo todavía no tiene ninguna comparsa.'),
    ).toBeInTheDocument();
  });

  it('tells that the kept assignments of an Admin have no effect and offers none', async () => {
    userWithComparsas(OTRA_ADMIN, [NORTE]);
    await renderApp(`/users/${OTRA_ADMIN.id}`, { session: SYNTHETIC_ADMIN });

    const group = await section();
    expect(
      await within(group).findByText(/Este usuario es administrador y ve todas las comparsas/),
    ).toBeInTheDocument();
    expect(within(group).queryByRole('combobox')).not.toBeInTheDocument();
    expect(
      await within(group).findByRole('button', { name: 'Quitar Comparsa Sintética Norte' }),
    ).toBeInTheDocument();
  });

  it('is not shown for an Admin without assignments', async () => {
    userWithComparsas(OTRA_ADMIN, []);
    await renderApp(`/users/${OTRA_ADMIN.id}`, { session: SYNTHETIC_ADMIN });

    await screen.findByRole('heading', { level: 1, name: OTRA_ADMIN.name });
    await waitFor(() => {
      expect(screen.queryByRole('group', { name: 'Comparsas' })).not.toBeInTheDocument();
    });
  });

  it('offers no comparsa to a deactivated FiringChief and says why', async () => {
    userWithComparsas(CHIEF_BAJA, []);
    await renderApp(`/users/${CHIEF_BAJA.id}`, { session: SYNTHETIC_ADMIN });

    const group = await section();
    expect(
      await within(group).findByText('Ese usuario está desactivado y no se puede asignar.'),
    ).toBeInTheDocument();
    expect(within(group).queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    userWithComparsas(CHIEF_UNO, [NORTE]);
    const { container } = await renderApp(`/users/${CHIEF_UNO.id}`, { session: SYNTHETIC_ADMIN });
    await within(await section()).findByText('Comparsa Sintética Norte');

    await waitFor(async () => {
      expect(await axeViolations(container)).toEqual([]);
    });
  });
});
