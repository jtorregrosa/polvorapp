import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ArquebusierPhotoResponse, ArquebusierResponse } from '@/api/generated/model';
import { cropImage, loadImage, previewOf } from '@/components/app/photo-image';
import { problem, recordBodies, renderApp } from '@/test/app';
import { SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { DETAIL_UNO, NORTE, ROW_DOS, ROW_UNO } from '../test-data';

// jsdom cannot decode images or draw on a canvas: the browser image work is replaced.
vi.mock('@/components/app/photo-image', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/app/photo-image')>()),
  loadImage: vi.fn(),
  rotateImage: vi.fn(),
  cropImage: vi.fn(),
  previewOf: vi.fn(),
  releaseImage: vi.fn(),
}));

const JPEG = new Blob([new Uint8Array([0xff, 0xd8])], { type: 'image/jpeg' });
const ID_PHOTO: ArquebusierPhotoResponse = {
  kind: 'ID',
  version: '00000000-0000-4000-8000-000000000901',
  width: 600,
  height: 800,
  uploadedAt: '2026-10-01T10:00:00Z',
};
const FRONT: ArquebusierPhotoResponse = {
  ...ID_PHOTO,
  kind: 'LICENSE_FRONT',
  version: '00000000-0000-4000-8000-000000000902',
  width: 1000,
  height: 700,
};
const NO_PHOTOS = { id: null, licenseFront: null, licenseBack: null };

/** A recorded multipart upload: jsdom's FormData cannot cross MSW, so uploads are answered at fetch. */
interface Upload {
  url: string;
  file: FormDataEntryValue | null;
}

/** Answers `PUT …/photos/…` with `respond`, records the uploads, and lets every other call reach MSW. */
function interceptUploads(respond: (url: string) => Response): Upload[] {
  const uploads: Upload[] = [];
  const realFetch = globalThis.fetch;
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    if (url.includes('/photos/') && (init?.method ?? 'GET').toUpperCase() === 'PUT') {
      uploads.push({ url, file: init?.body instanceof FormData ? init.body.get('file') : null });
      return Promise.resolve(respond(url));
    }
    return realFetch(input, init);
  });
  return uploads;
}

function detail(current: () => ArquebusierResponse) {
  server.use(
    mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => HttpResponse.json(current())),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
    mock.get('/api/weapon-models', () => HttpResponse.json([])),
  );
}

/** Opens the license panel, chooses "no license" and saves, which asks to confirm (spec: Current license). */
async function removeLicense() {
  await userEvent.click(await screen.findByRole('button', { name: 'Editar licencia' }));
  const panel = await screen.findByRole('dialog', { name: 'Editar licencia' });
  await userEvent.click(within(panel).getByRole('radio', { name: 'Sin licencia' }));
  await userEvent.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));
  return panel;
}

function chooseFile(index = 0) {
  const input = document.querySelectorAll<HTMLInputElement>('input[type="file"]')[index];
  if (!input) throw new Error('No file input');
  fireEvent.change(input, {
    target: { files: [new File([new Uint8Array([1])], 'foto.jpg', { type: 'image/jpeg' })] },
  });
}

beforeEach(() => {
  vi.mocked(loadImage).mockResolvedValue({ url: 'blob:chosen', width: 1200, height: 1600 });
  vi.mocked(cropImage).mockResolvedValue(JPEG);
  vi.mocked(previewOf).mockResolvedValue({ url: 'blob:preview', width: 160, height: 213 });
  // jsdom has no object URLs; the register page shows the chosen photo through one.
  URL.createObjectURL = vi.fn(() => 'blob:chosen-photo');
  URL.revokeObjectURL = vi.fn();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('Photos on the detail page (spec: Photo screens, Private photo access)', () => {
  it('shows the ID photo with the personal data, by a versioned URL without personal data', async () => {
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, id: ID_PHOTO } }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    // The photo is its own button, named by what it shows, with a menu to replace or remove it.
    const photo = await screen.findByRole('button', {
      name: 'Foto de carnet de Arcabucero García Sintético, opciones',
    });
    expect(photo.querySelector('img')).toHaveAttribute(
      'src',
      `/api/arquebusiers/${DETAIL_UNO.id}/photos/id?v=${ID_PHOTO.version}`,
    );
    // Spec "Photo screens": the ID photo is in the record header, beside the name.
    const header = photo.closest('header');
    expect(header).toContainElement(
      screen.getByRole('heading', { level: 1, name: 'Arcabucero García Sintético' }),
    );
    await userEvent.click(photo);
    expect(await screen.findByRole('menuitem', { name: 'Sustituir' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Quitar' })).toBeInTheDocument();
  });

  it('says there is no ID photo, and offers adding one', async () => {
    detail(() => ({ ...DETAIL_UNO, photos: NO_PHOTOS }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    // The empty frame says what is missing and is the add action itself.
    expect(
      await screen.findByRole('button', { name: 'Sin foto de carnet Añadir foto de carnet' }),
    ).toBeEnabled();
  });

  it('offers license photos only while a license is saved, and says so', async () => {
    detail(() => ({ ...DETAIL_UNO, license: null, photos: NO_PHOTOS }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    expect(
      await screen.findByText('Las fotos de la licencia se pueden añadir cuando la licencia esté guardada.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /anverso de la licencia/ })).not.toBeInTheDocument();
  });

  it('groups each license side under its title', async () => {
    detail(() => ({ ...DETAIL_UNO, photos: NO_PHOTOS }));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const front = await screen.findByRole('group', { name: 'Anverso de la licencia' });
    expect(await within(front).findByRole('button', { name: 'Añadir anverso de la licencia' })).toBeEnabled();
    expect(within(front).getByText('Sin foto todavía')).toBeInTheDocument();
  });

  it('uploads a cropped photo and shows the new one', async () => {
    let current: ArquebusierResponse = { ...DETAIL_UNO, photos: NO_PHOTOS };
    detail(() => current);
    const uploads = interceptUploads(() => {
      current = { ...current, photos: { ...NO_PHOTOS, id: ID_PHOTO } };
      return HttpResponse.json(ID_PHOTO);
    });
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('button', { name: /Añadir foto de carnet$/ });
    chooseFile(0);
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    const photo = await screen.findByRole('button', { name: /^Foto de carnet de .*, opciones$/ });
    expect(photo.querySelector('img')).toHaveAttribute(
      'src',
      expect.stringContaining(`v=${ID_PHOTO.version}`),
    );
    expect(uploads.map((upload) => upload.url)).toEqual([`/api/arquebusiers/${DETAIL_UNO.id}/photos/id`]);
    expect(uploads[0]?.file).toBeInstanceOf(Blob);
  });

  it.each([
    [
      problem(409, 'photos.noLicense'),
      'Las fotos de la licencia solo se pueden añadir cuando el arcabucero tiene licencia.',
    ],
    [problem(503, 'storage.unavailable'), 'El almacén de fotos no responde ahora mismo.'],
    [
      problem(400, 'validation', { errors: { file: 'tooSmall' } }),
      'La foto es demasiado pequeña. Elige una con más resolución.',
    ],
  ])('shows the translated reason of a rejected upload', async (response, text) => {
    detail(() => ({ ...DETAIL_UNO, photos: NO_PHOTOS }));
    interceptUploads(() => response);
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('button', { name: 'Añadir anverso de la licencia' });
    chooseFile(1);
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent(text);
  });

  it('switches to the locked state when an upload is refused because the registry was locked meanwhile', async () => {
    let locked = false;
    detail(() => ({ ...DETAIL_UNO, photos: NO_PHOTOS }));
    server.use(mock.get('/api/registry/lock', () => HttpResponse.json({ locked, changedAt: null })));
    interceptUploads(() => {
      locked = true;
      return problem(409, 'registry.locked');
    });
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('button', { name: /Añadir foto de carnet$/ });
    chooseFile(0);
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent(
      'La Federación ha bloqueado el registro: puedes consultarlo, pero no cambiarlo.',
    );
    await userEvent.keyboard('{Escape}');
    expect(
      await screen.findByText(/Puedes consultar los arcabuceros, pero no cambiarlos/),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /Añadir foto de carnet$/ })).not.toBeInTheDocument();
    });
  });

  it('refreshes the page when an upload finds the license gone', async () => {
    let current: ArquebusierResponse = { ...DETAIL_UNO, photos: NO_PHOTOS };
    let reads = 0;
    server.use(
      mock.get(`/api/arquebusiers/${DETAIL_UNO.id}`, () => {
        reads += 1;
        return HttpResponse.json(current);
      }),
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/weapon-models', () => HttpResponse.json([])),
    );
    interceptUploads(() => {
      current = { ...current, license: null };
      return problem(409, 'photos.noLicense');
    });
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('button', { name: 'Añadir anverso de la licencia' });
    const before = reads;
    chooseFile(1);
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    await waitFor(() => {
      expect(reads).toBeGreaterThan(before);
    });
  });

  it('treats removing a photo that is already gone as done', async () => {
    let current: ArquebusierResponse = { ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } };
    detail(() => current);
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}/photos/license-front`, () => {
        current = { ...current, photos: NO_PHOTOS };
        return problem(404, 'photos.notFound');
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await userEvent.click(await screen.findByRole('button', { name: 'Quitar anverso de la licencia' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar el anverso de la licencia?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Quitar foto' }));

    expect(await screen.findByRole('button', { name: 'Añadir anverso de la licencia' })).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('keeps the confirmation open with the reason when a removal fails', async () => {
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } }));
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}/photos/license-front`, () =>
        problem(503, 'storage.unavailable'),
      ),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await userEvent.click(await screen.findByRole('button', { name: 'Quitar anverso de la licencia' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar el anverso de la licencia?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Quitar foto' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'El almacén de fotos no responde ahora mismo.',
    );
  });

  it('removes a photo after a confirmation', async () => {
    let current: ArquebusierResponse = { ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } };
    detail(() => current);
    server.use(
      mock.delete(`/api/arquebusiers/${DETAIL_UNO.id}/photos/license-front`, () => {
        current = { ...current, photos: NO_PHOTOS };
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await userEvent.click(await screen.findByRole('button', { name: 'Quitar anverso de la licencia' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar el anverso de la licencia?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Quitar foto' }));

    expect(await screen.findByRole('button', { name: 'Añadir anverso de la licencia' })).toBeInTheDocument();
  });

  it('asks before removing a license with photos, and cancelling saves nothing', async () => {
    const saved = recordBodies(() => HttpResponse.json(DETAIL_UNO));
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } }));
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, saved.resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await removeLicense();
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar la licencia y sus fotos?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    expect(saved.bodies).toEqual([]);
    // The panel stays open with the choice, to save something else or cancel.
    expect(within(panel).getByRole('radio', { name: 'Sin licencia' })).toHaveAttribute(
      'aria-checked',
      'true',
    );
  });

  it('lists the refused field after a confirmed license removal', async () => {
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } }));
    server.use(
      mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => problem(409, 'arquebusiers.nationalIdTaken')),
    );
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await removeLicense();
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar la licencia y sus fotos?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar y borrar las fotos' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    // A refusal about a field outside the panel is listed in its error summary (WCAG 3.3.1).
    expect(await within(panel).findByRole('group', { name: 'Hay un problema' })).toHaveTextContent(
      /contacta con la Federación/,
    );
  });

  it('shows why a confirmed license removal failed, once the confirmation has closed', async () => {
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } }));
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, () => problem(409, 'arquebusiers.modified')));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    const panel = await removeLicense();
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar la licencia y sus fotos?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar y borrar las fotos' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(await within(panel).findByRole('group', { name: 'Hay un problema' })).toHaveTextContent(
      /ha cambiado/,
    );
  });

  it('removes the license once confirmed', async () => {
    const saved = recordBodies(() => HttpResponse.json({ ...DETAIL_UNO, license: null, photos: NO_PHOTOS }));
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } }));
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, saved.resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await removeLicense();
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar la licencia y sus fotos?' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar y borrar las fotos' }));

    await waitFor(() => {
      expect(saved.bodies).toHaveLength(1);
    });
    expect(saved.bodies[0]).toMatchObject({ license: null });
  });

  it('reminds to replace the license photos after a renewal', async () => {
    const saved = recordBodies(() => HttpResponse.json(DETAIL_UNO));
    detail(() => ({ ...DETAIL_UNO, photos: { ...NO_PHOTOS, licenseFront: FRONT } }));
    server.use(mock.put(`/api/arquebusiers/${DETAIL_UNO.id}`, saved.resolver));
    await renderApp(`/arquebusiers/${DETAIL_UNO.id}`, { session: SYNTHETIC_FIRING_CHIEF });

    await userEvent.click(await screen.findByRole('button', { name: 'Editar licencia' }));
    const panel = await screen.findByRole('dialog', { name: 'Editar licencia' });
    fireEvent.change(within(panel).getByLabelText(/Fecha de expedición/), {
      target: { value: '2026-03-10' },
    });
    await userEvent.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => {
      expect(
        screen.getByText('Datos guardados. Si has renovado la licencia, sustituye también sus fotos.', {
          selector: '[role=status]',
        }),
      ).toBeInTheDocument();
    });
  });
});

describe('ID photo when registering (spec: Photo screens)', () => {
  async function registerWith(user: ReturnType<typeof userEvent.setup>) {
    await user.type(await screen.findByLabelText(/ID Unión/), '900001');
    await user.type(screen.getByLabelText(/^DNI\/NIE/), '12345678z');
    await user.type(screen.getByLabelText(/^Nombre/), 'Arcabucera');
    await user.type(screen.getByLabelText(/^Apellidos/), 'Sintética Nueva');
    fireEvent.change(screen.getByLabelText(/Fecha de nacimiento/), { target: { value: '1990-05-01' } });
    await user.click(screen.getByRole('radio', { name: 'Mujer' }));
  }

  function registration(created: ArquebusierResponse) {
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/weapon-models', () => HttpResponse.json([])),
      mock.post('/api/arquebusiers', () => HttpResponse.json(created, { status: 201 })),
      mock.get(`/api/arquebusiers/${created.id}`, () => HttpResponse.json(created)),
    );
  }

  it('uploads the chosen ID photo once the arquebusier is registered', async () => {
    const user = userEvent.setup();
    registration({ ...DETAIL_UNO, photos: NO_PHOTOS });
    const uploads = interceptUploads(() => HttpResponse.json(ID_PHOTO));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await registerWith(user);
    chooseFile(0);
    await user.click(await screen.findByRole('button', { name: 'Usar foto' }));
    expect(await screen.findByRole('img', { name: 'Foto de carnet elegida' })).toBeInTheDocument();
    expect(await screen.findByText('Foto de carnet elegida; se guardará al registrar.')).toBeInTheDocument();
    expect(uploads).toEqual([]);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(uploads.map((upload) => upload.url)).toEqual([`/api/arquebusiers/${DETAIL_UNO.id}/photos/id`]);
    });
    expect(await screen.findByText(/registrado./)).toBeInTheDocument();
  });

  it('opens the new arquebusier and says why the photo was not saved', async () => {
    const user = userEvent.setup();
    registration({ ...DETAIL_UNO, photos: NO_PHOTOS });
    interceptUploads(() => problem(503, 'storage.unavailable'));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await registerWith(user);
    chooseFile(0);
    await user.click(await screen.findByRole('button', { name: 'Usar foto' }));
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('se ha registrado, pero la foto de carnet no se ha guardado');
    expect(notice).toHaveTextContent('El almacén de fotos no responde ahora mismo.');
  });

  it('keeps the chosen photo while the registration is being sent', async () => {
    const user = userEvent.setup();
    registration({ ...DETAIL_UNO, photos: NO_PHOTOS });
    let release: () => void = () => undefined;
    server.use(
      mock.post('/api/arquebusiers', async () => {
        await new Promise<void>((resolve) => {
          release = resolve;
        });
        return HttpResponse.json({ ...DETAIL_UNO, photos: NO_PHOTOS }, { status: 201 });
      }),
    );
    interceptUploads(() => HttpResponse.json(ID_PHOTO));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await registerWith(user);
    chooseFile(0);
    await user.click(await screen.findByRole('button', { name: 'Usar foto' }));
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Sustituir foto de carnet' })).toBeDisabled();
    });
    release();
    expect(await screen.findByText(/registrado./)).toBeInTheDocument();
  });

  it('says the chosen photo was not saved when the registration answers without the new arquebusier', async () => {
    const user = userEvent.setup();
    registration({ ...DETAIL_UNO, photos: NO_PHOTOS });
    server.use(
      mock.post('/api/arquebusiers', () => new HttpResponse(null, { status: 201 })),
      mock.get('/api/arquebusiers', () => HttpResponse.json([])),
    );
    const uploads = interceptUploads(() => HttpResponse.json(ID_PHOTO));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await registerWith(user);
    chooseFile(0);
    await user.click(await screen.findByRole('button', { name: 'Usar foto' }));
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('la foto de carnet no se ha guardado');
    expect(uploads).toEqual([]);
  });

  it('registers without a photo when none is chosen', async () => {
    const user = userEvent.setup();
    registration({ ...DETAIL_UNO, photos: NO_PHOTOS });
    const uploads = interceptUploads(() => HttpResponse.json(ID_PHOTO));
    await renderApp('/arquebusiers/new', { session: SYNTHETIC_FIRING_CHIEF });

    await registerWith(user);
    await user.click(screen.getByRole('button', { name: 'Registrar arcabucero' }));

    expect(await screen.findByText(/registrado./)).toBeInTheDocument();
    expect(uploads).toEqual([]);
  });
});

describe('Missing ID photos in the list (spec: Photo screens)', () => {
  it('marks rows without an ID photo in words, and loads no images', async () => {
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/arquebusiers', () =>
        HttpResponse.json([
          { ...ROW_UNO, hasIdPhoto: true },
          { ...ROW_DOS, hasIdPhoto: false },
        ]),
      ),
    );
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    const withPhoto = (await screen.findByRole('link', { name: /García Sintético/ })).closest(
      'tr',
    ) as HTMLElement;
    const without = screen.getByRole('link', { name: /Ñúñez Sintética/ }).closest('tr') as HTMLElement;
    // Once per row: in its warnings, which already list it (UI audit).
    expect(without).toHaveTextContent('Sin foto de carnet');
    expect(within(without).queryByText('Sin foto de carnet')).not.toBeInTheDocument();
    expect(withPhoto).not.toHaveTextContent('Sin foto de carnet');
    expect(screen.queryAllByRole('img')).toEqual([]);
  });

  it('says "No ID photo" under the name when the warnings do not list it', async () => {
    server.use(
      mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
      mock.get('/api/arquebusiers', () =>
        HttpResponse.json([{ ...ROW_UNO, hasIdPhoto: false, warnings: [] }]),
      ),
    );
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    const row = (await screen.findByRole('link', { name: /García Sintético/ })).closest('tr') as HTMLElement;
    expect(within(row).getByText('Sin foto de carnet')).toBeInTheDocument();
  });
});
