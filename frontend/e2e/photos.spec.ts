import type { Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from './identity';
import { expect, test as base, waitForShell } from './fixtures';

/**
 * Arquebusier photos (change add-arquebusier-photos) against the seeded stack. Each test registers
 * the arquebusier it changes and deletes it again; the images are synthetic shapes drawn in the
 * browser, never real photos (SEC-11). The stored JPEG is read back and inspected, so the engine's
 * own decoding (EXIF orientation, canvas export) is checked end to end in every browser project.
 */

/** "Arcabucera Sintética Seis", seeded in Comparsa Sintética Sur with an ID photo: outside the FiringChief's scope. */
const SEEDED_IN_SUR = '0193a300-0000-7000-8000-000000000006';
/** The seeded arquebusier with all three photos; only opened, never changed. */
const SEEDED_ALL_PHOTOS = '0193a300-0000-7000-8000-000000000001';
const LETTERS = 'TRWAGMYFPDXBNJZSQVHLCKE';
/** APP1 (EXIF/XMP), APP2 (ICC), APP13 (IPTC), APP14 (Adobe) and comments: none may be stored. */
const METADATA_MARKERS = [0xe1, 0xe2, 0xed, 0xee, 0xfe];
const BACKGROUND = '#c8d8e8';
const CIRCLE = '#4a5568';

let identities = 0;

function syntheticIdentity(): { nationalId: string; federationId: string; lastName: string } {
  const second = Math.floor(Date.now() / 1000) % 100_000;
  identities += 1;
  // A range of its own (the registry suite stays below 9 000 000), within the 8 digits of a DNI.
  const number = 20_000_000 + ((second * 90 + test.info().parallelIndex * 9 + (identities % 9)) % 9_000_000);
  return {
    nationalId: `${String(number).padStart(8, '0')}${LETTERS[number % LETTERS.length]}`,
    federationId: String(number),
    lastName: `Sintética Fotos ${number}`,
  };
}

async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

const test = base.extend<{ deleteAfter: (id: string) => void }>({
  deleteAfter: async ({ page }, use) => {
    const ids: string[] = [];
    await use((id) => ids.push(id));
    if (ids.length === 0) return;
    const headers = await antiforgeryHeaders(page);
    const failed: string[] = [];
    for (const id of ids) {
      const response = await page.request.delete(`/api/arquebusiers/${id}`, { headers });
      if (![204, 404].includes(response.status())) failed.push(`${id}: ${response.status()}`);
    }
    expect(failed, 'cleanup of registered arquebusiers').toEqual([]);
  },
});

interface Drawing {
  width: number;
  height: number;
  /** The circle's centre, as fractions of the width and height. */
  circle: readonly [number, number];
}

/** A synthetic image drawn in the page: a flat background with an off-centre circle, no text or faces. */
async function syntheticImage(
  page: Page,
  drawing: Drawing,
  type: 'image/png' | 'image/jpeg',
): Promise<Buffer> {
  const dataUrl = await page.evaluate(
    ({ width, height, circle, type, background, fill }) => {
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('No canvas');
      context.fillStyle = background;
      context.fillRect(0, 0, width, height);
      context.fillStyle = fill;
      context.beginPath();
      context.arc(width * circle[0], height * circle[1], Math.min(width, height) / 5, 0, Math.PI * 2);
      context.fill();
      return canvas.toDataURL(type, 0.95);
    },
    { ...drawing, type, background: BACKGROUND, fill: CIRCLE },
  );
  return Buffer.from(dataUrl.split(',')[1] ?? '', 'base64');
}

/**
 * The JPEG with its APP0 replaced by an EXIF block: orientation 6 (turn 90° clockwise to display)
 * and a camera make, as a phone writes them.
 */
function withExifOrientation6(jpeg: Buffer): Buffer {
  const tiff = Buffer.from([
    0x49,
    0x49,
    0x2a,
    0x00,
    0x08,
    0x00,
    0x00,
    0x00, // little-endian TIFF, IFD0 at 8
    0x02,
    0x00, // two entries
    0x0f,
    0x01,
    0x02,
    0x00,
    0x04,
    0x00,
    0x00,
    0x00,
    0x53,
    0x79,
    0x6e,
    0x00, // Make = "Syn"
    0x12,
    0x01,
    0x03,
    0x00,
    0x01,
    0x00,
    0x00,
    0x00,
    0x06,
    0x00,
    0x00,
    0x00, // Orientation = 6
    0x00,
    0x00,
    0x00,
    0x00, // no next IFD
  ]);
  const payload = Buffer.concat([Buffer.from('Exif\0\0', 'latin1'), tiff]);
  const app1 = Buffer.alloc(4);
  app1.writeUInt16BE(0xffe1, 0);
  app1.writeUInt16BE(payload.length + 2, 2);
  const afterApp0 = jpeg[3] === 0xe0 ? 4 + jpeg.readUInt16BE(4) : 2;
  return Buffer.concat([jpeg.subarray(0, 2), app1, payload, jpeg.subarray(afterApp0)]);
}

/** The markers of a JPEG up to the start of scan, and the frame size. */
function jpegInfo(bytes: Buffer): { markers: number[]; width: number; height: number } {
  const markers: number[] = [];
  let width = 0;
  let height = 0;
  let position = 2;
  while (position + 4 <= bytes.length && bytes[position] === 0xff) {
    const marker = bytes[position + 1] ?? 0;
    if (marker === 0xda) break;
    markers.push(marker);
    if (marker === 0xc0 || marker === 0xc2) {
      height = bytes.readUInt16BE(position + 5);
      width = bytes.readUInt16BE(position + 7);
    }
    position += 2 + bytes.readUInt16BE(position + 2);
  }
  return { markers, width, height };
}

/** Whether the stored image is dark (the circle) at each point, decoded by the browser under test. */
async function darkAt(page: Page, url: string, points: [number, number][]): Promise<boolean[]> {
  return page.evaluate(
    async ({ url, points }) => {
      const bitmap = await createImageBitmap(await (await fetch(url)).blob());
      const canvas = document.createElement('canvas');
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
      const context = canvas.getContext('2d');
      if (!context) throw new Error('No canvas');
      context.drawImage(bitmap, 0, 0);
      return points.map(([x, y]) => {
        const [r = 0, g = 0, b = 0] = context.getImageData(x, y, 1, 1).data;
        return r + g + b < 3 * 128;
      });
    },
    { url, points },
  );
}

/** Reads a stored photo, checks how it is served and returns its bytes. */
async function storedPhoto(page: Page, url: string): Promise<Buffer> {
  const response = await page.request.get(url);
  expect(response.status()).toBe(200);
  const headers = response.headers();
  expect(headers['content-type']).toBe('image/jpeg');
  expect(headers['cache-control']).toContain('no-store');
  expect(headers['x-content-type-options']).toBe('nosniff');
  expect(headers['content-disposition']).toBe('inline; filename="photo.jpg"');
  const bytes = await response.body();
  expect([bytes[0], bytes[1]]).toEqual([0xff, 0xd8]);
  expect(jpegInfo(bytes).markers.filter((marker) => METADATA_MARKERS.includes(marker))).toEqual([]);
  return bytes;
}

/**
 * Waits for the dialog's opening animations (overlay fade, zoom) to end: mid-fade, colours blend
 * with the page behind and contrast checks depend on the engine's timing.
 */
async function animationsDone(page: Page): Promise<void> {
  await page.evaluate(() =>
    Promise.all(
      document
        .getAnimations()
        .filter((animation) => animation.effect?.getComputedTiming().iterations !== Infinity)
        .map((animation) => animation.finished),
    ),
  );
}

/** Registers an arquebusier (cleaned up after the test), optionally with an issued AE license. */
async function register(
  page: Page,
  deleteAfter: (id: string) => void,
  { license = false } = {},
): Promise<{ id: string; lastName: string }> {
  const identity = syntheticIdentity();
  await page.goto('/arquebusiers/new');
  await waitForShell(page);
  await page.getByRole('combobox', { name: 'Comparsa' }).selectOption({ label: 'Comparsa Sintética Norte' });
  await page.getByLabel(/ID Unión/).fill(identity.federationId);
  await page.getByLabel(/^DNI\/NIE/).fill(identity.nationalId);
  await page.getByLabel(/^Nombre/).fill('Arcabucera');
  await page.getByLabel(/^Apellidos/).fill(identity.lastName);
  await page.getByLabel(/Fecha de nacimiento/).fill('1990-05-01');
  await page.getByRole('radio', { name: 'Mujer' }).click();
  if (license) {
    await page.getByRole('radio', { name: /^AE/ }).click();
    await page.getByLabel(/Fecha de expedición/).fill('2025-03-10');
  }
  const created = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/arquebusiers',
  );
  await page.getByRole('button', { name: 'Registrar arcabucero' }).click();
  const response = await created;
  expect(response.status()).toBe(201);
  const { id } = (await response.json()) as { id: string };
  deleteAfter(id);
  await expect(page).toHaveURL(new RegExp(`/arquebusiers/${id}$`));
  // Let the detail page finish loading: navigating away mid-request aborts its fetches, which
  // WebKit reports as access-control errors.
  await waitForShell(page);
  return { id, lastName: identity.lastName };
}

/** Chooses a file through the visible button, as a person does, and waits for the crop dialog. */
async function chooseFile(page: Page, button: string, file: Buffer, type = 'image/png') {
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: button }).click();
  await (
    await chooser
  ).setFiles({
    name: type === 'image/png' ? 'sintetica.png' : 'sintetica.jpg',
    mimeType: type,
    buffer: file,
  });
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('img', { name: 'Vista previa de la foto recortada' })).toBeVisible();
  return dialog;
}

/** Confirms the crop and waits until the page shows the stored photo; returns its URL. */
async function confirmAndWait(
  page: Page,
  photoName: RegExp | string,
  previous?: string | null,
): Promise<string> {
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Usar foto' }).click();
  await expect(dialog).toBeHidden();
  const photo = page.getByRole('img', { name: photoName });
  await expect(photo).toBeVisible();
  if (previous) await expect(photo).not.toHaveAttribute('src', previous);
  // Decoded by the engine itself, not only present with its alt text.
  await expect.poll(() => photo.evaluate((image: HTMLImageElement) => image.naturalWidth)).toBeGreaterThan(0);
  return (await photo.getAttribute('src')) ?? '';
}

test.describe('arquebusier photos', () => {
  test('applies the EXIF orientation, stores a clean 3:4 JPEG and clears the list marker', async ({
    page,
    deleteAfter,
    axeViolations,
  }) => {
    const { id, lastName } = await register(page, deleteAfter);

    await page.goto('/arquebusiers');
    await waitForShell(page);
    await page.getByRole('searchbox').fill(lastName);
    // A table row on wide screens, a stacked item on a phone.
    await expect(
      page
        .getByRole('row')
        .or(page.getByRole('listitem'))
        .filter({ hasText: lastName })
        // Exactly the marker: the warnings line also names a missing ID photo.
        .getByText('Sin foto de carnet', { exact: true }),
    ).toBeVisible();

    await page.goto(`/arquebusiers/${id}`);
    await waitForShell(page);
    await expect(page.getByText('Sin foto de carnet')).toBeVisible();
    // Stored sideways (1200 × 900) with the circle on the left: upright it is 900 × 1200 with the
    // circle in the upper part, as a phone photo held upright.
    const sideways = await syntheticImage(
      page,
      { width: 1200, height: 900, circle: [1 / 2.6, 0.5] },
      'image/jpeg',
    );
    const dialog = await chooseFile(
      page,
      'Añadir foto de carnet',
      withExifOrientation6(sideways),
      'image/jpeg',
    );
    await animationsDone(page);
    // The crop dialog; the page behind it is inert and covered by the overlay.
    expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);
    const url = await confirmAndWait(page, `Foto de carnet de Arcabucera ${lastName}`);
    await expect(dialog).toBeHidden();

    expect(url).toMatch(new RegExp(`^/api/arquebusiers/${id}/photos/id\\?v=[0-9a-f-]{36}$`));
    const { width, height } = jpegInfo(await storedPhoto(page, url));
    // The initial selection: 90 % of the upright 900 × 1200, centred.
    expect({ width, height }).toEqual({ width: 810, height: 1080 });
    // Upright circle centre (450, 462), minus the selection's offset (45, 60); its mirror is background.
    expect(
      await darkAt(page, url, [
        [405, 402],
        [405, 678],
      ]),
    ).toEqual([true, false]);

    await page.goto('/arquebusiers');
    await waitForShell(page);
    await page.getByRole('searchbox').fill(lastName);
    const row = page.getByRole('row').or(page.getByRole('listitem')).filter({ hasText: lastName });
    await expect(row).toBeVisible();
    await expect(row.getByText('Sin foto de carnet', { exact: true })).toHaveCount(0);
  });

  test('rotates a license photo both ways, replaces it and removes it after a confirmation', async ({
    page,
    deleteAfter,
  }) => {
    const { id } = await register(page, deleteAfter, { license: true });
    await page.goto(`/arquebusiers/${id}`);
    await waitForShell(page);
    // 1200 × 760 with the circle in the left part.
    const landscape = await syntheticImage(
      page,
      { width: 1200, height: 760, circle: [1 / 2.6, 0.5] },
      'image/png',
    );
    const front = /^Anverso de la licencia de/;

    await chooseFile(page, 'Añadir anverso de la licencia', landscape);
    await page.getByRole('button', { name: 'Girar a la derecha' }).click();
    const right = await confirmAndWait(page, front);
    expect(jpegInfo(await storedPhoto(page, right))).toMatchObject({ width: 760, height: 1200 });
    // Turned clockwise, the left part is now at the top.
    expect(
      await darkAt(page, right, [
        [380, 462],
        [380, 738],
      ]),
    ).toEqual([true, false]);

    await chooseFile(page, 'Sustituir anverso de la licencia', landscape);
    await page.getByRole('button', { name: 'Girar a la izquierda' }).click();
    const left = await confirmAndWait(page, front, right);
    expect(jpegInfo(await storedPhoto(page, left))).toMatchObject({ width: 760, height: 1200 });
    expect(
      await darkAt(page, left, [
        [380, 738],
        [380, 462],
      ]),
    ).toEqual([true, false]);

    await page.getByRole('button', { name: 'Quitar anverso de la licencia' }).click();
    const confirm = page.getByRole('alertdialog', { name: '¿Quitar el anverso de la licencia?' });
    await confirm.getByRole('button', { name: 'Cancelar' }).click();
    await expect(page.getByRole('img', { name: front })).toBeVisible();

    await page.getByRole('button', { name: 'Quitar anverso de la licencia' }).click();
    await confirm.getByRole('button', { name: 'Quitar foto' }).click();
    await expect(page.getByRole('button', { name: 'Añadir anverso de la licencia' })).toBeVisible();
    expect((await page.request.get(`/api/arquebusiers/${id}/photos/license-front`)).status()).toBe(404);
  });

  test('crops with the keyboard only, keeping the 3:4 shape, and returns the focus', async ({
    page,
    deleteAfter,
  }) => {
    const { id, lastName } = await register(page, deleteAfter);
    await page.goto(`/arquebusiers/${id}`);
    await waitForShell(page);
    const upright = await syntheticImage(
      page,
      { width: 900, height: 1200, circle: [0.5, 1 / 2.6] },
      'image/png',
    );

    const dialog = await chooseFile(page, 'Añadir foto de carnet', upright);
    await dialog.getByRole('button', { name: /^Esquina inferior derecha/ }).focus();
    await page.keyboard.press('Shift+ArrowLeft');
    await page.keyboard.press('Shift+ArrowLeft');
    await dialog.getByRole('group', { name: /^Selección del recorte/ }).focus();
    await page.keyboard.press('ArrowLeft');
    await dialog.getByRole('button', { name: 'Usar foto' }).focus();
    await page.keyboard.press('Enter');
    await expect(dialog).toBeHidden();

    const photo = page.getByRole('img', { name: `Foto de carnet de Arcabucera ${lastName}` });
    await expect(photo).toBeVisible();
    const { width, height } = jpegInfo(await storedPhoto(page, (await photo.getAttribute('src')) ?? ''));
    expect(width).toBeLessThan(810);
    expect(width).toBeGreaterThanOrEqual(600);
    expect(Math.abs(width * 4 - height * 3)).toBeLessThanOrEqual(4);
    await expect(page.getByRole('button', { name: 'Sustituir foto de carnet' })).toBeFocused();
  });

  test('crops with the buttons only, without dragging, keeping the 3:4 shape (SC 2.5.7)', async ({
    page,
    deleteAfter,
  }) => {
    const { id, lastName } = await register(page, deleteAfter);
    const upright = await syntheticImage(page, { width: 900, height: 1200, circle: [0.5, 0.4] }, 'image/png');
    const dialog = await chooseFile(page, 'Añadir foto de carnet', upright);
    const tools = dialog.getByRole('group', { name: 'Ajustar el recorte' });

    // From 90 % centred: two steps smaller, then up and left until the corner.
    await tools.getByRole('button', { name: 'Hacer el recorte más pequeño' }).click();
    await tools.getByRole('button', { name: 'Hacer el recorte más pequeño' }).click();
    for (let step = 0; step < 3; step += 1) {
      await tools.getByRole('button', { name: 'Mover el recorte hacia arriba' }).click();
      await tools.getByRole('button', { name: 'Mover el recorte a la izquierda' }).click();
    }
    const url = await confirmAndWait(page, `Foto de carnet de Arcabucera ${lastName}`);

    expect(url).toMatch(new RegExp(`^/api/arquebusiers/${id}/photos/id`));
    const { width, height } = jpegInfo(await storedPhoto(page, url));
    // 80 % of 900 × 1200 at the top left corner: 720 × 960, still 3:4.
    expect({ width, height }).toEqual({ width: 720, height: 960 });
  });

  test('a FiringChief cannot read or change the photos of another comparsa', async ({ page, browser }) => {
    // Positive control: the photo exists and the Admin reads it.
    await page.goto('/');
    await waitForShell(page);
    expect((await page.request.get(`/api/arquebusiers/${SEEDED_IN_SUR}/photos/id`)).status()).toBe(200);

    const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
    try {
      const chief = await context.newPage();
      await chief.goto('/');
      await waitForShell(chief);
      const read = await chief.request.get(`/api/arquebusiers/${SEEDED_IN_SUR}/photos/id`);
      expect(read.status()).toBe(404);
      expect(read.headers()['content-type']).not.toBe('image/jpeg');
      const headers = await antiforgeryHeaders(chief);
      const removal = await chief.request.delete(`/api/arquebusiers/${SEEDED_IN_SUR}/photos/id`, { headers });
      expect(removal.status()).toBe(404);
      const upload = await chief.request.put(`/api/arquebusiers/${SEEDED_IN_SUR}/photos/id`, {
        headers,
        multipart: {
          file: {
            name: 'sintetica.png',
            mimeType: 'image/png',
            buffer: await syntheticImage(chief, { width: 600, height: 800, circle: [0.5, 0.4] }, 'image/png'),
          },
        },
      });
      expect(upload.status()).toBe(404);
    } finally {
      await context.close();
    }
    expect((await page.request.get(`/api/arquebusiers/${SEEDED_IN_SUR}/photos/id`)).status()).toBe(200);
  });
});

test.describe('the crop dialog at 320 px', () => {
  test.use({ viewport: { width: 320, height: 568 } });

  test('keeps every control reachable without horizontal scrolling', async ({ page, axeViolations }) => {
    await page.goto(`/arquebusiers/${SEEDED_ALL_PHOTOS}`);
    await waitForShell(page);
    const portrait = await syntheticImage(
      page,
      { width: 900, height: 1200, circle: [0.5, 0.4] },
      'image/png',
    );
    const dialog = await chooseFile(page, 'Sustituir foto de carnet', portrait);

    expect(await dialog.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
    for (const name of ['Girar a la izquierda', 'Girar a la derecha', 'Usar foto', 'Cancelar']) {
      const button = dialog.getByRole('button', { name });
      await button.scrollIntoViewIfNeeded();
      const box = await button.boundingBox();
      expect(box, name).not.toBeNull();
      expect((box?.x ?? -1) >= 0 && (box?.x ?? 0) + (box?.width ?? 0) <= 320, name).toBe(true);
    }
    await animationsDone(page);
    // The crop dialog; the page behind it is inert and covered by the overlay.
    expect(await axeViolations(page, '[role="dialog"]')).toEqual([]);

    // Nothing is changed: the dialog is cancelled.
    await dialog.getByRole('button', { name: 'Cancelar' }).click();
    await expect(dialog).toBeHidden();
  });
});
