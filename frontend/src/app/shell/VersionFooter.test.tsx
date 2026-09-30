import { screen } from '@testing-library/react';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { VersionFooter } from './VersionFooter';

describe('VersionFooter', () => {
  it('shows the API version in the active language', async () => {
    server.use(
      mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc1234' })),
    );

    await renderWithProviders(<VersionFooter />, 'ca-ES-valencia');

    expect(await screen.findByText('Versió 1.4.0')).toBeInTheDocument();
  });

  it('shows a translated notice when the API fails', async () => {
    server.use(mock.get('/api/system/info', () => new HttpResponse(null, { status: 503 })));

    await renderWithProviders(<VersionFooter />, 'en');

    expect(await screen.findByText('Version unavailable')).toBeInTheDocument();
  });

  it('shows a translated notice when the API is unreachable', async () => {
    server.use(mock.get('/api/system/info', () => HttpResponse.error()));

    await renderWithProviders(<VersionFooter />);

    expect(await screen.findByText('Versión no disponible')).toBeInTheDocument();
  });
});
