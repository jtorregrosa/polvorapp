import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import type { i18n as I18n } from 'i18next';
import type { ReactElement } from 'react';
import { I18nextProvider } from 'react-i18next';
import type { AccountResponse } from '@/api/generated/model';
import { SESSION_QUERY_KEY } from '@/features/identity-access/session';
import { createI18n } from '@/i18n';
import { ThemeProvider } from '@/theme/ThemeProvider';

export interface ProvidersResult extends RenderResult {
  i18n: I18n;
  queryClient: QueryClient;
}

export interface ProvidersOptions {
  /** Starts signed in as this user (see `@/test/identity`), without asking the API. */
  session?: AccountResponse;
}

/**
 * Renders `ui` with a fresh i18n instance (in `language`) and a retry-free QueryClient, optionally
 * already signed in.
 */
export async function renderWithProviders(
  ui: ReactElement,
  language = 'es-ES',
  { session }: ProvidersOptions = {},
): Promise<ProvidersResult> {
  window.localStorage.setItem('polvorapp.language', language);
  const i18n = await createI18n();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (session) {
    queryClient.setQueryData(SESSION_QUERY_KEY, session);
  }

  const result = render(
    <QueryClientProvider client={queryClient}>
      <I18nextProvider i18n={i18n}>
        <ThemeProvider>{ui}</ThemeProvider>
      </I18nextProvider>
    </QueryClientProvider>,
  );
  return { ...result, i18n, queryClient };
}
