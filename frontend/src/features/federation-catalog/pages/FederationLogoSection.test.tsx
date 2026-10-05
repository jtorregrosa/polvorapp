import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ComparsaLogoResponse } from '@/api/generated/model';
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
const NEW_LOGO: ComparsaLogoResponse = { ...LOGO, version: '0193a600-0000-7000-8000-0000000000bb' };

/** The comparsas and the Federation's settings, with the logo changing as uploads and removals succeed. */
function federation(initial: ComparsaLogoResponse | null) {
  let logo = initial;
  server.use(
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
    mock.get('/api/federation', () => HttpResponse.json({ logo })),
  );
  return {
    setLogo: (next: ComparsaLogoResponse | null) => {
      logo = next;
    },
  };
}

/** Multipart uploads go through `fetch` directly: jsdom's FormData does not cross MSW intact. */
function interceptUploads(respond: () => Response): string[] {
  const uploads: string[] = [];
  const realFetch = globalThis.fetch;
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    if (url.endsWith('/federation-logo') && (init?.method ?? 'GET').toUpperCase() === 'PUT') {
      uploads.push(url);
      return Promise.resolve(respond());
    }
    return realFetch(input, init);
  });
  return uploads;
}

function chooseLogo(section: HTMLElement) {
  const input = section.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) throw new Error('No file input');
  fireEvent.change(input, {
    target: { files: [new File([new Uint8Array([1])], 'escudo.png', { type: 'image/png' })] },
  });
}

/** The page's Federation logo section, once the page and the logo state have loaded. */
async function federationSection(name = 'Logo de la Federación'): Promise<HTMLElement> {
  await screen.findByRole('table', { name: /Comparsas/ }, { timeout: 5000 });
  const section = await screen.findByRole('region', { name });
  await waitFor(
    () => {
      expect(within(section).queryByText(/Cargando|Loading/)).not.toBeInTheDocument();
    },
    { timeout: 5000 },
  );
  return section;
}

const uploaded = () =>
  HttpResponse.json({ version: NEW_LOGO.version, width: 800, height: 400, uploadedAt: NEW_LOGO.uploadedAt });

/** Opens the logo's own menu and chooses `action` (spec: Picture actions). */
async function logoAction(
  user: UserEvent,
  section: HTMLElement,
  action: 'Sustituir' | 'Quitar',
): Promise<void> {
  await user.click(within(section).getByRole('button', { name: /, opciones$/ }));
  await user.click(await screen.findByRole('menuitem', { name: action }));
}

describe('Federation logo on the comparsas page (spec: Federation logo)', () => {
  beforeEach(() => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:logo', width: 800, height: 400 });
    vi.mocked(cropImage).mockResolvedValue(PNG);
    vi.mocked(previewOf).mockResolvedValue({ url: 'blob:preview', width: 160, height: 80 });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('is not offered to a FiringChief, nor asked for', async () => {
    let requests = 0;
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/federation', () => {
        requests++;
        return HttpResponse.json({ logo: LOGO });
      }),
    );
    await renderApp('/comparsas', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('table', { name: 'Comparsas' });
    expect(screen.queryByRole('region', { name: 'Logo de la Federación' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /logo de la Federación/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /, opciones$/ })).not.toBeInTheDocument();
    expect(requests).toBe(0);
  });

  it('says what the logo is for and offers an Admin to add it', async () => {
    federation(null);
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });

    const section = await federationSection();
    expect(within(section).getByText(/formulario de autorización de recogida/)).toBeInTheDocument();
    expect(
      await within(section).findByRole('button', { name: 'Añadir logo de la Federación' }),
    ).toBeInTheDocument();
  });

  it('uploads the cropped logo as PNG, starting with the whole image, and shows it', async () => {
    const user = userEvent.setup();
    const api = federation(null);
    const uploads = interceptUploads(() => {
      api.setLogo(NEW_LOGO);
      return uploaded();
    });
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();
    await within(section).findByRole('button', { name: 'Añadir logo de la Federación' });

    chooseLogo(section);
    const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la Federación' });
    await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

    await waitFor(() => {
      expect(
        within(section).getByRole('button', { name: 'Logo de la Federación, opciones' }).querySelector('img'),
      ).toHaveAttribute('src', `/api/federation-logo?v=${NEW_LOGO.version}`);
    });
    expect(uploads).toHaveLength(1);
    expect(cropImage).toHaveBeenCalledWith(
      expect.anything(),
      { x: 0, y: 0, width: 800, height: 400 },
      800,
      400,
      'png',
    );
  });

  it('replaces an existing logo', async () => {
    const user = userEvent.setup();
    const api = federation(LOGO);
    const uploads = interceptUploads(() => {
      api.setLogo(NEW_LOGO);
      return uploaded();
    });
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();
    await within(section).findByRole('button', { name: 'Logo de la Federación, opciones' });

    chooseLogo(section);
    const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la Federación' });
    await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

    await waitFor(() => {
      expect(
        within(section).getByRole('button', { name: 'Logo de la Federación, opciones' }).querySelector('img'),
      ).toHaveAttribute('src', `/api/federation-logo?v=${NEW_LOGO.version}`);
    });
    expect(uploads).toHaveLength(1);
  });

  it('refuses a logo that is too small before uploading it', async () => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:small', width: 200, height: 150 });
    federation(null);
    const uploads = interceptUploads(() => new HttpResponse(null, { status: 500 }));
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();
    await within(section).findByRole('button', { name: 'Añadir logo de la Federación' });

    chooseLogo(section);

    expect(await within(section).findByRole('alert')).toHaveTextContent('al menos 256 px');
    expect(uploads).toHaveLength(0);
  });

  it.each([
    [400, 'validation', { errors: { file: 'tooSmall' } }, 'El logo es demasiado pequeño'],
    [503, 'storage.unavailable', {}, 'El almacén de imágenes no responde ahora mismo.'],
    [429, 'tooManyRequests', {}, 'Has hecho demasiadas subidas seguidas'],
  ])('keeps the crop open with the translated reason (%i %s)', async (status, code, extra, reason) => {
    const user = userEvent.setup();
    federation(null);
    interceptUploads(() => problem(status, code, extra));
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();
    await within(section).findByRole('button', { name: 'Añadir logo de la Federación' });

    chooseLogo(section);
    const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la Federación' });
    await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

    expect(await within(dialog).findByText(reason, { exact: false })).toBeInTheDocument();
  });

  it('removes the logo only after a confirmation', async () => {
    const user = userEvent.setup();
    const api = federation(LOGO);
    let removals = 0;
    server.use(
      mock.delete('/api/federation-logo', () => {
        removals++;
        api.setLogo(null);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();

    await logoAction(user, section, 'Quitar');
    const confirmation = await screen.findByRole('alertdialog', {
      name: '¿Quitar el logo de la Federación?',
    });
    expect(within(confirmation).getByText(/sin logo/)).toBeInTheDocument();
    await user.click(within(confirmation).getByRole('button', { name: 'Quitar logo' }));

    expect(
      await within(section).findByRole('button', { name: 'Añadir logo de la Federación' }),
    ).toBeInTheDocument();
    expect(removals).toBe(1);
    expect(
      await within(section).findByText('Logo quitado', { selector: '[role=status]' }),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: 'Añadir logo de la Federación' })).toHaveFocus();
    });
  });

  it('warns that the logo may be outdated when the refresh after a change fails', async () => {
    const user = userEvent.setup();
    let failRefresh = false;
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/federation', () =>
        failRefresh ? problem(503, 'storage.unavailable') : HttpResponse.json({ logo: null }),
      ),
    );
    interceptUploads(() => {
      failRefresh = true;
      return uploaded();
    });
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();
    await within(section).findByRole('button', { name: 'Añadir logo de la Federación' });

    chooseLogo(section);
    const dialog = await screen.findByRole('dialog', { name: 'Recortar logo de la Federación' });
    await user.click(within(dialog).getByRole('button', { name: 'Usar logo' }));

    expect(await within(section).findByText(/puede que lo que ves no esté al día/)).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('treats a logo already removed elsewhere as removed', async () => {
    const user = userEvent.setup();
    const api = federation(LOGO);
    server.use(
      mock.delete('/api/federation-logo', () => {
        api.setLogo(null);
        return problem(404, 'logos.notFound');
      }),
    );
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();

    await logoAction(user, section, 'Quitar');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );

    expect(
      await within(section).findByRole('button', { name: 'Añadir logo de la Federación' }),
    ).toBeInTheDocument();
    expect(within(section).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says so when the logo cannot be loaded', async () => {
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/federation', () => problem(503, 'storage.unavailable')),
    );
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });

    const section = await federationSection();
    expect(await within(section).findByRole('alert')).toHaveTextContent(
      'No se ha podido cargar el logo de la Federación.',
    );
    expect(within(section).queryByRole('button', { name: /logo de la Federación/ })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /, opciones$/ })).not.toBeInTheDocument();

    server.use(mock.get('/api/federation', () => HttpResponse.json({ logo: null })));
    await userEvent.setup().click(within(section).getByRole('button', { name: 'Reintentar' }));

    expect(
      await within(section).findByRole('button', { name: 'Añadir logo de la Federación' }),
    ).toBeInTheDocument();
  });

  it('speaks English', async () => {
    federation(null);
    await renderApp('/comparsas', { session: SYNTHETIC_ADMIN, language: 'en' });

    const section = await federationSection('Federation logo');
    expect(await within(section).findByRole('button', { name: 'Add Federation logo' })).toBeInTheDocument();
  });

  it('has no automatically detectable accessibility violations', async () => {
    federation(LOGO);
    const { container } = await renderApp('/comparsas', { session: SYNTHETIC_ADMIN });
    const section = await federationSection();
    await within(section).findByRole('button', { name: 'Logo de la Federación, opciones' });

    expect(await axeViolations(container)).toEqual([]);
  });
});
