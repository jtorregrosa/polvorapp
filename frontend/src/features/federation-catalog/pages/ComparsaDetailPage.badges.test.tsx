import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ArquebusierRowResponse } from '@/api/generated/model';
import { renderApp } from '@/test/app';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE } from '../test-data';

// Synthetic data only.
function arquebusier(id: string, lastName: string, hasIdPhoto: boolean): ArquebusierRowResponse {
  return {
    id,
    firstName: 'Ana',
    lastName,
    nationalId: '00000000T',
    federationId: 1,
    comparsaId: NORTE.id,
    comparsaName: NORTE.name,
    status: 'ACTIVE',
    licenseStatus: 'VALID',
    licenseExpiresOn: '2033-05-31',
    hasIdPhoto,
    warnings: [],
  };
}

function comparsa(rows: ArquebusierRowResponse[]) {
  const queries: string[] = [];
  server.use(
    mock.get(`/api/comparsas/${NORTE.id}`, () => HttpResponse.json(NORTE)),
    mock.get(`/api/comparsas/${NORTE.id}/firing-chiefs`, () => HttpResponse.json([])),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
    mock.get('/api/users', () => HttpResponse.json([])),
    mock.get('/api/arquebusiers', ({ request }) => {
      queries.push(new URL(request.url).search);
      return HttpResponse.json(rows);
    }),
  );
  return queries;
}

describe('ComparsaDetailPage badges (spec: Badge screens)', () => {
  it("offers Admins the comparsa's badges with its counts", async () => {
    const user = userEvent.setup();
    const queries = comparsa([
      arquebusier('a1', 'Abad Sintético', true),
      arquebusier('a2', 'Bravo Sintético', false),
    ]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await user.click(await screen.findByRole('button', { name: `Imprimir carnets de ${NORTE.name}` }));

    const dialog = await screen.findByRole('dialog', { name: 'Imprimir carnets' });
    expect(dialog).toHaveTextContent(`2 arcabuceros de ${NORTE.name}, activos y en reserva`);
    expect(within(dialog).getByText(/1 sin foto de carnet/)).toBeInTheDocument();
    expect(queries).toContain(`?comparsaId=${NORTE.id}`);
  });

  it('offers nothing for a comparsa without arquebusiers', async () => {
    comparsa([]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('heading', { name: NORTE.name });

    expect(screen.queryByRole('button', { name: /Imprimir carnets/ })).not.toBeInTheDocument();
  });

  it('offers FiringChiefs no badges', async () => {
    comparsa([arquebusier('a1', 'Abad Sintético', true)]);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('heading', { name: NORTE.name });

    expect(screen.queryByRole('button', { name: /Imprimir carnets/ })).not.toBeInTheDocument();
  });
});
