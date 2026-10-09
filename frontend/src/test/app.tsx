import { http as mock, HttpResponse, type HttpResponseResolver } from 'msw';
import { createMemoryRouter, RouterProvider } from 'react-router';
import type { AccountResponse } from '@/api/generated/model';
import { appRoutes } from '@/app/routes';
import { renderWithProviders, type ProvidersResult } from './render';
import { server } from './server';

export interface AppResult extends ProvidersResult {
  router: ReturnType<typeof createMemoryRouter>;
  /** The current path and query, as the address bar would show it. */
  location: () => string;
}

export interface AppOptions {
  language?: string;
  /** Starts signed in as this user; otherwise `GET /api/account` answers 401. */
  session?: AccountResponse;
  /**
   * Answers `GET /api/account` instead, with nothing known of the session beforehand: e.g. a server
   * that cannot be reached, or a session that is only known after a while.
   */
  account?: HttpResponseResolver;
}

/**
 * Renders the real route table at `path`, with the calls every page makes (system info, the
 * anti-forgery token, the session) answered. Tests add the calls they are about with `server.use`.
 */
export async function renderApp(
  path: string,
  { language = 'es-ES', session, account }: AppOptions = {},
): Promise<AppResult> {
  server.use(
    mock.get('/api/system/info', () => HttpResponse.json({ version: '1.4.0', commit: 'abc1234' })),
    mock.get('/api/auth/antiforgery', () => new HttpResponse(null, { status: 204 })),
    mock.get(
      '/api/account',
      account ?? (() => (session ? HttpResponse.json(session) : problem(401, 'auth.unauthenticated'))),
    ),
  );
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  const result = await renderWithProviders(<RouterProvider router={router} />, language, {
    session: account ? undefined : session,
  });
  return {
    ...result,
    router,
    location: () => `${router.state.location.pathname}${router.state.location.search}`,
  };
}

/** A problem-details response as the API sends it. */
export function problem(status: number, code: string, extra: Record<string, unknown> = {}) {
  return HttpResponse.json(
    { status, title: 'Problem', code, ...extra },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

/** Records the JSON bodies a handler receives. */
export function recordBodies(respond: HttpResponseResolver): {
  bodies: unknown[];
  resolver: HttpResponseResolver;
} {
  const bodies: unknown[] = [];
  return {
    bodies,
    resolver: async (info) => {
      bodies.push(await info.request.clone().json());
      return respond(info);
    },
  };
}
