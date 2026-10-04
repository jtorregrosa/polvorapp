import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { EditionResponse } from '@/api/generated/model';
import { recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { CATALOGUE, CURRENT_2031 } from '../test-data';

function milestone(index: number) {
  const found = CURRENT_2031.milestones[index];
  if (!found) throw new Error(`The test edition has no milestone ${String(index)}.`);
  return found;
}

/** The first milestone of the test edition has `notify` on, the second off. */
const REMINDED = milestone(0);
const PLAIN = milestone(1);

function serve(edition: EditionResponse = CURRENT_2031) {
  server.use(
    mock.get(`/api/editions/${edition.id}`, () => HttpResponse.json(edition)),
    mock.get('/api/weapon-models', () => HttpResponse.json(CATALOGUE)),
  );
}

describe('Milestone reminders on the edition page (specs: Calendar milestones, Editions screens)', () => {
  it('marks the milestones reminded by email with text', async () => {
    serve();
    await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Hitos del calendario' });
    expect(within(table).getByRole('columnheader', { name: 'Recordatorio por correo' })).toBeInTheDocument();
    const reminded = within(table).getByRole('row', { name: new RegExp(REMINDED.title) });
    expect(within(reminded).getByRole('cell', { name: 'Sí' })).toBeInTheDocument();
    expect(within(reminded).getByRole('rowheader')).toHaveTextContent(new RegExp(`^${REMINDED.title}$`));
    const plain = within(table).getByRole('row', { name: new RegExp(PLAIN.title) });
    expect(within(plain).getByRole('cell', { name: 'No' })).toBeInTheDocument();
  });

  it('adds a milestone with a reminder, explaining who is reminded and when', async () => {
    const user = userEvent.setup();
    serve();
    const added = recordBodies(() =>
      HttpResponse.json(
        {
          id: '00000000-0000-4000-8000-000000000799',
          date: '2031-03-15',
          title: 'Curso sintético',
          notify: true,
        },
        { status: 201 },
      ),
    );
    server.use(mock.post(`/api/editions/${CURRENT_2031.id}/milestones`, added.resolver));
    await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Añadir hito' }));
    const panel = await screen.findByRole('dialog', { name: 'Nuevo hito' });
    const reminder = within(panel).getByRole('checkbox', { name: 'Enviar un recordatorio por correo' });
    expect(reminder).not.toBeChecked();
    expect(panel).toHaveTextContent(
      'Una semana antes, a los Admins y, con la edición en curso, a los jefes de disparo.',
    );
    await user.type(within(panel).getByRole('textbox', { name: 'Título' }), 'Curso sintético');
    fireEvent.change(within(panel).getByLabelText('Fecha'), { target: { value: '2031-03-15' } });
    await user.click(reminder);
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(added.bodies).toEqual([{ date: '2031-03-15', title: 'Curso sintético', notify: true }]);
    });
  });

  it('turns a reminder off when editing a milestone', async () => {
    const user = userEvent.setup();
    serve();
    const edited = recordBodies(() => HttpResponse.json({ ...REMINDED, notify: false }));
    server.use(mock.put(`/api/editions/${CURRENT_2031.id}/milestones/${REMINDED.id}`, edited.resolver));
    await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_ADMIN });

    const table = await screen.findByRole('table', { name: 'Hitos del calendario' });
    await user.click(within(table).getByRole('button', { name: `Editar el hito ${REMINDED.title}` }));
    const panel = await screen.findByRole('dialog', { name: 'Editar hito' });
    const reminder = within(panel).getByRole('checkbox', { name: 'Enviar un recordatorio por correo' });
    expect(reminder).toBeChecked();
    await user.click(reminder);
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(edited.bodies).toEqual([{ date: REMINDED.date, title: REMINDED.title, notify: false }]);
    });
  });

  it('shows a FiringChief the reminder mark read-only', async () => {
    serve();
    await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const table = await screen.findByRole('table', { name: 'Hitos del calendario' });
    const row = within(table).getByRole('row', { name: new RegExp(REMINDED.title) });
    expect(within(row).getByRole('cell', { name: 'Sí' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Añadir hito' })).not.toBeInTheDocument();
  });

  it.each([
    ['openOrders', { ...CURRENT_2031, ordersOpen: false }, 'Abrir pedidos'],
    ['closeOrders', CURRENT_2031, 'Cerrar pedidos'],
  ])('says in the %s dialog that FiringChiefs are told by email', async (_, edition, action) => {
    const user = userEvent.setup();
    serve(edition);
    await renderApp(`/editions/${edition.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: action }));

    expect(await screen.findByRole('alertdialog')).toHaveTextContent('Se les avisará por correo.');
  });

  it('has no accessibility violations', async () => {
    serve();
    const { container } = await renderApp(`/editions/${CURRENT_2031.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('table', { name: 'Hitos del calendario' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
