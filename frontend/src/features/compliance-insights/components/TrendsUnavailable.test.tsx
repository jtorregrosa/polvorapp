import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import TrendsUnavailable from './TrendsUnavailable';

describe('TrendsUnavailable', () => {
  it('says the trends could not be loaded and offers to try again', async () => {
    await renderWithProviders(<TrendsUnavailable />);

    expect(screen.getByRole('alert')).toHaveTextContent(
      'No se han podido cargar las tendencias: recarga la página para volver a intentarlo.',
    );
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
});
