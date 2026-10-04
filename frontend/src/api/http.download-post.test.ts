import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { server } from '@/test/server';
import { apiDownloadPost, ApiProblemError, setUnauthorizedHandler } from './http';

describe('apiDownloadPost', () => {
  let unregister: (() => void) | undefined;

  afterEach(() => {
    unregister?.();
    unregister = undefined;
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  });

  it('posts the body as JSON with the anti-forgery token and returns the named file', async () => {
    document.cookie = 'XSRF-TOKEN=token; path=/';
    let received: { body: unknown; token: string | null; type: string | null; url: string } | undefined;
    server.use(
      mock.post('/api/privacy/people/export', async ({ request }) => {
        received = {
          body: await request.json(),
          token: request.headers.get('X-XSRF-TOKEN'),
          type: request.headers.get('Content-Type'),
          url: request.url,
        };
        return new HttpResponse(new Uint8Array([0x50, 0x4b]), {
          headers: {
            'Content-Type': 'application/zip',
            'Content-Disposition': 'attachment; filename=polvorapp-personal-data-20301001.zip',
          },
        });
      }),
    );

    const file = await apiDownloadPost('/api/privacy/people/export', {
      nationalId: '00000000T',
      reference: 'REQ-1',
    });

    expect(file.fileName).toBe('polvorapp-personal-data-20301001.zip');
    expect(file.blob.size).toBe(2);
    expect(received?.body).toEqual({ nationalId: '00000000T', reference: 'REQ-1' });
    expect(received?.token).toBe('token');
    expect(received?.type).toContain('application/json');
    expect(received?.url).not.toContain('00000000T');
  });

  it('refreshes a rejected anti-forgery token and sends the request once more', async () => {
    document.cookie = 'XSRF-TOKEN=stale; path=/';
    const tokens: (string | null)[] = [];
    server.use(
      mock.get('/api/auth/antiforgery', () => {
        document.cookie = 'XSRF-TOKEN=fresh; path=/';
        return new HttpResponse(null, { status: 204 });
      }),
      mock.post('/api/privacy/people/export', ({ request }) => {
        tokens.push(request.headers.get('X-XSRF-TOKEN'));
        return tokens.length === 1
          ? HttpResponse.json(
              { status: 400, code: 'antiforgery.invalid' },
              { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
            )
          : new HttpResponse(new Uint8Array([0x50, 0x4b]), {
              headers: { 'Content-Disposition': 'attachment; filename=polvorapp-personal-data-20301001.zip' },
            });
      }),
    );

    const file = await apiDownloadPost('/api/privacy/people/export', { reference: 'REQ-1' });

    expect(tokens).toEqual(['stale', 'fresh']);
    expect(file.fileName).toBe('polvorapp-personal-data-20301001.zip');
  });

  it('throws the problem of a refusal', async () => {
    document.cookie = 'XSRF-TOKEN=token; path=/';
    server.use(
      mock.post('/api/privacy/people/export', () =>
        HttpResponse.json(
          { status: 404, code: 'privacy.notFound' },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const error = await apiDownloadPost('/api/privacy/people/export', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiProblemError);
    expect((error as ApiProblemError).problem?.code).toBe('privacy.notFound');
  });

  it('reports an expired session', async () => {
    document.cookie = 'XSRF-TOKEN=token; path=/';
    const expired = vi.fn();
    unregister = setUnauthorizedHandler(expired);
    server.use(mock.post('/api/privacy/users/1/export', () => new HttpResponse(null, { status: 401 })));

    await expect(
      apiDownloadPost('/api/privacy/users/1/export', { reference: 'REQ-1' }),
    ).rejects.toBeInstanceOf(ApiProblemError);
    expect(expired).toHaveBeenCalledOnce();
  });
});
