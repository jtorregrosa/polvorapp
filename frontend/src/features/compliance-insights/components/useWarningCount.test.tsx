import { screen, waitFor } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ComplianceSummaryResponse } from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { invalidateInsights } from '../queries';
import { SUMMARY, SUMMARY_UP_TO_DATE } from '../test-data';

// Spec "Warning count in the navigation": the Arquebusiers item says how many arquebusiers in the
// user's scope have warnings; the Statistics page is in the navigation for every role.

function summary(respond: () => Response) {
  server.use(
    mock.get('/api/compliance/summary', respond),
    mock.get('/api/arquebusiers', () => HttpResponse.json([])),
    mock.get('/api/comparsas', () => HttpResponse.json([])),
  );
}

const navigation = () => screen.getByRole('navigation', { name: 'Navegación principal' });

describe('Warning count in the navigation', () => {
  it('shows the count on the Arquebusiers item, as part of its name', async () => {
    summary(() => HttpResponse.json({ ...SUMMARY, withWarnings: 3 } satisfies ComplianceSummaryResponse));
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('link', { name: 'Arcabuceros, 3 con avisos' })).toHaveAttribute(
      'href',
      '/arquebusiers',
    );
  });

  it('says "1 con aviso" for a single arquebusier', async () => {
    summary(() => HttpResponse.json({ ...SUMMARY, withWarnings: 1 }));
    await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('link', { name: 'Arcabuceros, 1 con aviso' })).toBeInTheDocument();
  });

  it('shows no count when nothing is pending', async () => {
    summary(() => HttpResponse.json(SUMMARY_UP_TO_DATE));
    await renderApp('/account', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('link', { name: 'Arcabuceros' })).toBeInTheDocument();
    expect(navigation().querySelector('[data-sidebar="menu-badge"]')).toBeNull();
  });

  it('shows no count when the summary cannot be loaded', async () => {
    summary(() => problem(500, 'server.error'));
    await renderApp('/account', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('link', { name: 'Arcabuceros' })).toBeInTheDocument();
    expect(navigation().querySelector('[data-sidebar="menu-badge"]')).toBeNull();
  });

  it('follows a fix without reloading the page', async () => {
    let withWarnings = 1;
    summary(() => HttpResponse.json({ ...SUMMARY, withWarnings }));
    const app = await renderApp('/account', { session: SYNTHETIC_FIRING_CHIEF });
    await screen.findByRole('link', { name: 'Arcabuceros, 1 con aviso' });

    withWarnings = 0;
    await invalidateInsights(app.queryClient);

    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'Arcabuceros' })).toBeInTheDocument();
    });
  });

  it.each([
    ['an Admin', SYNTHETIC_ADMIN],
    ['a FiringChief', SYNTHETIC_FIRING_CHIEF],
  ])('offers the Statistics page to %s', async (_, session) => {
    summary(() => HttpResponse.json(SUMMARY_UP_TO_DATE));
    await renderApp('/account', { session });

    expect(await screen.findByRole('link', { name: 'Estadísticas' })).toHaveAttribute('href', '/statistics');
  });
});
