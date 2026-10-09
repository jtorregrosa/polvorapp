import 'fake-indexeddb/auto';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { DistributionPlanResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { readPackage, resetCaptureDbForTests, savePackage } from '../offline/store';
import { ADMIN_PLAN, CAPTURE_PACKAGE, FIRING_CHIEF_PLAN, POWDER_DAY } from '../test-data';

const PAGE = `/editions/${ADMIN_PLAN.editionId}/distribution`;

const WITH_HANDOVERS: DistributionPlanResponse = {
  ...ADMIN_PLAN,
  days: ADMIN_PLAN.days.map((day) =>
    day.id === POWDER_DAY.id ? { ...day, handovers: { recorded: 37, holders: 120 } } : day,
  ),
};

function distribution(plan: DistributionPlanResponse) {
  server.use(
    mock.get(`/api/distribution/editions/${plan.editionId}`, () => HttpResponse.json(plan)),
    mock.get(`/api/distribution/editions/${plan.editionId}/proxies`, () => HttpResponse.json([])),
  );
}

const powder = () => screen.findByRole('region', { name: 'Día de reparto de pólvora' }, { timeout: 5000 });

afterEach(async () => {
  await resetCaptureDbForTests();
});

describe('Handover actions on the powder day (spec: Handover screens)', () => {
  it('shows an Admin how many holders have collected', async () => {
    distribution(WITH_HANDOVERS);
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    expect(await within(await powder()).findByText('37 de 120 entregados')).toBeInTheDocument();
  });

  it('prepares the device: downloads the day, keeps it for the Admin and offers to open the capture', async () => {
    const user = userEvent.setup();
    distribution(WITH_HANDOVERS);
    server.use(
      mock.get(`/api/distribution/distributions/${POWDER_DAY.id}/capture`, () =>
        HttpResponse.json(CAPTURE_PACKAGE),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Preparar para captura sin conexión' }));

    expect(await within(section).findByRole('link', { name: 'Abrir captura' })).toHaveAttribute(
      'href',
      `/distribution/capture/${POWDER_DAY.id}`,
    );
    expect(
      await screen.findByText(/La lista del reparto está guardada en este dispositivo/),
    ).toBeInTheDocument();
    expect((await readPackage(POWDER_DAY.id, SYNTHETIC_ADMIN.id))?.package.rows).toHaveLength(
      CAPTURE_PACKAGE.rows.length,
    );
  });

  it('offers to open the capture on a device already prepared', async () => {
    await savePackage(SYNTHETIC_ADMIN.id, CAPTURE_PACKAGE, new Date());
    distribution(WITH_HANDOVERS);
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    expect(await within(await powder()).findByRole('link', { name: 'Abrir captura' })).toBeInTheDocument();
  });

  it('says why the download failed', async () => {
    const user = userEvent.setup();
    distribution(WITH_HANDOVERS);
    server.use(
      mock.get(`/api/distribution/distributions/${POWDER_DAY.id}/capture`, () =>
        HttpResponse.json({ status: 409, code: 'distribution.editionNotInProgress' }, { status: 409 }),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    await user.click(within(section).getByRole('button', { name: 'Preparar para captura sin conexión' }));

    expect(await within(section).findByRole('alert')).toHaveTextContent(/edición no está en curso/);
    expect(await readPackage(POWDER_DAY.id, SYNTHETIC_ADMIN.id)).toBeUndefined();
  });

  it('does not offer to delete a powder day with handovers, and says why', async () => {
    distribution(WITH_HANDOVERS);
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    const section = await powder();

    expect(
      within(section).queryByRole('button', { name: /Borrar día de reparto de pólvora/ }),
    ).not.toBeInTheDocument();
    expect(
      within(section).getByText('No se puede borrar: tiene 37 entregas registradas.'),
    ).toBeInTheDocument();
  });

  it('offers no capture to a FiringChief', async () => {
    distribution(FIRING_CHIEF_PLAN);
    await renderApp(PAGE, { session: SYNTHETIC_FIRING_CHIEF });
    const section = await powder();

    await waitFor(() => {
      expect(
        within(section).queryByRole('button', { name: 'Preparar para captura sin conexión' }),
      ).not.toBeInTheDocument();
    });
    expect(within(section).queryByText(/entregados/)).not.toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    distribution(WITH_HANDOVERS);
    const { container } = await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    await within(await powder()).findByText('37 de 120 entregados');

    expect(await axeViolations(container)).toEqual([]);
  });
});
