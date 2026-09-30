import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import type { i18n as I18n } from 'i18next';
import type { ReactElement } from 'react';
import { I18nextProvider } from 'react-i18next';
import { createI18n } from '@/i18n';

export interface ProvidersResult extends RenderResult {
  i18n: I18n;
}

/** Renders `ui` with a fresh i18n instance (in `language`) and a retry-free QueryClient. */
export async function renderWithProviders(ui: ReactElement, language = 'es-ES'): Promise<ProvidersResult> {
  window.localStorage.setItem('polvorapp.language', language);
  const i18n = await createI18n();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const result = render(
    <QueryClientProvider client={queryClient}>
      <I18nextProvider i18n={i18n}>{ui}</I18nextProvider>
    </QueryClientProvider>,
  );
  return { ...result, i18n };
}
