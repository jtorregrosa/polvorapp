import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { server } from '@/test/server';
import { apiDownload, ApiProblemError, setUnauthorizedHandler } from './http';

const XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

describe('apiDownload', () => {
  let unregister: (() => void) | undefined;

  afterEach(() => {
    unregister?.();
    unregister = undefined;
    document.documentElement.lang = '';
  });

  /** The file name `apiDownload` reads from a `Content-Disposition` header. */
  async function nameFrom(disposition: string): Promise<string | undefined> {
    server.use(
      mock.get(
        '/api/files/template',
        () =>
          new HttpResponse(new Uint8Array([1]), {
            headers: { 'Content-Type': XLSX, 'Content-Disposition': disposition },
          }),
      ),
    );
    return (await apiDownload('/api/files/template')).fileName;
  }

  it('reads the encoded name with a language tag, and falls back when it does not decode', async () => {
    expect(await nameFrom(`attachment; filename*=UTF-8'es'plantilla%20arcabuceros.xlsx`)).toBe(
      'plantilla arcabuceros.xlsx',
    );
    expect(await nameFrom(`attachment; filename=plantilla.xlsx; filename*=UTF-8''%E0%A4%A.xlsx`)).toBe(
      'plantilla.xlsx',
    );
  });

  it('keeps a semicolon inside a quoted name and ignores an empty one', async () => {
    expect(await nameFrom('attachment; filename="plantilla;2026.xlsx"')).toBe('plantilla;2026.xlsx');
    expect(await nameFrom('attachment; filename= ;')).toBeUndefined();
  });

  it('returns the file with the name the server gives, asking in the UI language', async () => {
    document.documentElement.lang = 'en';
    let language: string | null = null;
    server.use(
      mock.get('/api/files/template', ({ request }) => {
        language = request.headers.get('Accept-Language');
        return new HttpResponse(new Uint8Array([1, 2, 3]), {
          headers: {
            'Content-Type': XLSX,
            'Content-Disposition': `attachment; filename=plantilla.xlsx; filename*=UTF-8''plantilla-arcabuceros.xlsx`,
          },
        });
      }),
    );

    const file = await apiDownload('/api/files/template');

    expect(language).toBe('en');
    expect(file.fileName).toBe('plantilla-arcabuceros.xlsx');
    expect(file.blob.size).toBe(3);
  });

  it('reads a plain quoted file name', async () => {
    server.use(
      mock.get(
        '/api/files/template',
        () =>
          new HttpResponse(new Uint8Array([1]), {
            headers: { 'Content-Type': XLSX, 'Content-Disposition': 'attachment; filename="plantilla.xlsx"' },
          }),
      ),
    );

    expect((await apiDownload('/api/files/template')).fileName).toBe('plantilla.xlsx');
  });

  it('has no file name when the server gives none', async () => {
    server.use(
      mock.get(
        '/api/files/template',
        () => new HttpResponse(new Uint8Array([1]), { headers: { 'Content-Type': XLSX } }),
      ),
    );

    expect((await apiDownload('/api/files/template')).fileName).toBeUndefined();
  });

  it('throws an ApiProblemError with the problem of a refused download', async () => {
    server.use(
      mock.get('/api/files/template', () =>
        HttpResponse.json(
          { status: 403, code: 'forbidden' },
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const error = await apiDownload('/api/files/template').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiProblemError);
    expect((error as ApiProblemError).status).toBe(403);
    expect((error as ApiProblemError).problem?.code).toBe('forbidden');
  });

  it('reports an expired session to the session handler', async () => {
    const handler = vi.fn();
    unregister = setUnauthorizedHandler(handler);
    server.use(mock.get('/api/files/template', () => new HttpResponse(null, { status: 401 })));

    await expect(apiDownload('/api/files/template')).rejects.toBeInstanceOf(ApiProblemError);
    expect(handler).toHaveBeenCalledTimes(1);
  });
});
