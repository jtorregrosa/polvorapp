import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { SaveNoticeProvider } from './SaveNotice';
import { useSaveNotice } from './save-notice';

// Spec: Detail pages in read mode ("Changes saved" is announced without taking focus).

function Saver({ text = 'Cambios guardados' }: { text?: string }) {
  const notify = useSaveNotice();
  return (
    <button
      type="button"
      onClick={() => {
        notify(text);
      }}
    >
      Guardar
    </button>
  );
}

describe('SaveNotice', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('is one polite status region mounted once by the provider', async () => {
    await renderWithProviders(
      <SaveNoticeProvider>
        <Saver />
        <Saver text="Otro" />
      </SaveNoticeProvider>,
    );

    const regions = screen.getAllByRole('status');
    expect(regions).toHaveLength(1);
    expect(regions[0]).toHaveAttribute('aria-live', 'polite');
    expect(regions[0]).toBeEmptyDOMElement();
  });

  it('announces a notice without moving the focus', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <SaveNoticeProvider>
        <Saver />
      </SaveNoticeProvider>,
    );
    const button = screen.getByRole('button', { name: 'Guardar' });

    await user.click(button);

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('Cambios guardados');
    });
    expect(button).toHaveFocus();
  });

  it('announces the same notice again when it is repeated', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
    await renderWithProviders(
      <SaveNoticeProvider>
        <Saver />
      </SaveNoticeProvider>,
    );
    const status = screen.getByRole('status');
    const button = screen.getByRole('button', { name: 'Guardar' });

    await user.click(button);
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    expect(status).toHaveTextContent('Cambios guardados');

    await user.click(button);
    // Cleared first, so assistive technology sees a change and reads it again.
    expect(status).toBeEmptyDOMElement();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
    expect(status).toHaveTextContent('Cambios guardados');
  });

  it('hides the notice after a few seconds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
    await renderWithProviders(
      <SaveNoticeProvider>
        <Saver />
      </SaveNoticeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(9000);
    });

    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });

  it('does nothing outside a provider', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Saver />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
