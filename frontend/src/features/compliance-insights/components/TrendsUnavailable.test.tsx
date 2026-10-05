import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import TrendsUnavailable from './TrendsUnavailable';

const original = window.location;

describe('TrendsUnavailable', () => {
  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: original });
  });

  it('says the trends could not be loaded and offers to try again', async () => {
    await renderWithProviders(<TrendsUnavailable />);

    expect(screen.getByRole('alert')).toHaveTextContent(
      'No se han podido cargar las tendencias: recarga la página para volver a intentarlo.',
    );
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('reloads the page to try again', async () => {
    const reload = vi.fn();
    Object.defineProperty(window, 'location', { configurable: true, value: { ...original, reload } });
    const user = userEvent.setup();
    await renderWithProviders(<TrendsUnavailable />);

    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(reload).toHaveBeenCalledOnce();
  });
});
