import 'fake-indexeddb/auto';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it } from 'vitest';
import { CAPTURE_PACKAGE, CAPTURED } from '@/features/distribution/test-data';
import {
  listQueue,
  putCaptured,
  readPackage,
  resetCaptureDbForTests,
  savePackage,
} from '@/features/distribution/offline/store';
import { renderApp } from '@/test/app';
import { SYNTHETIC_ADMIN } from '@/test/identity';
import { server } from '@/test/server';

const DAY = CAPTURE_PACKAGE.distributionId;
const NOW = new Date();

afterEach(async () => {
  await resetCaptureDbForTests();
});

function countLogout(): { calls: number } {
  const count = { calls: 0 };
  server.use(
    mock.post('/api/auth/logout', () => {
      count.calls++;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return count;
}

async function openSignOut() {
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: /^Menú de / }));
  await user.click(await screen.findByRole('menuitem', { name: 'Cerrar sesión' }));
  return user;
}

describe('capture device on sign-out and sign-in (distribution spec: Data kept on the device, SEC-14)', () => {
  it('warns how many handovers would be lost and signs out only after confirmation, clearing the device', async () => {
    await savePackage(SYNTHETIC_ADMIN.id, CAPTURE_PACKAGE, NOW);
    for (const id of ['a', 'b', 'c']) {
      await putCaptured({ ...CAPTURED, id, ownerUserId: SYNTHETIC_ADMIN.id, capturedAt: NOW.toISOString() });
    }
    const logout = countLogout();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const user = await openSignOut();
    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText(/3 entregas/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    expect(logout.calls).toBe(0);
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^Menú de / })).toHaveFocus();
    });
    expect(await listQueue(DAY, SYNTHETIC_ADMIN.id)).toHaveLength(3);

    await openSignOut();
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cerrar sesión y borrar' }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    expect(logout.calls).toBe(1);
    expect(await listQueue(DAY, SYNTHETIC_ADMIN.id)).toEqual([]);
    expect(await readPackage(DAY, SYNTHETIC_ADMIN.id)).toBeUndefined();
  });

  it('keeps the session and the device when signing out fails', async () => {
    await putCaptured({ ...CAPTURED, ownerUserId: SYNTHETIC_ADMIN.id, capturedAt: NOW.toISOString() });
    server.use(mock.post('/api/auth/logout', () => new HttpResponse(null, { status: 500 })));
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    const user = await openSignOut();
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cerrar sesión y borrar' }),
    );

    expect(
      await within(screen.getByRole('alertdialog')).findByText(
        'No se ha podido cerrar la sesión. Comprueba la conexión y vuelve a intentarlo.',
      ),
    ).toBeInTheDocument();
    expect(await listQueue(DAY, SYNTHETIC_ADMIN.id)).toHaveLength(1);
  });

  it("clears the user's packages when they sign out everywhere, keeping their queue", async () => {
    const user = userEvent.setup();
    await savePackage(SYNTHETIC_ADMIN.id, CAPTURE_PACKAGE, NOW);
    await putCaptured({ ...CAPTURED, ownerUserId: SYNTHETIC_ADMIN.id, capturedAt: NOW.toISOString() });
    server.use(mock.post('/api/account/sign-out-everywhere', () => new HttpResponse(null, { status: 204 })));
    await renderApp('/account', { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: 'Cerrar todas las sesiones' }));
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cerrar todas las sesiones' }),
    );

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    await waitFor(async () => {
      expect(await readPackage(DAY, SYNTHETIC_ADMIN.id)).toBeUndefined();
    });
    expect(await listQueue(DAY, SYNTHETIC_ADMIN.id)).toHaveLength(1);
  });

  it('signs out at once with nothing to lose, and clears the downloaded package', async () => {
    await savePackage(SYNTHETIC_ADMIN.id, CAPTURE_PACKAGE, NOW);
    const logout = countLogout();
    await renderApp('/', { session: SYNTHETIC_ADMIN });

    await openSignOut();

    expect(await screen.findByRole('heading', { level: 1, name: 'Iniciar sesión' })).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(logout.calls).toBe(1);
    await waitFor(async () => {
      expect(await readPackage(DAY, SYNTHETIC_ADMIN.id)).toBeUndefined();
    });
  });

  it("clears another Admin's package when someone else signs in", async () => {
    const previous = '00000000-0000-4000-8000-0000000000ff';
    await savePackage(previous, CAPTURE_PACKAGE, NOW);

    await renderApp('/', { session: SYNTHETIC_ADMIN });

    await waitFor(async () => {
      expect(await readPackage(DAY, previous)).toBeUndefined();
    });
  });
});
