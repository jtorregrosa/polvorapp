/** RFC 9457 problem details as returned by the API (spec: Problem details error responses). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  /** Stable, culture-independent error code the UI translates (e.g. `auth.invalidCode`). */
  code?: string;
  /** Field errors (`validation`) or failed password rules (`auth.invalidPassword`). */
  errors?: Record<string, string> | string[];
  /** The user an action created before it failed (an invitation whose email was not sent). */
  userId?: string;
  /** The columns a file problem is about (an import file missing or repeating columns). */
  columns?: unknown;
  /** The validation report of a refused import (`arquebusierImport.rowErrors`); check its shape before use. */
  report?: unknown;
  /** The fields an edition lacks to start (`editions.incomplete`); check each before use. */
  missing?: unknown;
  /** The year of the edition already in progress (`editions.anotherInProgress`); null when unknown. */
  inProgressYear?: unknown;
  /** The entries that block an order's submission (`orders.entriesInvalid`); check each before use. */
  entries?: unknown;
}

/**
 * An API response the UI cannot use: a non-2xx status, or a 2xx whose body is not JSON (for
 * example an HTML page from a proxy). `problem` is set when the body was a problem-details object.
 */
export class ApiProblemError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | undefined;

  constructor(status: number, problem: ProblemDetails | undefined) {
    super(problem?.title ?? `API request failed with status ${status}`);
    this.name = 'ApiProblemError';
    this.status = status;
    this.problem = problem;
  }
}

const DEFAULT_LANGUAGE = 'es-ES';

/** The active UI language; i18n keeps `<html lang>` in sync with it (UC-27). */
const activeLanguage = (): string => document.documentElement.lang || DEFAULT_LANGUAGE;

const isJson = (response: Response): boolean => (response.headers.get('Content-Type') ?? '').includes('json');

const readJson = async (response: Response): Promise<unknown> => {
  const text = await response.text();
  return text.length === 0 ? undefined : (JSON.parse(text) as unknown);
};

const readProblem = async (response: Response): Promise<ProblemDetails | undefined> => {
  if (!isJson(response)) {
    return undefined;
  }
  try {
    const body = await readJson(response);
    return body !== null && typeof body === 'object' && !Array.isArray(body) ? body : undefined;
  } catch {
    // A malformed error body still surfaces as ApiProblemError with its status.
    return undefined;
  }
};

/** A 2xx body: nothing (204, 202 Accepted…), or JSON. Anything else is not the API talking. */
const readData = async (response: Response): Promise<unknown> => {
  const text = response.status === 204 ? '' : await response.text();
  if (text.length === 0) {
    return undefined;
  }
  if (!isJson(response)) {
    throw new ApiProblemError(response.status, undefined);
  }
  try {
    return JSON.parse(text) as unknown;
  } catch {
    throw new ApiProblemError(response.status, undefined);
  }
};

const ANTIFORGERY_URL = '/api/auth/antiforgery';
const ANTIFORGERY_COOKIE = 'XSRF-TOKEN';
const ANTIFORGERY_HEADER = 'X-XSRF-TOKEN';
const ANTIFORGERY_PROBLEM = 'antiforgery.invalid';
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE']);

/** The request token the API put in a script-readable cookie (ADR-0004, design D6). */
const readAntiforgeryToken = (): string | undefined => {
  const raw = document.cookie
    .split('; ')
    .find((cookie) => cookie.startsWith(`${ANTIFORGERY_COOKIE}=`))
    ?.slice(ANTIFORGERY_COOKIE.length + 1);
  try {
    return raw === undefined ? undefined : decodeURIComponent(raw);
  } catch {
    return undefined; // A malformed cookie is no token: a fresh one is fetched.
  }
};

let refreshing: Promise<void> | undefined;

/**
 * Asks the API for a fresh anti-forgery token. Tokens are bound to the signed-in user, so call it
 * after signing in or out; concurrent callers share one request.
 */
export function refreshAntiforgeryToken(): Promise<void> {
  refreshing ??= fetch(ANTIFORGERY_URL, {
    credentials: 'same-origin',
    headers: { 'Accept-Language': activeLanguage() },
  })
    .then((response) => {
      if (!response.ok) {
        throw new ApiProblemError(response.status, undefined);
      }
    })
    .finally(() => {
      refreshing = undefined;
    });
  return refreshing;
}

/** Test hook: forgets an in-flight refresh between tests. */
export function resetAntiforgeryForTests(): void {
  refreshing = undefined;
}

/** The body type of the 2xx members of a generated-client response union. */
type SuccessData<TResponse> =
  Extract<TResponse, { status: 200 | 201 }> extends { data: infer TData } ? TData : never;

/**
 * The body of a successful generated-client response. Error statuses already threw; the contract
 * promises a body, so an empty one is treated as a failed call rather than typed as a value.
 */
export function responseData<TResponse extends { status: number; data: unknown }>(
  response: TResponse,
): SuccessData<TResponse> {
  if (response.data === undefined || response.data === null) {
    throw new ApiProblemError(response.status, undefined);
  }
  return response.data as SuccessData<TResponse>;
}

type UnauthorizedHandler = () => void;
let unauthorizedHandler: UnauthorizedHandler | undefined;

/**
 * Registers what happens when a protected call answers 401 (the session expired or was ended);
 * returns a function that unregisters it.
 */
export function setUnauthorizedHandler(handler: UnauthorizedHandler): () => void {
  unauthorizedHandler = handler;
  return () => {
    if (unauthorizedHandler === handler) {
      unauthorizedHandler = undefined;
    }
  };
}

/** Sign-in steps answer 401 as part of their flow: the page shows the error, not "session expired". */
const isSignInStep = (url: string): boolean =>
  new URL(url, window.location.origin).pathname.startsWith('/api/auth/');

/** A file the API sent, with the name it suggested. */
export interface DownloadedFile {
  blob: Blob;
  fileName: string | undefined;
}

/**
 * The file name of a `Content-Disposition` header (RFC 6266): the UTF-8 `filename*` (with or
 * without a language tag) when it decodes, else the plain `filename`, quoted or not. Undefined when
 * there is no usable name.
 */
function fileNameOf(disposition: string | null): string | undefined {
  if (!disposition) return undefined;
  const encoded = /filename\*\s*=\s*UTF-8'[^']*'([^;\s]+)/i.exec(disposition)?.[1];
  if (encoded) {
    try {
      const name = decodeURIComponent(encoded).trim();
      if (name) return name;
    } catch {
      // A malformed encoded name falls back to the plain one.
    }
  }
  const plain = /filename\s*=\s*(?:"((?:[^"\\]|\\.)*)"|([^;]+))/i.exec(disposition);
  const name = (plain?.[1]?.replace(/\\(.)/g, '$1') ?? plain?.[2])?.trim();
  return name === '' ? undefined : name;
}

/**
 * Downloads a file the API generates (e.g. the import template, add-registry-import D10):
 * `apiFetch` only accepts JSON bodies. Same credentials and `Accept-Language` as `apiFetch`, an
 * expired session is reported to the registered handler, and a refusal throws {@link ApiProblemError}.
 */
export async function apiDownload(url: string): Promise<DownloadedFile> {
  const response = await fetch(url, {
    credentials: 'same-origin',
    headers: { 'Accept-Language': activeLanguage() },
  });
  if (!response.ok) {
    if (response.status === 401 && !isSignInStep(url)) {
      unauthorizedHandler?.();
    }
    throw new ApiProblemError(response.status, await readProblem(response));
  }
  return { blob: await response.blob(), fileName: fileNameOf(response.headers.get('Content-Disposition')) };
}

/**
 * Mutator used by the orval-generated client: same-origin cookies, the UI language as
 * `Accept-Language`, the anti-forgery token on every write (fetched when missing, refreshed and
 * retried once when rejected), expired sessions reported to the registered handler, and unusable
 * responses thrown as {@link ApiProblemError}.
 */
export const apiFetch = async <T>(url: string, options: RequestInit): Promise<T> => {
  const method = (options.method ?? 'GET').toUpperCase();
  const write = !SAFE_METHODS.has(method);

  const send = async (): Promise<Response> => {
    const headers = new Headers(options.headers);
    if (!headers.has('Accept-Language')) {
      headers.set('Accept-Language', activeLanguage());
    }
    if (write) {
      if (!readAntiforgeryToken()) {
        await refreshAntiforgeryToken();
      }
      const token = readAntiforgeryToken();
      if (token) {
        headers.set(ANTIFORGERY_HEADER, token);
      }
    }
    return fetch(url, { ...options, method, headers, credentials: 'same-origin' });
  };

  let response = await send();
  if (write && response.status === 400) {
    const problem = await readProblem(response.clone());
    if ((problem as { code?: unknown } | undefined)?.code === ANTIFORGERY_PROBLEM) {
      await refreshAntiforgeryToken();
      response = await send();
    }
  }

  if (!response.ok) {
    if (response.status === 401 && !isSignInStep(url)) {
      unauthorizedHandler?.();
    }
    throw new ApiProblemError(response.status, await readProblem(response));
  }

  const data = await readData(response);
  // The generated fetch client types each call as `{ data, status, headers }` for its documented
  // responses; this is the only place that shape is asserted.
  return { data, status: response.status, headers: response.headers } as T;
};
