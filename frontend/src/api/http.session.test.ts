import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { server } from '@/test/server';
import { apiFetch, resetAntiforgeryForTests, setUnauthorizedHandler } from './http';

/** The anti-forgery endpoint as the API implements it: the request token in a readable cookie. */
function antiforgeryEndpoint(tokens: string[]): () => number {
  let calls = 0;
  server.use(
    mock.get('/api/auth/antiforgery', () => {
      document.cookie = `XSRF-TOKEN=${tokens[Math.min(calls, tokens.length - 1)]}; path=/`;
      calls += 1;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return () => calls;
}

function clearCookie(): void {
  document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
}

const antiforgeryProblem = () =>
  HttpResponse.json(
    { status: 400, code: 'antiforgery.invalid' },
    { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
  );

describe('apiFetch — anti-forgery (spec: Sessions)', () => {
  beforeEach(() => {
    clearCookie();
    resetAntiforgeryForTests();
  });

  it('fetches a token before the first write and sends it in X-XSRF-TOKEN', async () => {
    const calls = antiforgeryEndpoint(['token-1']);
    let header: string | null = null;
    server.use(
      mock.post('/api/thing', ({ request }) => {
        header = request.headers.get('X-XSRF-TOKEN');
        return new HttpResponse(null, { status: 204 });
      }),
    );

    await apiFetch('/api/thing', { method: 'POST' });

    expect(calls()).toBe(1);
    expect(header).toBe('token-1');
  });

  it('never sends the token on reads', async () => {
    const calls = antiforgeryEndpoint(['token-1']);
    let header: string | null = 'unset';
    server.use(
      mock.get('/api/thing', ({ request }) => {
        header = request.headers.get('X-XSRF-TOKEN');
        return HttpResponse.json({});
      }),
    );

    await apiFetch('/api/thing', { method: 'GET' });

    expect(calls()).toBe(0);
    expect(header).toBeNull();
  });

  it('refreshes the token once and retries when the API rejects it', async () => {
    const calls = antiforgeryEndpoint(['stale', 'fresh']);
    const received: (string | null)[] = [];
    server.use(
      mock.post('/api/thing', ({ request }) => {
        const token = request.headers.get('X-XSRF-TOKEN');
        received.push(token);
        return token === 'fresh' ? new HttpResponse(null, { status: 204 }) : antiforgeryProblem();
      }),
    );

    const response = await apiFetch<{ status: number }>('/api/thing', { method: 'POST' });

    expect(response.status).toBe(204);
    expect(received).toEqual(['stale', 'fresh']);
    expect(calls()).toBe(2);
  });

  it('does not retry more than once', async () => {
    antiforgeryEndpoint(['stale']);
    server.use(mock.post('/api/thing', antiforgeryProblem));

    await expect(apiFetch('/api/thing', { method: 'POST' })).rejects.toMatchObject({ status: 400 });
  });
});

describe('apiFetch — expired sessions', () => {
  const handler = vi.fn();
  let unregister: () => void = () => undefined;

  beforeEach(() => {
    handler.mockReset();
    unregister = setUnauthorizedHandler(handler);
  });

  afterEach(() => {
    unregister();
  });

  it('reports a 401 from a protected call to the session handler', async () => {
    server.use(mock.get('/api/users', () => HttpResponse.json({ status: 401 }, { status: 401 })));

    await expect(apiFetch('/api/users', { method: 'GET' })).rejects.toMatchObject({ status: 401 });

    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('does not report a 401 from a sign-in step', async () => {
    antiforgeryEndpoint(['token']);
    server.use(mock.post('/api/auth/login', () => HttpResponse.json({ status: 401 }, { status: 401 })));

    await expect(apiFetch('/api/auth/login', { method: 'POST' })).rejects.toMatchObject({ status: 401 });

    expect(handler).not.toHaveBeenCalled();
  });
});
