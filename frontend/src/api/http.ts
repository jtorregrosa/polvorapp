/** RFC 9457 problem details as returned by the API (spec: Problem details error responses). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
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

const readData = async (response: Response): Promise<unknown> => {
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined;
  }
  if (!isJson(response)) {
    throw new ApiProblemError(response.status, undefined);
  }
  try {
    return await readJson(response);
  } catch {
    throw new ApiProblemError(response.status, undefined);
  }
};

/**
 * Mutator used by the orval-generated client: same-origin cookies, the UI language as
 * `Accept-Language`, and unusable responses thrown as {@link ApiProblemError}.
 */
export const apiFetch = async <T>(url: string, options: RequestInit): Promise<T> => {
  const headers = new Headers(options.headers);
  if (!headers.has('Accept-Language')) {
    headers.set('Accept-Language', activeLanguage());
  }

  const response = await fetch(url, { ...options, headers, credentials: 'same-origin' });
  if (!response.ok) {
    throw new ApiProblemError(response.status, await readProblem(response));
  }

  const data = await readData(response);
  // The generated fetch client types each call as `{ data, status, headers }` for its documented
  // responses; this is the only place that shape is asserted.
  return { data, status: response.status, headers: response.headers } as T;
};
