import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, fn, waitFor, within } from 'storybook/test';
import { PhotoUpload } from './PhotoUpload';

/** A synthetic placeholder portrait: flat shapes, never a real photo (SEC-11). */
const SYNTHETIC_PORTRAIT =
  'data:image/svg+xml,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="300" height="400" viewBox="0 0 300 400">' +
      '<rect width="300" height="400" fill="#c8d8e8"/>' +
      '<circle cx="150" cy="155" r="65" fill="#4a5568"/>' +
      '<ellipse cx="150" cy="400" rx="130" ry="140" fill="#4a5568"/>' +
      '</svg>',
  );

const meta = {
  title: 'Composites/PhotoUpload',
  component: PhotoUpload,
  args: {
    label: 'foto de carnet',
    photoUrl: null,
    photoAlt: 'Foto de carnet de Arcabucera Sintética Uno',
    aspect: 3 / 4,
    minWidth: 600,
    minHeight: 800,
    maxWidth: 1200,
    maxHeight: 1600,
    onUpload: fn(() => Promise.resolve()),
  },
} satisfies Meta<typeof PhotoUpload>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {};

export const WithPhoto: Story = {
  args: {
    photoUrl: SYNTHETIC_PORTRAIT,
    removal: {
      title: '¿Quitar la foto de carnet?',
      description: 'Se borrará la foto de Arcabucera Sintética Uno.',
      confirmLabel: 'Quitar foto',
      onRemove: fn(() => Promise.resolve()),
    },
  },
};

export const Disabled: Story = {
  args: {
    label: 'anverso de la licencia',
    disabled: true,
    disabledHint: 'Las fotos de la licencia se pueden añadir cuando la licencia esté guardada.',
  },
};

/** A file the browser cannot read as an image: the reason is shown and nothing is uploaded. */
export const UnreadableFile: Story = {
  play: async ({ canvasElement, args }) => {
    const input = canvasElement.querySelector<HTMLInputElement>('input[type="file"]');
    if (!input) throw new Error('No file input');
    const file = new File(['%PDF-1.7 synthetic'], 'documento.pdf', { type: 'application/pdf' });
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await waitFor(async () => {
      await expect(within(canvasElement).getByRole('alert')).toBeInTheDocument();
    });
    await expect(args.onUpload).not.toHaveBeenCalled();
  },
};

/** A synthetic emblem on a transparent background: flat shapes, never a real comparsa's logo (ADR-0012). */
const SYNTHETIC_LOGO =
  'data:image/svg+xml,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">' +
      '<circle cx="256" cy="256" r="220" fill="#2f6b4f"/>' +
      '<rect x="36" y="220" width="440" height="72" fill="#d9a441"/>' +
      '</svg>',
  );

/** Logo mode (add-comparsa-logos): free shape, transparency kept, PNG uploaded. */
export const TransparentLogo: Story = {
  args: {
    label: 'logo de la comparsa',
    photoUrl: SYNTHETIC_LOGO,
    photoAlt: 'Logo de Comparsa Sintética Este',
    aspect: undefined,
    minWidth: undefined,
    minHeight: undefined,
    maxSideRatio: 3,
    minLongSide: 256,
    maxWidth: 1024,
    maxHeight: 1024,
    output: 'png',
  },
};

export const LongValencian: Story = {
  args: {
    label: "revers de la llicència d'armes",
    photoUrl: SYNTHETIC_PORTRAIT,
    photoAlt: "Revers de la llicència d'armes d'Arcabucera Sintètica Dos",
  },
};
