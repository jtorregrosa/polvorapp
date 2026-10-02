/**
 * Browser image work for `PhotoUpload` (design D9): decoding, quarter turns and the cropped export,
 * as JPEG for photos or as PNG with transparency for logos (add-comparsa-logos D8). Kept apart
 * from the component so tests can replace it (jsdom has no image decoding or canvas; the encoding
 * choices are tested with a fake canvas in `photo-image.test.ts`). Current browsers (Chrome 81+, Firefox 77+, Safari 13.1+) apply the EXIF orientation when
 * decoding and drawing, so every size here is upright; older browsers are not supported.
 *
 * A chosen photo is decoded once and reduced to a bounded working copy (long side at most
 * {@link WORKING_LONG_SIDE}), which is what the user crops and rotates: phones take 12–200 MP
 * photos, and canvases that large fail or come out blank on iOS. The copy is still larger than any
 * stored photo (at most 1600 or 2000 px), so nothing is lost.
 */

/** Files larger than this are refused before decoding (the server accepts at most 10 MB after cropping). */
export const MAX_SOURCE_BYTES = 25 * 1024 * 1024;

/** Images with more pixels than this are refused right after decoding. */
export const MAX_SOURCE_PIXELS = 100_000_000;

/** Long side of the working copy the user crops. */
export const WORKING_LONG_SIDE = 2400;

const DECODE_TIMEOUT_MS = 30_000;
const WORKING_QUALITY = 0.95;
const JPEG_QUALITY = 0.9;

/** How images are encoded: JPEG (photos, white behind transparency) or PNG (logos, transparency kept). */
export type ImageOutput = 'jpeg' | 'png';

const MIME: Record<ImageOutput, 'image/jpeg' | 'image/png'> = { jpeg: 'image/jpeg', png: 'image/png' };

/** Lossless PNG takes no quality; JPEG uses the given one. */
function qualityFor(output: ImageOutput, jpegQuality: number): number | undefined {
  return output === 'jpeg' ? jpegQuality : undefined;
}

/** A decoded image ready to crop: an object URL for display and its upright pixel size. */
export interface LoadedImage {
  url: string;
  width: number;
  height: number;
}

/** A region of a {@link LoadedImage}, in its pixels. */
export interface PixelRegion {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** A failed upload whose message is already translated and can be shown as is. */
export class PhotoUploadFailure extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'PhotoUploadFailure';
  }
}

/** The file could not be decoded as an image by this browser. */
export class UnreadableImageError extends Error {
  constructor() {
    super('The file is not an image this browser can read.');
    this.name = 'UnreadableImageError';
  }
}

/** The image has more pixels than a browser can safely handle. */
export class OversizedImageError extends Error {
  constructor() {
    super('The image has too many pixels.');
    this.name = 'OversizedImageError';
  }
}

/** Loads an image through its load and error events, with a timeout: `decode()` alone rejects or hangs in some browsers. */
function decode(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const element = new Image();
    const timer = window.setTimeout(() => {
      reject(new UnreadableImageError());
    }, DECODE_TIMEOUT_MS);
    element.onload = () => {
      window.clearTimeout(timer);
      resolve(element);
    };
    element.onerror = () => {
      window.clearTimeout(timer);
      reject(new UnreadableImageError());
    };
    element.src = url;
  });
}

/** Draws on a canvas of the given size, encodes it and frees the canvas memory (Safari keeps it otherwise). */
async function render(
  width: number,
  height: number,
  type: 'image/jpeg' | 'image/png',
  quality: number | undefined,
  draw: (context: CanvasRenderingContext2D) => void,
): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  try {
    const context = canvas.getContext('2d');
    if (!context) {
      throw new Error('Canvas 2D is not available.');
    }
    context.imageSmoothingEnabled = true;
    context.imageSmoothingQuality = 'high';
    draw(context);
    const blob = await new Promise<Blob | null>((resolve) => {
      canvas.toBlob(resolve, type, quality);
    });
    // A browser that cannot encode the type falls back to PNG: never upload something else.
    if (blob?.type !== type) {
      throw new Error('The image could not be encoded.');
    }
    return blob;
  } finally {
    canvas.width = 0;
    canvas.height = 0;
  }
}

function loaded(blob: Blob, width: number, height: number): LoadedImage {
  return { url: URL.createObjectURL(blob), width, height };
}

/**
 * Decodes a chosen file into a bounded working copy. Rejects with {@link UnreadableImageError} when
 * the browser cannot read it and {@link OversizedImageError} when it has too many pixels. The copy
 * is PNG for `png` output, so transparency survives cropping.
 */
export async function loadImage(file: Blob, output: ImageOutput = 'jpeg'): Promise<LoadedImage> {
  const url = URL.createObjectURL(file);
  try {
    const source = await decode(url);
    const { naturalWidth: width, naturalHeight: height } = source;
    if (width === 0 || height === 0) {
      throw new UnreadableImageError();
    }
    if (width * height > MAX_SOURCE_PIXELS) {
      throw new OversizedImageError();
    }
    const scale = Math.min(1, WORKING_LONG_SIDE / Math.max(width, height));
    const [workingWidth, workingHeight] = [Math.round(width * scale), Math.round(height * scale)];
    const copy = await render(
      workingWidth,
      workingHeight,
      MIME[output],
      qualityFor(output, WORKING_QUALITY),
      (context) => {
        context.drawImage(source, 0, 0, workingWidth, workingHeight);
      },
    );
    return loaded(copy, workingWidth, workingHeight);
  } finally {
    URL.revokeObjectURL(url);
  }
}

/** The image turned a quarter clockwise (1) or anticlockwise (-1), as a new image. */
export async function rotateImage(
  image: LoadedImage,
  direction: 1 | -1,
  output: ImageOutput = 'jpeg',
): Promise<LoadedImage> {
  const source = await decode(image.url);
  const rotated = await render(
    image.height,
    image.width,
    MIME[output],
    qualityFor(output, WORKING_QUALITY),
    (context) => {
      context.translate(image.height / 2, image.width / 2);
      context.rotate((direction * Math.PI) / 2);
      context.drawImage(source, -image.width / 2, -image.height / 2);
    },
  );
  return loaded(rotated, image.height, image.width);
}

/**
 * The region of the image scaled to `width` × `height` and encoded freshly: no metadata survives.
 * JPEG puts white behind transparent areas; PNG keeps them.
 */
export async function cropImage(
  image: LoadedImage,
  region: PixelRegion,
  width: number,
  height: number,
  output: ImageOutput = 'jpeg',
): Promise<Blob> {
  const source = await decode(image.url);
  return render(width, height, MIME[output], qualityFor(output, JPEG_QUALITY), (context) => {
    if (output === 'jpeg') {
      context.fillStyle = 'white';
      context.fillRect(0, 0, width, height);
    }
    context.drawImage(source, region.x, region.y, region.width, region.height, 0, 0, width, height);
  });
}

/** A small copy of the region, `width` pixels wide, to preview the result before it is used. */
export async function previewOf(
  image: LoadedImage,
  region: PixelRegion,
  width: number,
  output: ImageOutput = 'jpeg',
): Promise<LoadedImage> {
  const height = Math.max(1, Math.round((region.height / region.width) * width));
  const blob = await cropImage(image, region, width, height, output);
  return loaded(blob, width, height);
}

/** Frees an image's object URL. */
export function releaseImage(image: LoadedImage | undefined): void {
  if (image) {
    URL.revokeObjectURL(image.url);
  }
}
