import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';

const PATH = '/api/account/notification-preferences';

function kinds(enabled: Record<string, boolean>) {
  return { kinds: Object.entries(enabled).map(([kind, on]) => ({ kind, enabled: on })) };
}

const CHIEF_KINDS = {
  LICENSE_DIGEST: true,
  ORDER_WINDOW: true,
  ORDER_STATUS: true,
  MILESTONE_REMINDER: true,
};

async function section() {
  return screen.findByRole('region', { name: 'Avisos por correo' });
}

describe('NotificationsSection on the account page (spec: Notification screens)', () => {
  it('lists the kinds of a FiringChief with their descriptions and state as text', async () => {
    server.use(mock.get(PATH, () => HttpResponse.json(kinds({ ...CHIEF_KINDS, ORDER_WINDOW: false }))));
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    const region = await section();
    await waitFor(() => {
      expect(region).toHaveTextContent('Resumen mensual de licencias');
    });
    expect(region).toHaveTextContent('Apertura y cierre de pedidos');
    expect(region).toHaveTextContent('Estado de los pedidos');
    expect(region).toHaveTextContent('Recordatorios del calendario');
    expect(region).toHaveTextContent('El día 1 de cada mes');
    expect(within(region).getAllByText('Activado')).toHaveLength(3);
    expect(within(region).getAllByText('Desactivado')).toHaveLength(1);
  });

  it('lists the kinds the server returns for an Admin', async () => {
    server.use(
      mock.get(PATH, () => HttpResponse.json(kinds({ ORDER_STATUS: true, MILESTONE_REMINDER: true }))),
    );
    await renderApp('/account', { session: SYNTHETIC_ADMIN });

    const region = await section();
    await waitFor(() => {
      expect(region).toHaveTextContent('Estado de los pedidos');
    });
    expect(region).not.toHaveTextContent('Resumen mensual de licencias');
    expect(region).toHaveTextContent('Recordatorios del calendario');
  });

  it('turns a kind off in the panel, saves and announces it', async () => {
    const user = userEvent.setup();
    const save = recordBodies(() => HttpResponse.json(kinds({ ...CHIEF_KINDS, LICENSE_DIGEST: false })));
    server.use(mock.put(PATH, save.resolver));
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Editar avisos por correo' }));
    const panel = await screen.findByRole('dialog');
    await user.click(within(panel).getByRole('checkbox', { name: 'Resumen mensual de licencias' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(screen.getByText('Cambios guardados', { selector: '[role=status]' })).toBeInTheDocument();
    });
    expect(save.bodies).toEqual([
      kinds({ LICENSE_DIGEST: false, ORDER_WINDOW: true, ORDER_STATUS: true, MILESTONE_REMINDER: true }),
    ]);
    await waitFor(() => {
      expect(
        within(screen.getByRole('region', { name: 'Avisos por correo' })).getAllByText('Desactivado'),
      ).toHaveLength(1);
    });
  });

  it('keeps the panel and its values with the reason when saving fails', async () => {
    const user = userEvent.setup();
    server.use(mock.put(PATH, () => problem(503, 'storage.unavailable')));
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Editar avisos por correo' }));
    const panel = await screen.findByRole('dialog');
    const digest = within(panel).getByRole('checkbox', { name: 'Resumen mensual de licencias' });
    await user.click(digest);
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(within(panel).getByRole('checkbox', { name: 'Resumen mensual de licencias' })).not.toBeChecked();
    expect(
      within(screen.getByRole('region', { name: 'Avisos por correo', hidden: true })).getAllByText(
        'Activado',
      ),
    ).toHaveLength(4);
  });

  it('says when the preferences cannot be loaded, offers a retry, and the rest of the page works', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      mock.get(PATH, () => {
        calls += 1;
        return calls === 1 ? problem(500, 'internal') : HttpResponse.json(kinds(CHIEF_KINDS));
      }),
    );
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    const region = await section();
    expect(await within(region).findByText(/No se han podido cargar tus avisos/)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Cambiar la contraseña' })).toBeInTheDocument();
    await user.click(within(region).getByRole('button', { name: 'Reintentar' }));

    await waitFor(() => {
      expect(region).toHaveTextContent('Resumen mensual de licencias');
    });
  });

  it('focuses the section from an email link even when the preferences cannot be loaded', async () => {
    server.use(mock.get(PATH, () => problem(500, 'internal')));
    await renderApp('/account?section=notifications', { session: SYNTHETIC_FIRING_CHIEF });

    const region = await section();
    expect(await within(region).findByText(/No se han podido cargar tus avisos/)).toBeInTheDocument();
    await waitFor(() => {
      expect(region).toHaveFocus();
    });
  });

  it('explains a kind that no longer applies and shows the current kinds', async () => {
    const user = userEvent.setup();
    let calls = 0;
    server.use(
      mock.get(PATH, () => {
        calls += 1;
        return HttpResponse.json(
          kinds(calls === 1 ? CHIEF_KINDS : { ORDER_STATUS: true, MILESTONE_REMINDER: true }),
        );
      }),
      mock.put(PATH, () => problem(400, 'validation', { errors: { 'kinds[0].kind': 'notApplicable' } })),
    );
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    await user.click(await screen.findByRole('button', { name: 'Editar avisos por correo' }));
    const panel = await screen.findByRole('dialog');
    await user.click(within(panel).getByRole('checkbox', { name: 'Resumen mensual de licencias' }));
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByText('Este aviso no se aplica a tu rol.')).toBeInTheDocument();
    await waitFor(() => {
      expect(calls).toBe(2);
    });
  });

  it('opens at the section and focuses it from an email link', async () => {
    await renderApp('/account?section=notifications', { session: SYNTHETIC_FIRING_CHIEF });

    const region = await section();
    await waitFor(() => {
      expect(region).toHaveFocus();
    });
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });
    const region = await section();
    await waitFor(() => {
      expect(region).toHaveTextContent('Resumen mensual de licencias');
    });

    expect(await axeViolations(container)).toEqual([]);
  });
});
