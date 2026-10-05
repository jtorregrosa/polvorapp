import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ComparsaLogoResponse, ComparsaResponse } from '@/api/generated/model';
import { cropImage, loadImage, previewOf } from '@/components/app/photo-image';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { LOGO, NORTE } from '../test-data';

// jsdom cannot decode images or draw on a canvas: the browser image work is replaced.
vi.mock('@/components/app/photo-image', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/app/photo-image')>()),
  loadImage: vi.fn(),
  rotateImage: vi.fn(),
  cropImage: vi.fn(),
  previewOf: vi.fn(),
  releaseImage: vi.fn(),
}));

const PNG = new Blob([new Uint8Array([0x89, 0x50])], { type: 'image/png' });
const NEW_LOGO: ComparsaLogoResponse = { ...LOGO, version: '0193a600-0000-7000-8000-0000000000aa' };

interface Upload {
  url: string;
  file: FormDataEntryValue | null;
}

/** The comparsa API, with the logo changing as uploads and removals succeed. */
function comparsa(initial: ComparsaResponse) {
  let current = initial;
  server.use(
    mock.get(`/api/comparsas/${initial.id}`, () => HttpResponse.json(current)),
    mock.get(`/api/comparsas/${initial.id}/firing-chiefs`, () => HttpResponse.json([])),
    mock.get('/api/comparsas', () => HttpResponse.json([current])),
    mock.get('/api/users', () => HttpResponse.json([])),
  );
  return {
    setLogo: (logo: ComparsaLogoResponse | null) => {
      current = { ...current, logo };
    },
  };
}

/**
 * Multipart uploads go through `fetch` directly: jsdom's FormData does not cross MSW intact, so the
 * logo PUT is answered here and its body recorded.
 */
function interceptUploads(respond: () => Response): Upload[] {
  const uploads: Upload[] = [];
  const realFetch = globalThis.fetch;
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    if (url.endsWith('/logo') && (init?.method ?? 'GET').toUpperCase() === 'PUT') {
      uploads.push({ url, file: init?.body instanceof FormData ? init.body.get('file') : null });
      return Promise.resolve(respond());
    }
    return realFetch(input, init);
  });
  return uploads;
}

function chooseLogo() {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) throw new Error('No file input');
  fireEvent.change(input, {
    target: { files: [new File([new Uint8Array([1])], 'logo.png', { type: 'image/png' })] },
  });
}

/**
 * The logo tile of the page: the record header's, the only `ComparsaLogo` in the main content (the
 * logo section shows the logo through `PhotoUpload`, the sidebar is outside `main`).
 */
function headerTile(): HTMLElement {
  const tiles = screen
    // Also while a dialog hides the page from assistive technology.
    .getByRole('main', { hidden: true })
    .querySelectorAll<HTMLElement>('[data-slot="comparsa-logo"]');
  expect(tiles).toHaveLength(1);
  const [tile] = tiles;
  if (!tile) throw new Error('No logo in the record header');
  return tile;
}

/** Opens the logo's own menu and chooses `action` (spec: Picture actions). */
async function logoAction(
  user: UserEvent,
  section: HTMLElement,
  action: 'Sustituir' | 'Quitar',
): Promise<void> {
  await user.click(within(section).getByRole('button', { name: /, opciones$/ }));
  await user.click(await screen.findByRole('menuitem', { name: action }));
}

describe('Comparsa logo on the detail page (spec: Logo display)', () => {
  beforeEach(() => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:logo', width: 800, height: 400 });
    vi.mocked(cropImage).mockResolvedValue(PNG);
    vi.mocked(previewOf).mockResolvedValue({ url: 'blob:preview', width: 160, height: 80 });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows a FiringChief the logo in the header, decorative, without any logo action', async () => {
    comparsa({ ...NORTE, logo: LOGO });
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('heading', { level: 1, name: NORTE.name });
    const image = headerTile().querySelector('img');
    expect(image).toHaveAttribute('src', `/api/comparsas/${NORTE.id}/logo?v=${LOGO.version}`);
    expect(image).toHaveAttribute('alt', '');
    expect(screen.queryByRole('button', { name: /logo de la comparsa|, opciones$/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Logo' })).not.toBeInTheDocument();
  });

  it('shows the placeholder in the header and offers an Admin to add a logo', async () => {
    comparsa(NORTE);
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });

    await screen.findByRole('heading', { level: 1, name: NORTE.name });
    expect(headerTile().querySelector('img')).toBeNull();
    const section = screen.getByRole('region', { name: 'Logo' });
    expect(within(section).getByRole('button', { name: 'Añadir logo de la comparsa' })).toBeInTheDocument();
  });

  it('uploads a PNG logo and refreshes the header', async () => {
    const user = userEvent.setup();
    const api = comparsa(NORTE);
    const uploads = interceptUploads(() => {
      api.setLogo(NEW_LOGO);
      return HttpResponse.json({
        version: NEW_LOGO.version,
        width: 800,
        height: 400,
        uploadedAt: NEW_LOGO.uploadedAt,
      });
    });
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('heading', { level: 1, name: NORTE.name });

    chooseLogo();
    const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la comparsa' });
    await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

    await waitFor(() => {
      expect(headerTile().querySelector('img')).toHaveAttribute(
        'src',
        `/api/comparsas/${NORTE.id}/logo?v=${NEW_LOGO.version}`,
      );
    });
    expect(uploads).toHaveLength(1);
    expect(uploads[0]?.url).toContain(`/api/comparsas/${NORTE.id}/logo`);
    const file = uploads[0]?.file;
    expect(file).toBeInstanceOf(Blob);
    expect(file instanceof Blob ? file.type : undefined).toBe('image/png');
    expect(cropImage).toHaveBeenCalledWith(
      expect.anything(),
      { x: 0, y: 0, width: 800, height: 400 },
      800,
      400,
      'png',
    );
  });

  it.each([
    [400, 'validation', { errors: { file: 'tooSmall' } }, 'El logo es demasiado pequeño'],
    [503, 'storage.unavailable', {}, 'El almacén de imágenes no responde ahora mismo.'],
    [503, 'catalog.busy', {}, 'El servidor está procesando otras imágenes'],
    [429, 'tooManyRequests', {}, 'Has hecho demasiadas subidas seguidas'],
  ])(
    'keeps the crop open with the translated reason when the upload is refused (%i %s)',
    async (status, code, extra, reason) => {
      const user = userEvent.setup();
      comparsa(NORTE);
      // A fresh response per request: a body can be read only once.
      interceptUploads(() => problem(status, code, extra));
      await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
      await screen.findByRole('heading', { level: 1, name: NORTE.name });

      chooseLogo();
      const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la comparsa' });
      await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

      expect(await within(dialog).findByText(reason, { exact: false })).toBeInTheDocument();
    },
  );

  it('replaces an existing logo and shows the new one in the header', async () => {
    const user = userEvent.setup();
    const api = comparsa({ ...NORTE, logo: LOGO });
    const uploads = interceptUploads(() => {
      api.setLogo(NEW_LOGO);
      return HttpResponse.json({
        version: NEW_LOGO.version,
        width: 800,
        height: 400,
        uploadedAt: NEW_LOGO.uploadedAt,
      });
    });
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const section = await screen.findByRole('region', { name: 'Logo' });
    expect(
      within(section).getByRole('button', { name: 'Logo de Comparsa Sintética Norte, opciones' }),
    ).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /^(Sustituir|Quitar) / })).toBeNull();

    chooseLogo();
    const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la comparsa' });
    await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

    await waitFor(() => {
      expect(headerTile().querySelector('img')).toHaveAttribute(
        'src',
        `/api/comparsas/${NORTE.id}/logo?v=${NEW_LOGO.version}`,
      );
    });
    expect(uploads).toHaveLength(1);
  });

  it('refuses a logo that is too small before uploading it, in logo terms', async () => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:small', width: 200, height: 150 });
    comparsa(NORTE);
    const uploads = interceptUploads(() => new HttpResponse(null, { status: 500 }));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const section = await screen.findByRole('region', { name: 'Logo' });

    chooseLogo();

    expect(await within(section).findByRole('alert')).toHaveTextContent('al menos 256 px');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(uploads).toHaveLength(0);
  });

  it('removes the logo only after a confirmation, and cancelling keeps it', async () => {
    const user = userEvent.setup();
    const api = comparsa({ ...NORTE, logo: LOGO });
    let removals = 0;
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/logo`, () => {
        removals++;
        api.setLogo(null);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const section = await screen.findByRole('region', { name: 'Logo' });

    await logoAction(user, section, 'Quitar');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Cancelar' }),
    );
    expect(removals).toBe(0);

    await logoAction(user, section, 'Quitar');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );

    await waitFor(() => {
      expect(headerTile().querySelector('img')).toBeNull();
    });
    expect(removals).toBe(1);
  });

  it('announces the removal without moving focus away from the add action', async () => {
    const user = userEvent.setup();
    const api = comparsa({ ...NORTE, logo: LOGO });
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/logo`, () => {
        api.setLogo(null);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const section = await screen.findByRole('region', { name: 'Logo' });

    await logoAction(user, section, 'Quitar');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );

    expect(
      await within(section).findByText('Logo quitado', { selector: '[role=status]' }),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: 'Añadir logo de la comparsa' })).toHaveFocus();
    });
  });

  it('keeps the logo and shows the reason when the removal fails', async () => {
    const user = userEvent.setup();
    comparsa({ ...NORTE, logo: LOGO });
    server.use(mock.delete(`/api/comparsas/${NORTE.id}/logo`, () => problem(503, 'catalog.busy')));
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const section = await screen.findByRole('region', { name: 'Logo' });

    await logoAction(user, section, 'Quitar');
    const confirmation = await screen.findByRole('alertdialog');
    await user.click(within(confirmation).getByRole('button', { name: 'Quitar logo' }));

    expect(
      await within(confirmation).findByText('El servidor está procesando otras imágenes', { exact: false }),
    ).toBeInTheDocument();
    expect(headerTile().querySelector('img')).not.toBeNull();
  });

  it('treats a logo already removed elsewhere as removed and shows the current state', async () => {
    const user = userEvent.setup();
    const api = comparsa({ ...NORTE, logo: LOGO });
    server.use(
      mock.delete(`/api/comparsas/${NORTE.id}/logo`, () => {
        api.setLogo(null);
        return problem(404, 'logos.notFound');
      }),
    );
    await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    const section = await screen.findByRole('region', { name: 'Logo' });

    await logoAction(user, section, 'Quitar');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    await waitFor(() => {
      expect(headerTile().querySelector('img')).toBeNull();
    });
    expect(within(section).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('has no automatically detectable accessibility violations', async () => {
    comparsa({ ...NORTE, logo: LOGO });
    const { container } = await renderApp(`/comparsas/${NORTE.id}`, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('region', { name: 'Logo' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
