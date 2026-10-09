import 'fake-indexeddb/auto';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import type { HandoverSyncRequest, SyncResult } from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { listQueue, putCaptured, readPackage, resetCaptureDbForTests, savePackage } from '../offline/store';
import { CAPTURE_PACKAGE, CAPTURED, SERVER_HANDOVER } from '../test-data';

const DAY = CAPTURE_PACKAGE.distributionId;
const PAGE = `/distribution/capture/${DAY}`;
const ADMIN = SYNTHETIC_ADMIN.id;

afterEach(async () => {
  await resetCaptureDbForTests();
});

/** The sync endpoint, answering every handover with `outcome` (recorded by default). */
function syncEndpoint(
  outcome: (item: { id: string; holderEntryId: string }) => Partial<SyncResult> = () => ({}),
) {
  const requests: HandoverSyncRequest[] = [];
  server.use(
    mock.post(`/api/distribution/distributions/${DAY}/handovers/sync`, async ({ request }) => {
      const body = (await request.json()) as HandoverSyncRequest;
      requests.push(body);
      return HttpResponse.json({
        results: (body.handovers ?? []).map((item) => ({
          id: item.id,
          outcome: 'RECORDED',
          code: null,
          handover: { ...SERVER_HANDOVER, ...item, version: 3 },
          existing: null,
          ...outcome({ id: item.id ?? '', holderEntryId: item.holderEntryId ?? '' }),
        })),
      });
    }),
  );
  return requests;
}

const holderRow = (name: RegExp) => screen.findByRole('button', { name });

describe('Capture screen (spec: Handover screens)', () => {
  it('shows the day, its status and the holders by slot and comparsa, each with its state', async () => {
    await savePackage(ADMIN, { ...CAPTURE_PACKAGE, handovers: [SERVER_HANDOVER] }, new Date());
    syncEndpoint();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('heading', { level: 1, name: 'Captura de entregas' })).toBeInTheDocument();
    expect(
      screen.getByText(/Reparto de pólvora del 18 de abril de 2031 en Paraje Sintético del Reparto/),
    ).toBeInTheDocument();
    const status = screen.getByRole('region', { name: 'Estado de la captura' });
    expect(within(status).getByText('Con conexión')).toBeInTheDocument();
    expect(within(status).getByText('0 pendientes de sincronizar')).toBeInTheDocument();
    const norte = screen.getByRole('heading', {
      level: 3,
      name: `09:00 · ${CAPTURE_PACKAGE.rows[0]?.comparsaName ?? ''}`,
    }).parentElement;
    if (!norte) throw new Error('No group');
    expect(
      within(norte).getByRole('button', { name: /Nº 1 · Abad Sintética, Ana.*Por entregar/ }),
    ).toBeInTheDocument();
    expect(
      within(norte).getByRole('button', { name: /Nº 2 · Bernabeu Sintético, Bruno.*Entregada/ }),
    ).toBeInTheDocument();
  });

  it('finds holders by number, name or DNI/NIE', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    syncEndpoint();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    await holderRow(/Nº 1 ·/);

    await user.type(
      screen.getByRole('searchbox', { name: 'Buscar por número, nombre o DNI/NIE' }),
      '00000003a',
    );

    expect(screen.getByRole('button', { name: /Nº 3 · Climent Sintética/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Nº 1 ·/ })).not.toBeInTheDocument();
  });

  it('records a handover with its flask number and syncs it', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    const requests = syncEndpoint();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    await user.click(await holderRow(/Nº 1 · Abad Sintética/));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Abad Sintética, Ana' });
    await user.click(within(panel).getByRole('radio', { name: /Su autorizado: Zamora Sintético, Bruno/ }));
    await user.type(within(panel).getByLabelText('Nº de cantimplora'), 'P-117');
    await user.type(within(panel).getByLabelText(/Trazabilidad 1/), 'A3');
    await user.click(within(panel).getByRole('button', { name: 'Registrar entrega' }));

    expect(await holderRow(/Nº 1 · Abad Sintética.*Entregada/)).toBeInTheDocument();
    const sent = requests.flatMap((r) => r.handovers ?? []);
    expect(sent).toEqual([
      expect.objectContaining({
        holderEntryId: CAPTURE_PACKAGE.rows[0]?.entryId,
        distributionNumber: 1,
        collectedBy: 'PROXY',
        collectorEntryId: CAPTURE_PACKAGE.rows[0]?.proxy?.entryId,
        rentalFlaskNumber: 'P-117',
        traceability1: 'A3',
        traceability2: null,
      }),
    ]);
    expect(await listQueue(DAY, ADMIN)).toEqual([]);
  });

  it('refuses a missing flask number and one already given, naming the holder who has it', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: new Date().toISOString() });
    server.use(
      mock.post(`/api/distribution/distributions/${DAY}/handovers/sync`, () =>
        problem(503, 'distribution.busy'),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    await user.click(await holderRow(/Nº 3 · Climent Sintética/));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Climent Sintética, Carla' });
    await user.click(within(panel).getByRole('button', { name: 'Registrar entrega' }));
    expect(
      await within(panel).findAllByText('Indica el número de la cantimplora alquilada.'),
    ).not.toHaveLength(0);

    await user.type(within(panel).getByLabelText('Nº de cantimplora'), 'p-117');
    await user.click(within(panel).getByRole('button', { name: 'Registrar entrega' }));

    expect(
      await within(panel).findAllByText('La cantimplora p-117 ya se ha entregado al número 1.'),
    ).not.toHaveLength(0);
    expect((await listQueue(DAY, ADMIN)).map((h) => h.holderEntryId)).toEqual([CAPTURED.holderEntryId]);
  });

  it('removes a handover not yet synced', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: new Date().toISOString() });
    server.use(
      mock.post(`/api/distribution/distributions/${DAY}/handovers/sync`, () =>
        problem(503, 'distribution.busy'),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    await user.click(await holderRow(/Nº 1 · Abad Sintética.*Pendiente de sincronizar/));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Abad Sintética, Ana' });
    expect(within(panel).getByLabelText('Nº de cantimplora')).toHaveValue('P-117');
    await user.click(within(panel).getByRole('button', { name: 'Quitar entrega' }));
    const confirm = await screen.findByRole('alertdialog', {
      name: '¿Quitar la entrega de Abad Sintética, Ana?',
    });
    await user.click(within(confirm).getByRole('button', { name: 'Quitar entrega' }));

    const row = await holderRow(/Nº 1 · Abad Sintética.*Por entregar/);
    await waitFor(() => {
      expect(row).toHaveFocus();
    });
    expect(await listQueue(DAY, ADMIN)).toEqual([]);
  });

  it('keeps the form open, with what was typed, when a sync records the handover meanwhile', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: new Date().toISOString() });
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    syncEndpoint();
    server.use(
      mock.post(`/api/distribution/distributions/${DAY}/handovers/sync`, async () => {
        await held;
        return undefined; // Falls through to the recording endpoint.
      }),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    await user.click(await holderRow(/Nº 1 · Abad Sintética.*Pendiente de sincronizar/));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Abad Sintética, Ana' });
    await user.type(within(panel).getByLabelText(/Trazabilidad 2/), 'B7');
    release();
    // Behind the open panel, the page is hidden from assistive technology.
    await screen.findByRole('button', { name: /Nº 1 · Abad Sintética.*Entregada/, hidden: true });

    expect(screen.getByRole('dialog', { name: 'Entrega de Abad Sintética, Ana' })).toBe(panel);
    expect(within(panel).getByLabelText(/Trazabilidad 2/)).toHaveValue('B7');
  });

  it('sends a conflict again as a new handover in its place, and returns focus to the holder', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({
      ...CAPTURED,
      ownerUserId: ADMIN,
      capturedAt: new Date().toISOString(),
      state: 'conflict',
      code: 'distribution.flaskNumberTaken',
    });
    const requests = syncEndpoint();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    const conflicts = await screen.findByRole('region', { name: 'Conflictos' });
    await user.click(within(conflicts).getByRole('button', { name: /Editar y reenviar/ }));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Abad Sintética, Ana' });
    await user.clear(within(panel).getByLabelText('Nº de cantimplora'));
    await user.type(within(panel).getByLabelText('Nº de cantimplora'), 'P-118');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    const row = await holderRow(/Nº 1 · Abad Sintética.*Entregada/);
    await waitFor(() => {
      expect(row).toHaveFocus();
    });
    const sent = requests.flatMap((r) => r.handovers ?? []);
    expect(sent).toEqual([expect.objectContaining({ rentalFlaskNumber: 'P-118' })]);
    expect(sent[0]?.id).not.toBe(CAPTURED.id);
    expect(screen.queryByRole('region', { name: 'Conflictos' })).not.toBeInTheDocument();
  });

  it('undoes a synced handover online after a confirmation naming the holder and the flask', async () => {
    const user = userEvent.setup();
    const synced = { ...SERVER_HANDOVER, rentalFlaskNumber: 'P-200' };
    await savePackage(ADMIN, { ...CAPTURE_PACKAGE, handovers: [synced] }, new Date());
    syncEndpoint();
    let undone: string | null = null;
    server.use(
      mock.delete(`/api/distribution/handovers/${synced.id}`, ({ request }) => {
        undone = new URL(request.url).searchParams.get('version');
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    await user.click(await holderRow(/Nº 2 · Bernabeu Sintético.*Entregada/));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Bernabeu Sintético, Bruno' });
    await user.click(within(panel).getByRole('button', { name: 'Deshacer entrega' }));
    const confirm = await screen.findByRole('alertdialog', {
      name: '¿Deshacer la entrega de Bernabeu Sintético, Bruno?',
    });
    expect(confirm).toHaveTextContent('P-200');
    await user.click(within(confirm).getByRole('button', { name: 'Deshacer entrega' }));

    expect(await holderRow(/Nº 2 · Bernabeu Sintético.*Por entregar/)).toBeInTheDocument();
    expect(undone).toBe(String(synced.version));
    expect((await readPackage(DAY, ADMIN))?.package.handovers).toEqual([]);
  });

  it('offers no undo of a synced handover without a session to undo it with', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, { ...CAPTURE_PACKAGE, handovers: [SERVER_HANDOVER] }, new Date());
    await renderApp(PAGE, { account: () => HttpResponse.error() });

    await user.click(await holderRow(/Nº 2 · Bernabeu Sintético.*Entregada/));
    const panel = await screen.findByRole('dialog', { name: 'Entrega de Bernabeu Sintético, Bruno' });

    expect(
      within(panel).getByText('Para deshacer una entrega ya sincronizada hace falta conexión.'),
    ).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Deshacer entrega' })).not.toBeInTheDocument();
  });

  it('lists the conflicts with their reason and the handover recorded first, and discards one', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({
      ...CAPTURED,
      ownerUserId: ADMIN,
      capturedAt: new Date().toISOString(),
      state: 'conflict',
      code: 'distribution.alreadyHandedOver',
      existing: { ...SERVER_HANDOVER, holderEntryId: CAPTURED.holderEntryId, rentalFlaskNumber: 'P-555' },
    });
    syncEndpoint();
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    const conflicts = await screen.findByRole('region', { name: 'Conflictos' });
    expect(
      within(conflicts).getByText('Otro dispositivo ya registró la entrega de este titular.'),
    ).toBeInTheDocument();
    expect(within(conflicts).getByText(/cantimplora P-555/)).toBeInTheDocument();
    await user.click(within(conflicts).getByRole('button', { name: /Descartar/ }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Descartar' }),
    );

    await waitFor(() => {
      expect(screen.queryByRole('region', { name: 'Conflictos' })).not.toBeInTheDocument();
    });
    expect(await listQueue(DAY, ADMIN)).toEqual([]);
  });

  it('keeps capturing with an ended session and asks to sign in to sync', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await renderApp(PAGE);

    expect(await screen.findByText(/Tu sesión ha terminado/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Iniciar sesión' })).toHaveAttribute(
      'href',
      expect.stringContaining('/login'),
    );
    expect(await holderRow(/Nº 1 · Abad Sintética/)).toBeInTheDocument();
  });

  it('works from the device when the server cannot be reached, without syncing', async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: new Date().toISOString() });
    const requests = syncEndpoint();
    await renderApp(PAGE, { account: () => HttpResponse.error() });

    expect(await holderRow(/Nº 1 · Abad Sintética.*Pendiente de sincronizar/)).toBeInTheDocument();
    expect(screen.queryByText(/Tu sesión ha terminado/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sincronizar ahora' })).toBeDisabled();
    expect(requests).toEqual([]);
  });

  it("never sends another Admin's handovers while the session is being checked (SEC-14)", async () => {
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: new Date().toISOString() });
    const requests = syncEndpoint();
    await renderApp(PAGE, {
      account: async () => {
        await delay(200);
        return HttpResponse.json({ ...SYNTHETIC_ADMIN, id: '00000000-0000-4000-8000-0000000000ff' });
      },
    });

    expect(await holderRow(/Nº 1 · Abad Sintética/)).toBeInTheDocument();
    expect(
      await screen.findByText('Este dispositivo no tiene la lista de este reparto.'),
    ).toBeInTheDocument();
    expect(requests).toEqual([]);
  });

  it('closes the capture once everything is synced, clearing the list from the device', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    syncEndpoint();
    const app = await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    await holderRow(/Nº 1 · Abad Sintética/);

    await user.click(screen.getByRole('button', { name: 'Cerrar captura' }));
    await user.click(
      within(await screen.findByRole('alertdialog', { name: '¿Cerrar la captura?' })).getByRole('button', {
        name: 'Cerrar captura',
      }),
    );

    await waitFor(() => {
      expect(app.location()).toBe(`/editions/${CAPTURE_PACKAGE.editionId}/distribution`);
    });
    expect(await readPackage(DAY, ADMIN)).toBeUndefined();
  });

  it('does not close the capture while handovers are left to sync', async () => {
    const user = userEvent.setup();
    await savePackage(ADMIN, CAPTURE_PACKAGE, new Date());
    await putCaptured({ ...CAPTURED, ownerUserId: ADMIN, capturedAt: new Date().toISOString() });
    server.use(
      mock.post(`/api/distribution/distributions/${DAY}/handovers/sync`, () =>
        problem(503, 'distribution.busy'),
      ),
    );
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    await holderRow(/Nº 1 · Abad Sintética.*Pendiente de sincronizar/);

    await user.click(screen.getByRole('button', { name: 'Cerrar captura' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Cerrar la captura?' });
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar captura' }));

    expect(
      await within(dialog).findByText(
        'Aún hay entregas sin sincronizar: sincronízalas o descártalas antes de cerrar.',
      ),
    ).toBeInTheDocument();
    expect(await readPackage(DAY, ADMIN)).toBeDefined();
  });

  it('says the device has no list for the day', async () => {
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText('Este dispositivo no tiene la lista de este reparto.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ir a la página de reparto' })).toHaveAttribute(
      'href',
      '/distribution',
    );
  });

  it('shows no list of another Admin to whoever signs in', async () => {
    await savePackage('00000000-0000-4000-8000-0000000000ff', CAPTURE_PACKAGE, new Date());
    await renderApp(PAGE, { session: SYNTHETIC_ADMIN });

    expect(
      await screen.findByText('Este dispositivo no tiene la lista de este reparto.'),
    ).toBeInTheDocument();
  });

  it('is not allowed to a FiringChief', async () => {
    await renderApp(PAGE, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
  });

  it('has no accessibility violations', async () => {
    await savePackage(ADMIN, { ...CAPTURE_PACKAGE, handovers: [SERVER_HANDOVER] }, new Date());
    syncEndpoint();
    const { container } = await renderApp(PAGE, { session: SYNTHETIC_ADMIN });
    await holderRow(/Nº 1 · Abad Sintética/);

    expect(await axeViolations(container)).toEqual([]);
  });
});
