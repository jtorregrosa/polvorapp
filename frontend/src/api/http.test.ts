import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { server } from '@/test/server';
import { apiFetch, ApiProblemError } from './http';

interface Envelope<T> {
  data: T;
  status: number;
  headers: Headers;
}

/** Registers a handler for GET /api/probe and returns a getter for the request it received. */
function captureRequest(): () => Request {
  let captured: Request | undefined;
  server.use(
    mock.get('/api/probe', ({ request }) => {
      captured = request;
      return HttpResponse.json({});
    }),
  );
  return () => {
    if (!captured) throw new Error('No request reached /api/probe');
    return captured;
  };
}

describe('apiFetch', () => {
  it('returns data, status and headers of a successful response', async () => {
    server.use(mock.get('/api/system/info', () => HttpResponse.json({ version: '1.2.3', commit: 'abc' })));

    const response = await apiFetch<Envelope<{ version: string }>>('/api/system/info', { method: 'GET' });

    expect(response.status).toBe(200);
    expect(response.data.version).toBe('1.2.3');
    expect(response.headers).toBeInstanceOf(Headers);
  });

  it('sends the active UI language as Accept-Language', async () => {
    document.documentElement.lang = 'ca-ES-valencia';
    const request = captureRequest();

    await apiFetch('/api/probe', { method: 'GET' });

    expect(request().headers.get('Accept-Language')).toBe('ca-ES-valencia');
  });

  it('falls back to Spanish when the document has no language', async () => {
    document.documentElement.lang = '';
    const request = captureRequest();

    await apiFetch('/api/probe', { method: 'GET' });

    expect(request().headers.get('Accept-Language')).toBe('es-ES');
  });

  it('keeps an explicit Accept-Language header from the caller', async () => {
    const request = captureRequest();

    await apiFetch('/api/probe', { method: 'GET', headers: { 'Accept-Language': 'en' } });

    expect(request().headers.get('Accept-Language')).toBe('en');
  });

  it('sends same-origin credentials', async () => {
    const request = captureRequest();

    await apiFetch('/api/probe', { method: 'GET' });

    expect(request().credentials).toBe('same-origin');
  });

  it('throws an ApiProblemError carrying the problem details of an error response', async () => {
    const problem = {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
      title: 'Recurso no encontrado',
      status: 404,
      traceId: 'a'.repeat(32),
    };
    server.use(
      mock.get('/api/missing', () =>
        HttpResponse.json(problem, { status: 404, headers: { 'Content-Type': 'application/problem+json' } }),
      ),
    );

    await expect(apiFetch('/api/missing', { method: 'GET' })).rejects.toMatchObject({
      name: 'ApiProblemError',
      status: 404,
      problem,
    });
  });

  it('throws an ApiProblemError without details when the error body is not JSON', async () => {
    server.use(mock.get('/api/broken', () => new HttpResponse('Bad gateway', { status: 502 })));

    await expect(apiFetch('/api/broken', { method: 'GET' })).rejects.toMatchObject({
      name: 'ApiProblemError',
      status: 502,
      problem: undefined,
    });
  });

  it('throws an ApiProblemError without details when the problem JSON is malformed', async () => {
    server.use(
      mock.get('/api/broken', () =>
        HttpResponse.text('{ not json', {
          status: 500,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
      ),
    );

    await expect(apiFetch('/api/broken', { method: 'GET' })).rejects.toMatchObject({
      status: 500,
      problem: undefined,
    });
  });

  it('ignores a JSON error body that is not an object', async () => {
    server.use(mock.get('/api/broken', () => HttpResponse.json(['unexpected'], { status: 500 })));

    await expect(apiFetch('/api/broken', { method: 'GET' })).rejects.toMatchObject({
      status: 500,
      problem: undefined,
    });
  });

  it('accepts an empty successful response without a content type', async () => {
    document.cookie = 'XSRF-TOKEN=token; path=/';
    server.use(mock.post('/api/thing', () => new HttpResponse(null, { status: 202 })));

    const response = await apiFetch<Envelope<unknown>>('/api/thing', { method: 'POST' });

    expect(response.status).toBe(202);
    expect(response.data).toBeUndefined();
  });

  it('rejects a successful response whose body is not JSON', async () => {
    server.use(
      mock.get('/api/system/info', () =>
        HttpResponse.text('<html>proxy page</html>', { headers: { 'Content-Type': 'text/html' } }),
      ),
    );

    const error: unknown = await apiFetch('/api/system/info', { method: 'GET' }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiProblemError);
    expect(error).toMatchObject({ status: 200, problem: undefined });
  });

  it('returns undefined data for an empty successful response', async () => {
    document.cookie = 'XSRF-TOKEN=token; path=/';
    server.use(mock.delete('/api/thing', () => new HttpResponse(null, { status: 204 })));

    const response = await apiFetch<Envelope<unknown>>('/api/thing', { method: 'DELETE' });

    expect(response.status).toBe(204);
    expect(response.data).toBeUndefined();
  });

  it('sends a FormData upload untouched, without a Content-Type, with the anti-forgery token', async () => {
    document.cookie = 'XSRF-TOKEN=token; path=/';
    // jsdom's FormData cannot cross MSW's interceptor, so the request is inspected at fetch.
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(HttpResponse.json({}));
    const body = new FormData();
    body.append('file', new Blob([new Uint8Array([0xff, 0xd8])], { type: 'image/jpeg' }), 'photo.jpg');

    await apiFetch('/api/upload', { method: 'PUT', body });

    const init = fetchSpy.mock.calls[0]?.[1];
    const headers = new Headers(init?.headers);
    // The browser sets the multipart boundary; an explicit Content-Type would break the body.
    expect(headers.has('Content-Type')).toBe(false);
    expect(headers.get('X-XSRF-TOKEN')).toBe('token');
    expect(init?.body).toBe(body);
    fetchSpy.mockRestore();
  });

  it('propagates network failures', async () => {
    server.use(mock.get('/api/system/info', () => HttpResponse.error()));

    await expect(apiFetch('/api/system/info', { method: 'GET' })).rejects.toThrow(TypeError);
  });
});
