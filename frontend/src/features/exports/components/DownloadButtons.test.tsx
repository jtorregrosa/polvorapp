import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { problem } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { DownloadButtons } from './DownloadButtons';

const URLS = { xlsx: '/api/exports/test.xlsx', pdf: '/api/exports/test.pdf' };

let saved: string | undefined;

beforeEach(() => {
  saved = undefined;
  // jsdom has no object URLs: a stand-in URL class, undone after each test.
  vi.stubGlobal(
    'URL',
    class extends URL {
      static override createObjectURL = vi.fn(() => 'blob:export');
      static override revokeObjectURL = vi.fn();
    },
  );
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved = this.download;
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function file(name: string) {
  return new HttpResponse(new Uint8Array([1, 2, 3]), {
    headers: { 'Content-Disposition': `attachment; filename=${name}` },
  });
}

describe('DownloadButtons (spec: Exports screens)', () => {
  it('names each button after what it downloads', async () => {
    await renderWithProviders(<DownloadButtons what="la Intervención de Armas" urls={URLS} />);

    expect(
      screen.getByRole('button', { name: 'Descargar la Intervención de Armas en Excel' }),
    ).toHaveTextContent('Excel');
    expect(
      screen.getByRole('button', { name: 'Descargar la Intervención de Armas en PDF' }),
    ).toHaveTextContent('PDF');
  });

  it('saves the file with the name the server gives', async () => {
    const user = userEvent.setup();
    server.use(mock.get(URLS.pdf, () => file('polvorapp-2031-arms-authority-provisional.pdf')));
    await renderWithProviders(<DownloadButtons what="la Intervención de Armas" urls={URLS} />);

    await user.click(screen.getByRole('button', { name: /en PDF$/ }));

    await waitFor(() => {
      expect(saved).toBe('polvorapp-2031-arms-authority-provisional.pdf');
    });
  });

  it('shows the download in progress on the button pressed', async () => {
    const user = userEvent.setup();
    let release: () => void = () => undefined;
    server.use(
      mock.get(URLS.xlsx, async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return file('lista.xlsx');
      }),
    );
    await renderWithProviders(<DownloadButtons what="la lista" urls={URLS} />);

    await user.click(screen.getByRole('button', { name: /en Excel$/ }));

    expect(screen.getByRole('button', { name: /en Excel, Un momento/ })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('button', { name: /en PDF$/ })).not.toHaveAttribute('aria-busy', 'true');
    release();
    await waitFor(() => {
      expect(saved).toBe('lista.xlsx');
    });
  });

  it.each([
    [() => problem(409, 'exports.notPrepared'), 'La comparsa no ha preparado su pedido.'],
    [
      () => problem(429, 'tooManyRequests'),
      'Has descargado demasiados ficheros seguidos. Espera un minuto y vuelve a intentarlo.',
    ],
    [
      () => problem(503, 'exports.auditUnavailable'),
      'No se ha podido registrar la descarga, así que no se ha generado el fichero. Inténtalo de nuevo en unos minutos.',
    ],
    [() => HttpResponse.error(), 'Inténtalo de nuevo.'],
  ])('says what failed and why', async (answer, message) => {
    const user = userEvent.setup();
    server.use(mock.get(URLS.xlsx, answer));
    await renderWithProviders(<DownloadButtons what="la lista" urls={URLS} />);

    await user.click(screen.getByRole('button', { name: /en Excel$/ }));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(screen.getByText('No se ha descargado la lista')).toBeInTheDocument();
    expect(saved).toBeUndefined();
  });

  it('ignores a second download while one runs, and says "one moment" on the busy button', async () => {
    const user = userEvent.setup();
    let calls = 0;
    let release: () => void = () => undefined;
    server.use(
      mock.get(URLS.xlsx, async () => {
        calls += 1;
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return file('lista.xlsx');
      }),
      mock.get(URLS.pdf, () => {
        calls += 1;
        return file('lista.pdf');
      }),
    );
    await renderWithProviders(<DownloadButtons what="la lista" urls={URLS} />);

    await user.click(screen.getByRole('button', { name: /en Excel$/ }));
    await user.click(screen.getByRole('button', { name: /en PDF$/ }));

    expect(
      screen.getByRole('button', { name: 'Descargar la lista en Excel, Un momento…' }),
    ).toBeInTheDocument();
    release();
    await waitFor(() => {
      expect(saved).toBe('lista.xlsx');
    });
    expect(calls).toBe(1);
    expect(screen.getByRole('status')).toHaveTextContent('Descargado: lista.xlsx');
  });

  it('falls back to a name of its own when the server gives none', async () => {
    const user = userEvent.setup();
    server.use(mock.get(URLS.pdf, () => new HttpResponse(new Uint8Array([1]))));
    await renderWithProviders(<DownloadButtons what="la lista" urls={URLS} />);

    await user.click(screen.getByRole('button', { name: /en PDF$/ }));

    await waitFor(() => {
      expect(saved).toBe('polvorapp.pdf');
    });
  });

  it('clears the failure when the user tries again', async () => {
    const user = userEvent.setup();
    let fail = true;
    server.use(
      mock.get(URLS.xlsx, () => (fail ? problem(503, 'exports.auditUnavailable') : file('lista.xlsx'))),
    );
    await renderWithProviders(<DownloadButtons what="la lista" urls={URLS} />);
    await user.click(screen.getByRole('button', { name: /en Excel$/ }));
    expect(await screen.findByText('No se ha descargado la lista')).toBeInTheDocument();

    fail = false;
    await user.click(screen.getByRole('button', { name: /en Excel$/ }));

    await waitFor(() => {
      expect(saved).toBe('lista.xlsx');
    });
    expect(screen.queryByText('No se ha descargado la lista')).not.toBeInTheDocument();
  });

  it('speaks Valencian and English', async () => {
    const { unmount } = await renderWithProviders(
      <DownloadButtons what="la llista" urls={URLS} />,
      'ca-ES-valencia',
    );
    expect(screen.getByRole('button', { name: 'Descarrega la llista en Excel' })).toBeInTheDocument();
    unmount();

    await renderWithProviders(<DownloadButtons what="the list" urls={URLS} />, 'en');
    expect(screen.getByRole('button', { name: 'Download the list as PDF' })).toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    const { container } = await renderWithProviders(<DownloadButtons what="la lista" urls={URLS} />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
