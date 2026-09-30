import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nextProvider } from 'react-i18next';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { appRoutes } from '@/app/routes';
import { createI18n } from '@/i18n';

// Shown only if translations cannot be initialised, so it cannot come from them.
const STARTUP_FAILURE =
  'PolvorApp no se ha podido iniciar. Recarga la página. · ' +
  'PolvorApp no s’ha pogut iniciar. Recarrega la pàgina. · ' +
  'PolvorApp could not start. Reload the page.';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Root element #root not found');
}

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false } },
});
const router = createBrowserRouter(appRoutes);

createI18n()
  .then((i18n) => {
    createRoot(root).render(
      <StrictMode>
        <QueryClientProvider client={queryClient}>
          <I18nextProvider i18n={i18n}>
            <RouterProvider router={router} />
          </I18nextProvider>
        </QueryClientProvider>
      </StrictMode>,
    );
  })
  .catch((error: unknown) => {
    root.textContent = STARTUP_FAILURE;
    throw error; // Still reported to the console (and to error tracking once it exists).
  });
