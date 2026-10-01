import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { PhotoUpload, type PhotoUploadProps } from './PhotoUpload';
import {
  cropToJpeg,
  loadImage,
  OversizedImageError,
  PhotoUploadFailure,
  previewOf,
  releaseImage,
  rotateImage,
  UnreadableImageError,
} from './photo-image';

// jsdom cannot decode images or draw on a canvas: the browser image work is replaced.
vi.mock('./photo-image', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./photo-image')>()),
  loadImage: vi.fn(),
  rotateImage: vi.fn(),
  cropToJpeg: vi.fn(),
  previewOf: vi.fn(),
  releaseImage: vi.fn(),
}));

const ID_RULES = { aspect: 3 / 4, minWidth: 600, minHeight: 800, maxWidth: 1200, maxHeight: 1600 };
const JPEG = new Blob([new Uint8Array([0xff, 0xd8])], { type: 'image/jpeg' });

function setup(props: Partial<PhotoUploadProps> = {}) {
  const onUpload = vi.fn<(photo: Blob) => Promise<void>>().mockResolvedValue(undefined);
  const view = renderWithProviders(
    <PhotoUpload
      label="foto de carnet"
      photoUrl={null}
      photoAlt="Foto de carnet de Arcabucera Sintética"
      onUpload={onUpload}
      {...ID_RULES}
      {...props}
    />,
  );
  return { onUpload, view };
}

/** The hidden file input behind the add/replace action. */
function fileInput(): HTMLInputElement {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) throw new Error('No file input rendered');
  return input;
}

function chooseFile(file = new File([new Uint8Array([1])], 'foto.jpg', { type: 'image/jpeg' })) {
  fireEvent.change(fileInput(), { target: { files: [file] } });
}

describe('PhotoUpload', () => {
  beforeEach(() => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:chosen', width: 1200, height: 1600 });
    vi.mocked(cropToJpeg).mockResolvedValue(JPEG);
    vi.mocked(previewOf).mockResolvedValue({ url: 'blob:preview', width: 160, height: 213 });
    vi.mocked(rotateImage).mockImplementation((image) =>
      Promise.resolve({ url: 'blob:rotated', width: image.height, height: image.width }),
    );
  });

  it('shows an empty state and an add action when there is no photo', async () => {
    const { view } = setup();
    await view;

    expect(screen.getByText('Sin foto de carnet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Añadir foto de carnet' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: /Quitar/ })).not.toBeInTheDocument();
  });

  it('shows the current photo with its description and offers replacing it', async () => {
    await setup({ photoUrl: '/api/arquebusiers/1/photos/id?v=2' }).view;

    expect(screen.getByRole('img', { name: 'Foto de carnet de Arcabucera Sintética' })).toHaveAttribute(
      'src',
      '/api/arquebusiers/1/photos/id?v=2',
    );
    expect(screen.getByRole('button', { name: 'Sustituir foto de carnet' })).toBeInTheDocument();
  });

  it('accepts images from the camera or the gallery, without forcing the camera', async () => {
    await setup().view;

    const input = fileInput();
    expect(input).toHaveAttribute('accept', 'image/*');
    expect(input).not.toHaveAttribute('capture');
  });

  it('says when the file is not a readable image, and uploads nothing', async () => {
    vi.mocked(loadImage).mockRejectedValue(new UnreadableImageError());
    const { onUpload, view } = setup();
    await view;

    chooseFile(new File(['%PDF'], 'documento.pdf', { type: 'application/pdf' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Este archivo no es una imagen que se pueda abrir',
    );
    expect(onUpload).not.toHaveBeenCalled();
  });

  it('says when the image is too small for the shape, before uploading', async () => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:small', width: 400, height: 500 });
    const { onUpload, view } = setup();
    await view;

    chooseFile();

    expect(await screen.findByRole('alert')).toHaveTextContent('La foto es demasiado pequeña');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(onUpload).not.toHaveBeenCalled();
  });

  it('refuses a huge file before decoding it', async () => {
    const { view } = setup();
    await view;
    const huge = new File([new Uint8Array([1])], 'enorme.jpg', { type: 'image/jpeg' });
    Object.defineProperty(huge, 'size', { value: 26 * 1024 * 1024 });

    chooseFile(huge);

    expect(await screen.findByRole('alert')).toHaveTextContent('demasiado grande');
    expect(loadImage).not.toHaveBeenCalled();
  });

  it('crops at the fixed shape and uploads only the cropped JPEG', async () => {
    const { onUpload, view } = setup();
    await view;

    chooseFile();
    const dialog = await screen.findByRole('dialog', { name: 'Recortar foto de carnet' });
    expect(screen.getByRole('group', { name: /Selección del recorte/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Usar foto' }));

    await waitFor(() => {
      expect(onUpload).toHaveBeenCalledWith(JPEG);
    });
    // 90 % of a 1200 × 1600 image at 3:4, centred: 1080 × 1440, under the maxima.
    expect(cropToJpeg).toHaveBeenCalledWith(
      expect.objectContaining({ url: 'blob:chosen' }),
      { x: 60, y: 80, width: 1080, height: 1440 },
      1080,
      1440,
    );
    await waitFor(() => {
      expect(dialog).not.toBeInTheDocument();
    });
  });

  it('rotates the image by quarter turns', async () => {
    const { onUpload, view } = setup({
      aspect: undefined,
      minWidth: undefined,
      minHeight: undefined,
      minLongSide: 800,
      maxWidth: 2000,
      maxHeight: 2000,
    });
    await view;
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:card', width: 1000, height: 700 });

    chooseFile();
    await userEvent.click(await screen.findByRole('button', { name: 'Girar a la derecha' }));
    await userEvent.click(screen.getByRole('button', { name: 'Usar foto' }));

    expect(rotateImage).toHaveBeenCalledWith(expect.objectContaining({ url: 'blob:card' }), 1);
    await waitFor(() => {
      expect(onUpload).toHaveBeenCalled();
    });
    expect(cropToJpeg).toHaveBeenCalledWith(
      expect.objectContaining({ url: 'blob:rotated' }),
      { x: 0, y: 0, width: 700, height: 1000 },
      700,
      1000,
    );
  });

  it('cancelling the crop with Escape uploads nothing and returns focus to the add action', async () => {
    const { onUpload, view } = setup();
    await view;

    chooseFile();
    await screen.findByRole('dialog');
    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(onUpload).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Añadir foto de carnet' })).toHaveFocus();
  });

  it('keeps the crop operable with the keyboard', async () => {
    await setup().view;

    chooseFile();
    await screen.findByRole('dialog');

    // The selection and its corner handles are focusable, named, and reached with Tab.
    const area = screen.getByRole('group', { name: /Selección del recorte/ });
    expect(area).toHaveAttribute('tabindex', '0');
    expect(screen.getAllByRole('button', { name: /Esquina/ }).length).toBeGreaterThanOrEqual(4);
  });

  it('keeps the crop open with the translated reason when the upload fails', async () => {
    const { onUpload, view } = setup();
    await view;
    onUpload.mockRejectedValue(
      new PhotoUploadFailure('La arcabucera necesita una licencia para añadir esta foto.'),
    );

    chooseFile();
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    const dialog = screen.getByRole('dialog');
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('La arcabucera necesita una licencia');
    // Retrying needs no new crop.
    onUpload.mockResolvedValue(undefined);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Usar foto' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('shows a generic message for an unexpected failure', async () => {
    const { onUpload, view } = setup();
    await view;
    onUpload.mockRejectedValue(new Error('network'));

    chooseFile();
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('No se ha podido guardar la foto');
  });

  it('says when the image has too many pixels', async () => {
    vi.mocked(loadImage).mockRejectedValue(new OversizedImageError());
    await setup().view;

    chooseFile();

    expect(await screen.findByRole('alert')).toHaveTextContent('demasiado grande');
  });

  it('refuses a free crop that is too elongated', async () => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:strip', width: 2400, height: 300 });
    const { onUpload, view } = setup({
      aspect: undefined,
      minWidth: undefined,
      minHeight: undefined,
      maxSideRatio: 2,
      minLongSide: 800,
      maxWidth: 2000,
      maxHeight: 2000,
    });
    await view;

    chooseFile();

    expect(await screen.findByRole('alert')).toHaveTextContent('demasiado alargada');
    expect(onUpload).not.toHaveBeenCalled();
  });

  it('accepts a landscape image for a portrait shape, since it can be turned', async () => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:landscape', width: 1000, height: 700 });
    await setup().view;

    chooseFile();

    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('previews the cropped result and releases images it no longer shows', async () => {
    await setup().view;

    chooseFile();

    expect(await screen.findByRole('img', { name: 'Vista previa de la foto recortada' })).toHaveAttribute(
      'src',
      'blob:preview',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => {
      expect(releaseImage).toHaveBeenCalledWith(expect.objectContaining({ url: 'blob:chosen' }));
    });
    expect(releaseImage).toHaveBeenCalledWith(expect.objectContaining({ url: 'blob:preview' }));
  });

  it('scales a large crop down to the maximum size at the exact shape', async () => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:large', width: 1800, height: 2400 });
    const { onUpload, view } = setup();
    await view;

    chooseFile();
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    await waitFor(() => {
      expect(onUpload).toHaveBeenCalled();
    });
    // 90 % of 1800 × 2400 is 1620 × 2160, scaled to fit 1200 × 1600.
    expect(cropToJpeg).toHaveBeenCalledWith(
      expect.anything(),
      { x: 90, y: 120, width: 1620, height: 2160 },
      1200,
      1600,
    );
  });

  it('announces the saved photo once the crop has closed, and returns focus to the add action', async () => {
    await setup().view;

    chooseFile();
    await userEvent.click(await screen.findByRole('button', { name: 'Usar foto' }));

    expect(await screen.findByText('Guardada: foto de carnet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Añadir foto de carnet' })).toHaveFocus();
  });

  it('replaces a photo that fails to load with a message', async () => {
    await setup({ photoUrl: '/api/arquebusiers/1/photos/id?v=2' }).view;

    fireEvent.error(screen.getByRole('img'));

    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByText('No se ha podido cargar la foto')).toBeInTheDocument();
  });

  it('asks before removing, and cancelling keeps the photo', async () => {
    const onRemove = vi.fn().mockResolvedValue(undefined);
    await setup({
      photoUrl: '/api/arquebusiers/1/photos/id?v=2',
      removal: {
        title: '¿Quitar la foto de carnet?',
        description: 'Se borrará la foto.',
        confirmLabel: 'Quitar foto',
        onRemove,
      },
    }).view;

    await userEvent.click(screen.getByRole('button', { name: 'Quitar foto de carnet' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Cancelar' }));

    expect(onRemove).not.toHaveBeenCalled();
  });

  it('explains why it is disabled', async () => {
    await setup({ disabled: true, disabledHint: 'Introduce primero la licencia.' }).view;

    expect(screen.getByRole('button', { name: 'Añadir foto de carnet' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Añadir foto de carnet' })).toHaveAccessibleDescription(
      'Introduce primero la licencia.',
    );
  });

  it('has no automatically detectable accessibility violations, empty or with a photo', async () => {
    const empty = await setup().view;
    expect(await axeViolations(empty.container)).toEqual([]);
    empty.unmount();

    const withPhoto = await setup({ photoUrl: '/api/arquebusiers/1/photos/id?v=2' }).view;
    expect(await axeViolations(withPhoto.container)).toEqual([]);
  });

  it('has no automatically detectable accessibility violations while cropping', async () => {
    await setup().view;

    chooseFile();
    const dialog = await screen.findByRole('dialog');

    expect(await axeViolations(dialog)).toEqual([]);
  });
});
