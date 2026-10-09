import { readFile } from 'node:fs/promises';
import { inflateRawSync } from 'node:zlib';
import type { Browser, Download, Page } from '@playwright/test';
import { FIRING_CHIEF_STATE } from '../identity';
import { expect } from '../fixtures';

/** What the distribution specs share: Norte's order, the seeded plan and putting both back. */

export const NORTE_ORDER = '0193a700-0000-7000-8000-000000000003';

export interface Day {
  id: string;
  type: 'POWDER' | 'WEAPONS';
  date: string;
  location: string;
  version: number;
  slots: { comparsaId: string; startsAt: string }[];
}

interface Plan {
  editionId: string;
  days: Day[];
}

export async function antiforgeryHeaders(page: Page): Promise<Record<string, string>> {
  await page.request.get('/api/auth/antiforgery');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  return { 'X-XSRF-TOKEN': decodeURIComponent(token?.value ?? '') };
}

export async function currentPlan(page: Page): Promise<Plan> {
  const current = (await (await page.request.get('/api/editions/current')).json()) as {
    edition: { id: string };
  };
  return (await (await page.request.get(`/api/distribution/editions/${current.edition.id}`)).json()) as Plan;
}

export const powderOf = (plan: Plan): Day => {
  const day = plan.days.find((candidate) => candidate.type === 'POWDER');
  if (!day) throw new Error('The seed has no powder day');
  return day;
};

export async function saved(download: Promise<Download>): Promise<{ name: string; bytes: Buffer }> {
  const file = await download;
  const path = await file.path();
  return { name: file.suggestedFilename(), bytes: await readFile(path) };
}

/** The text of every XML part of an Excel workbook (a zip), to look for values in it. */
export function workbookText(bytes: Buffer): string {
  const end = bytes.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (end < 0) throw new Error('Not a zip file');
  const entries = bytes.readUInt16LE(end + 10);
  let offset = bytes.readUInt32LE(end + 16);
  let text = '';
  for (let entry = 0; entry < entries; entry++) {
    const method = bytes.readUInt16LE(offset + 10);
    const compressedSize = bytes.readUInt32LE(offset + 20);
    const nameLength = bytes.readUInt16LE(offset + 28);
    const extraLength = bytes.readUInt16LE(offset + 30);
    const commentLength = bytes.readUInt16LE(offset + 32);
    const local = bytes.readUInt32LE(offset + 42);
    const name = bytes.toString('utf8', offset + 46, offset + 46 + nameLength);
    if (name.endsWith('.xml')) {
      const start = local + 30 + bytes.readUInt16LE(local + 26) + bytes.readUInt16LE(local + 28);
      const data = bytes.subarray(start, start + compressedSize);
      text += (method === 8 ? inflateRawSync(data) : data).toString('utf8');
    }
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return text;
}

export async function asFiringChief(browser: Browser) {
  const context = await browser.newContext({ storageState: FIRING_CHIEF_STATE, locale: 'es-ES' });
  return { context, page: await context.newPage() };
}

/**
 * Validates Norte's submitted order, so the current edition's lists have people. Already validated
 * (a run killed before restoring it) is fine.
 */
export async function validateNorte(page: Page): Promise<void> {
  const order = (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    status: string;
    version: number;
  };
  if (order.status === 'VALIDATED') return;
  const validated = await page.request.post(`/api/comparsa-orders/${NORTE_ORDER}/validate`, {
    headers: await antiforgeryHeaders(page),
    data: { version: order.version },
  });
  expect(validated.ok(), 'validating Norte for the lists').toBe(true);
}

/** Runs every restoration step, even when one fails, and reports all failures at the end. */
export async function restoreAll(steps: [string, () => Promise<void>][]): Promise<void> {
  const failures: string[] = [];
  for (const [what, step] of steps) {
    try {
      await step();
    } catch (error) {
      failures.push(`${what}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
  expect(failures, 'restoring the seeded state after the test').toEqual([]);
}

/** Puts Norte's order back as submitted, as other specs read it first. */
export async function restoreOrder(page: Page, browser: Browser): Promise<void> {
  const order = (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    status: string;
    version: number;
  };
  if (order.status !== 'VALIDATED') return;
  const returned = await page.request.post(`/api/comparsa-orders/${NORTE_ORDER}/return`, {
    headers: await antiforgeryHeaders(page),
    data: { version: order.version, reason: 'Devuelto por la prueba E2E.' },
  });
  expect(returned.ok(), 'returning the order').toBe(true);
  const { version } = (await (await page.request.get(`/api/comparsa-orders/${NORTE_ORDER}`)).json()) as {
    version: number;
  };
  const { context, page: chief } = await asFiringChief(browser);
  try {
    const submitted = await chief.request.post(`/api/comparsa-orders/${NORTE_ORDER}/submit`, {
      headers: await antiforgeryHeaders(chief),
      data: { version, attestation: true },
    });
    expect(submitted.ok(), 'submitting the order again').toBe(true);
  } finally {
    await context.close();
  }
}
