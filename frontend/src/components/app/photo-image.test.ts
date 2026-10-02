import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cropImage, loadImage, previewOf, rotateImage, type LoadedImage } from './photo-image';

/**
 * The encoding choices of the browser image work (add-comparsa-logos D8). jsdom has no image
 * decoding or canvas, so both are replaced: the fake image "loads" at once, and the fake canvas
 * records what it was asked to draw and encode.
 */
interface Encoding {
  type: string;
  quality: unknown;
}

const encodings: Encoding[] = [];
const fillRect = vi.fn();
let encodedType: ((requested: string) => string) | undefined;

class FakeImage {
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  naturalWidth = 800;
  naturalHeight = 400;

  set src(_url: string) {
    queueMicrotask(() => this.onload?.());
  }
}

const IMAGE: LoadedImage = { url: 'blob:working', width: 800, height: 400 };
const REGION = { x: 0, y: 0, width: 800, height: 400 };

describe('photo-image encoding', () => {
  beforeEach(() => {
    encodings.length = 0;
    fillRect.mockClear();
    encodedType = undefined;
    vi.stubGlobal('Image', FakeImage);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
      () =>
        ({
          fillRect,
          drawImage: vi.fn(),
          translate: vi.fn(),
          rotate: vi.fn(),
        }) as unknown as CanvasRenderingContext2D,
    );
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(
      function toBlob(callback, type, quality) {
        encodings.push({ type: type ?? 'image/png', quality });
        callback(new Blob([new Uint8Array([1])], { type: encodedType?.(type ?? '') ?? type }));
      },
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('crops photos to JPEG on white by default', async () => {
    const blob = await cropImage(IMAGE, REGION, 800, 400);

    expect(blob.type).toBe('image/jpeg');
    expect(encodings).toEqual([{ type: 'image/jpeg', quality: 0.9 }]);
    expect(fillRect).toHaveBeenCalled();
  });

  it('crops logos to PNG without filling the transparent areas', async () => {
    const blob = await cropImage(IMAGE, REGION, 800, 400, 'png');

    expect(blob.type).toBe('image/png');
    expect(encodings).toEqual([{ type: 'image/png', quality: undefined }]);
    expect(fillRect).not.toHaveBeenCalled();
  });

  it('keeps the working copy, the rotation and the preview in PNG for logos', async () => {
    await loadImage(new Blob([new Uint8Array([1])], { type: 'image/png' }), 'png');
    await rotateImage(IMAGE, 1, 'png');
    await previewOf(IMAGE, REGION, 160, 'png');

    expect(encodings.map((encoding) => encoding.type)).toEqual(['image/png', 'image/png', 'image/png']);
    expect(fillRect).not.toHaveBeenCalled();
  });

  it('keeps the working copy in JPEG for photos', async () => {
    await loadImage(new Blob([new Uint8Array([1])], { type: 'image/jpeg' }));

    expect(encodings).toEqual([{ type: 'image/jpeg', quality: 0.95 }]);
  });

  it('refuses an image the browser encoded in another format', async () => {
    // A browser that cannot encode the requested type silently falls back to PNG.
    encodedType = () => 'image/png';

    await expect(cropImage(IMAGE, REGION, 800, 400)).rejects.toThrow('could not be encoded');
  });
});
