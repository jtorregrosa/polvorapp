import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { getAccount, getGetAccountQueryKey, updateLocale } from '@/api/generated/account/account';
import { logout } from '@/api/generated/auth/auth';
import type { AccountResponse } from '@/api/generated/model';
import { ApiProblemError, refreshAntiforgeryToken } from '@/api/http';
import { matchLanguage, rememberLanguage, type Language } from '@/i18n/config';

export const SESSION_QUERY_KEY = getGetAccountQueryKey();

/**
 * Announced on `window` when a session ends without the sign-out warning (e.g. "sign out
 * everywhere"), with the user's id, for whoever keeps that user's data on the device to clear it
 * (distribution spec: Data kept on the device).
 */
export const SESSION_ENDED_EVENT = 'polvorapp:session-ended';

export type Session =
  | { status: 'loading'; account?: undefined }
  | { status: 'signedOut'; account?: undefined }
  | { status: 'signedIn'; account: AccountResponse };

/** Reads the session for `SESSION_QUERY_KEY`: the account, or `null` when signed out (a 401). */
export async function fetchSession(signal: AbortSignal): Promise<AccountResponse | null> {
  try {
    return (await getAccount({ signal })).data as AccountResponse;
  } catch (error) {
    if (error instanceof ApiProblemError && error.status === 401) {
      return null;
    }
    throw error;
  }
}

/** The current session from `GET /api/account`: a 401 means "signed out", not an error. */
export function useSession(): Session {
  const query = useQuery({
    queryKey: SESSION_QUERY_KEY,
    queryFn: ({ signal }) => fetchSession(signal),
    staleTime: 60_000,
    retry: false,
  });

  if (query.data) {
    return { status: 'signedIn', account: query.data };
  }
  if (query.isPending) {
    return { status: 'loading' };
  }
  if (query.isError) {
    throw query.error; // The route's error page shows it.
  }
  return { status: 'signedOut' };
}

/** Control characters and backslashes, which browsers may strip or read as `/`. */
// eslint-disable-next-line no-control-regex -- matching control characters is the point.
const UNSAFE_PATH_CHARACTERS = /[\u0000-\u001f\u007f\\]/;

/** A same-origin path to return to after sign-in; anything else becomes the start page. */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value?.startsWith('/') || UNSAFE_PATH_CHARACTERS.test(value)) {
    return '/';
  }
  const url = new URL(value, window.location.origin);
  const path = `${url.pathname}${url.search}${url.hash}`;
  // Dot segments normalise `/.//evil.example` to the protocol-relative `//evil.example`.
  return url.origin === window.location.origin && !path.startsWith('//') ? path : '/';
}

/** Why the sign-in page is shown again: the session expired, or a sign-in step did. */
export type SignInReason = 'expired' | 'stepExpired';

/** `/login?returnTo=…` for a location (path and query) to come back to. */
export function signInPath(pathname: string, search: string, reason?: SignInReason): string {
  const params = new URLSearchParams({ returnTo: `${pathname}${search}` });
  if (reason) {
    params.set('reason', reason);
  }
  return `/login?${params.toString()}`;
}

/**
 * Loads the signed-in user's session into the cache and applies their language (spec: Switch UI
 * language — preferred language applied at sign-in). Call after any step that ends in DONE.
 */
export function useCompleteSignIn(): () => Promise<void> {
  const queryClient = useQueryClient();
  const { i18n } = useTranslation();

  return useCallback(async () => {
    await refreshAntiforgeryToken();
    const account = await queryClient.query({
      queryKey: SESSION_QUERY_KEY,
      queryFn: async () => (await getAccount()).data as AccountResponse,
      staleTime: 0, // A cached "signed out" must not survive signing in.
    });
    const language = matchLanguage(account.locale);
    if (language && language !== i18n.resolvedLanguage) {
      rememberLanguage(language);
      await i18n.changeLanguage(language);
    }
  }, [queryClient, i18n]);
}

/**
 * Saves a language switch as the signed-in user's preferred language (spec: Switch UI language).
 * Resolves to `false` when it could not be saved; the UI keeps the new language either way.
 */
export function useSaveLanguage(): (language: Language) => Promise<boolean> {
  const queryClient = useQueryClient();

  return useCallback(
    async (language: Language) => {
      if (!queryClient.getQueryData(SESSION_QUERY_KEY)) {
        return true; // Signed out: the browser remembers it.
      }
      try {
        await updateLocale({ locale: language });
      } catch {
        return false;
      }
      queryClient.setQueryData<AccountResponse | null>(SESSION_QUERY_KEY, (account) =>
        account ? { ...account, locale: language } : account,
      );
      return true;
    },
    [queryClient],
  );
}

/**
 * Forgets every cached answer and shows the sign-in page, for when the server has already ended
 * the session (e.g. "sign out everywhere").
 */
export function useForgetSession(): () => Promise<void> {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  return useCallback(async () => {
    const userId = queryClient.getQueryData<AccountResponse | null>(SESSION_QUERY_KEY)?.id;
    if (userId) {
      // Whoever keeps data of this user on the device (the capture store, SEC-14) clears it, told
      // while the signed-in pages that listen are still mounted.
      window.dispatchEvent(new CustomEvent(SESSION_ENDED_EVENT, { detail: userId }));
    }
    // Leave the signed-in pages first, so none renders (or redirects) without its session.
    await navigate('/login');
    queryClient.clear();
    queryClient.setQueryData(SESSION_QUERY_KEY, null);
    // Best effort: without a fresh token the next write fetches one and retries (apiFetch).
    await refreshAntiforgeryToken().catch(() => undefined);
  }, [queryClient, navigate]);
}

/**
 * Signs out, then forgets the session. Resolves to `false` — and stays signed in — when the server
 * could not end the session (offline, server error), so the caller can say so.
 */
export function useSignOut(): () => Promise<boolean> {
  const forgetSession = useForgetSession();

  return useCallback(async () => {
    try {
      await logout();
    } catch (error) {
      if (!(error instanceof ApiProblemError && error.status === 401)) {
        return false;
      }
    }
    await forgetSession();
    return true;
  }, [forgetSession]);
}
