import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { PhotoUpload, type PhotoUploadProps } from './PhotoUpload';
import { cropImage, loadImage, previewOf } from './photo-image';

// Spec: Picture actions (refine-navigation-and-lists D7). jsdom cannot decode images or draw on a
// canvas: the browser image work is replaced, as in PhotoUpload.test.tsx.
vi.mock('./photo-image', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./photo-image')>()),
  loadImage: vi.fn(),
  rotateImage: vi.fn(),
  cropImage: vi.fn(),
  previewOf: vi.fn(),
  releaseImage: vi.fn(),
}));

const LOGO_RULES = {
  maxSideRatio: 3,
  minLongSide: 256,
  maxWidth: 1024,
  maxHeight: 1024,
  output: 'png' as const,
};
const PNG = new Blob([new Uint8Array([0x89, 0x50])], { type: 'image/png' });
const LOGO_URL = '/api/comparsas/1/logo?v=2';

function setup(props: Partial<PhotoUploadProps> = {}) {
  const onUpload = vi.fn<(photo: Blob) => Promise<void>>().mockResolvedValue(undefined);
  const onRemove = vi.fn<() => Promise<void>>().mockResolvedValue(undefined);
  const view = renderWithProviders(
    <PhotoUpload
      variant="picture"
      label="logo de la comparsa"
      photoUrl={LOGO_URL}
      photoAlt="Logo de Comparsa Norte"
      emptyText="Sin logo"
      removedText="Logo quitado"
      subject="logo"
      onUpload={onUpload}
      removal={{
        title: '¿Quitar el logo de Comparsa Norte?',
        description: 'Se borrará la imagen.',
        confirmLabel: 'Quitar logo',
        onRemove,
      }}
      {...LOGO_RULES}
      {...props}
    />,
  );
  return { onUpload, onRemove, view };
}

const trigger = () => screen.getByRole('button', { name: 'Logo de Comparsa Norte, opciones' });
const addButton = () => screen.getByRole('button', { name: 'Añadir logo de la comparsa' });

/** Lets a test change the logo after an upload or removal, as the owner's refetch does later. */
let setLogoUrl: (url: string | null) => void = () => undefined;

function Harness({ initialUrl }: { initialUrl: string | null }) {
  const [url, setUrl] = useState(initialUrl);
  useEffect(() => {
    setLogoUrl = setUrl;
  }, []);
  return (
    <>
      <PhotoUpload
        variant="picture"
        label="logo de la comparsa"
        photoUrl={url}
        photoAlt="Logo de Comparsa Norte"
        subject="logo"
        onUpload={() => Promise.resolve()}
        removal={{
          title: '¿Quitar el logo de Comparsa Norte?',
          description: 'Se borrará la imagen.',
          confirmLabel: 'Quitar logo',
          onRemove: () => Promise.resolve(),
        }}
        {...LOGO_RULES}
      />
      <button type="button">Otro control</button>
    </>
  );
}

function chooseFile() {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) throw new Error('No file input rendered');
  fireEvent.change(input, {
    target: { files: [new File([new Uint8Array([1])], 'logo.png', { type: 'image/png' })] },
  });
}

describe('PhotoUpload variant="picture"', () => {
  let pickFile: MockInstance<() => void>;

  beforeEach(() => {
    vi.mocked(loadImage).mockResolvedValue({ url: 'blob:logo', width: 600, height: 400 });
    vi.mocked(cropImage).mockResolvedValue(PNG);
    vi.mocked(previewOf).mockResolvedValue({ url: 'blob:preview', width: 160, height: 107 });
    // The file chooser cannot open in jsdom: record that it was asked to.
    pickFile = vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterEach(() => {
    pickFile.mockRestore();
  });

  it('makes the picture a button named after it, without separate replace or remove buttons', async () => {
    await setup().view;

    expect(trigger()).toBeInTheDocument();
    expect(within(trigger()).getByRole('presentation', { hidden: true })).toHaveAttribute('src', LOGO_URL);
    expect(screen.queryByRole('button', { name: /Sustituir|Quitar/ })).not.toBeInTheDocument();
  });

  it('opens a menu with replace and remove from the picture', async () => {
    const user = userEvent.setup();
    await setup().view;

    await user.click(trigger());

    const menu = await screen.findByRole('menu');
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Sustituir', 'Quitar']);
  });

  it('replaces from the keyboard, then returns focus to the picture after the crop', async () => {
    const user = userEvent.setup();
    const { onUpload, view } = setup();
    await view;

    trigger().focus();
    await user.keyboard('{Enter}');
    await user.click(await screen.findByRole('menuitem', { name: 'Sustituir' }));
    expect(pickFile).toHaveBeenCalledTimes(1);

    chooseFile();
    await user.click(await screen.findByRole('button', { name: 'Usar logo' }));

    expect(onUpload).toHaveBeenCalledWith(PNG);
    await waitFor(() => {
      expect(trigger()).toHaveFocus();
    });
  });

  it('opens the file chooser at once from the placeholder, without a menu', async () => {
    const user = userEvent.setup();
    await setup({ photoUrl: null }).view;

    await user.click(screen.getByRole('button', { name: 'Añadir logo de la comparsa' }));

    expect(pickFile).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('hints with an overlay on hover and keyboard focus, hidden from assistive technology', async () => {
    await setup().view;

    const overlay = trigger().querySelector('[data-picture-overlay]');
    expect(overlay).toHaveAttribute('aria-hidden', 'true');
    expect(overlay?.className).toMatch(/group-hover:opacity-100/);
    expect(overlay?.className).toMatch(/group-focus-visible:opacity-100/);
  });

  it('opens the menu on touch, without relying on hover', async () => {
    const user = userEvent.setup();
    await setup().view;

    await user.pointer({ keys: '[TouchA]', target: trigger() });

    expect(await screen.findByRole('menu')).toBeInTheDocument();
  });

  it('asks before removing; cancelling keeps the logo and returns focus to it', async () => {
    const user = userEvent.setup();
    const { onRemove, view } = setup();
    await view;

    await user.click(trigger());
    await user.click(await screen.findByRole('menuitem', { name: 'Quitar' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Quitar el logo de Comparsa Norte?' });
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    expect(onRemove).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(trigger()).toHaveFocus();
    });
  });

  it('removes after confirming, announces it and returns focus to the picture', async () => {
    const user = userEvent.setup();
    const { onRemove, view } = setup();
    await view;

    await user.click(trigger());
    await user.click(await screen.findByRole('menuitem', { name: 'Quitar' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );

    expect(onRemove).toHaveBeenCalledTimes(1);
    expect(await screen.findByText('Logo quitado', { selector: '[role=status]' })).toBeInTheDocument();
    await waitFor(() => {
      expect(trigger()).toHaveFocus();
    });
  });

  it('returns focus to the logo when it appears after the upload, though the button was replaced', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Harness initialUrl={null} />);

    await user.click(addButton());
    chooseFile();
    await user.click(await screen.findByRole('button', { name: 'Usar logo' }));
    await waitFor(() => {
      expect(addButton()).toHaveFocus();
    });
    act(() => {
      setLogoUrl(LOGO_URL);
    });

    expect(trigger()).toHaveFocus();
  });

  it('returns focus to the placeholder when the removed logo goes away after confirming', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Harness initialUrl={LOGO_URL} />);

    await user.click(trigger());
    await user.click(await screen.findByRole('menuitem', { name: 'Quitar' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );
    await waitFor(() => {
      expect(trigger()).toHaveFocus();
    });
    act(() => {
      setLogoUrl(null);
    });

    expect(addButton()).toHaveFocus();
  });

  it('does not take focus back once the user has moved on', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Harness initialUrl={LOGO_URL} />);

    await user.click(trigger());
    await user.click(await screen.findByRole('menuitem', { name: 'Quitar' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Quitar logo' }),
    );
    await waitFor(() => {
      expect(trigger()).toHaveFocus();
    });
    await user.click(screen.getByRole('button', { name: 'Otro control' }));
    act(() => {
      setLogoUrl(null);
    });

    expect(screen.getByRole('button', { name: 'Otro control' })).toHaveFocus();
  });

  it('names a logo that failed to load by what it shows first (WCAG 2.5.3)', async () => {
    await setup().view;

    fireEvent.error(within(trigger()).getByRole('presentation', { hidden: true }));

    expect(
      screen.getByRole('button', {
        name: 'No se ha podido cargar el logo. Logo de Comparsa Norte, opciones',
      }),
    ).toBeInTheDocument();
  });

  it('offers only replace when the logo cannot be removed', async () => {
    const user = userEvent.setup();
    await setup({ removal: undefined }).view;

    await user.click(trigger());

    expect(
      within(await screen.findByRole('menu'))
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Sustituir']);
  });

  it('explains why it is disabled and opens nothing', async () => {
    const user = userEvent.setup();
    await setup({ disabled: true, disabledHint: 'Solo con conexión.' }).view;

    expect(trigger()).toBeDisabled();
    expect(trigger()).toHaveAccessibleDescription('Solo con conexión.');
    await user.click(trigger());
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('stays enabled and focused while the chosen image is opened, marked as busy', async () => {
    vi.mocked(loadImage).mockReturnValue(new Promise(() => undefined));
    const user = userEvent.setup();
    await setup({ photoUrl: null }).view;

    await user.click(addButton());
    chooseFile();

    expect(addButton()).toBeEnabled();
    expect(addButton()).toHaveAttribute('aria-busy', 'true');
    expect(addButton()).toHaveFocus();
    await user.click(addButton());
    expect(pickFile).toHaveBeenCalledTimes(1);
  });

  it('returns focus to the logo when its menu closes with Escape', async () => {
    const user = userEvent.setup();
    await setup().view;

    await user.click(trigger());
    await screen.findByRole('menu');
    await user.keyboard('{Escape}');

    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(trigger()).toHaveFocus();
  });

  it('returns focus to the logo when the crop is cancelled, and uploads nothing', async () => {
    const user = userEvent.setup();
    const { onUpload, view } = setup();
    await view;

    await user.click(trigger());
    await user.click(await screen.findByRole('menuitem', { name: 'Sustituir' }));
    chooseFile();
    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));

    await waitFor(() => {
      expect(trigger()).toHaveFocus();
    });
    expect(onUpload).not.toHaveBeenCalled();
  });

  it('keeps the crop open with the reason when the upload fails', async () => {
    const user = userEvent.setup();
    const onUpload = vi.fn<(photo: Blob) => Promise<void>>().mockRejectedValue(new Error('network'));
    await setup({ onUpload }).view;

    await user.click(trigger());
    await user.click(await screen.findByRole('menuitem', { name: 'Sustituir' }));
    chooseFile();
    await user.click(await screen.findByRole('button', { name: 'Usar logo' }));

    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toBeInTheDocument();
  });

  it('offers no action when read-only', async () => {
    await setup({ readOnly: true }).view;

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Logo de Comparsa Norte' })).toBeInTheDocument();
  });

  it('has no automatically detectable accessibility violations, empty, with a logo and with its menu open', async () => {
    const empty = await setup({ photoUrl: null }).view;
    expect(await axeViolations(empty.container)).toEqual([]);
    empty.unmount();

    const withLogo = await setup().view;
    expect(await axeViolations(withLogo.container)).toEqual([]);

    await userEvent.click(trigger());
    expect(await axeViolations(await screen.findByRole('menu'))).toEqual([]);
  });
});
