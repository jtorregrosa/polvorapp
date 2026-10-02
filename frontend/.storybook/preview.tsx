import type { Decorator, Preview } from '@storybook/react-vite';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { i18n as I18n } from 'i18next';
import { createContext, useContext, useState, type ComponentType } from 'react';
import { I18nextProvider } from 'react-i18next';
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router';
import type { RouteHandle } from '../src/components/app/Breadcrumbs';
import { createI18n } from '../src/i18n';
import { SUPPORTED_LANGUAGES } from '../src/i18n/config';
import { THEME_STORAGE_KEY } from '../src/theme/config';
import { ThemeProvider } from '../src/theme/ThemeProvider';
import '../src/styles/globals.css';

/** Story parameter: route handles to nest around the story, outermost first (breadcrumbs). */
export interface CatalogueRouteParameters {
  crumbs?: readonly (RouteHandle & { path: string })[];
}

let i18nInstance: Promise<I18n> | undefined;

const CurrentStory = createContext<ComponentType | null>(null);

function StorySlot() {
  const Story = useContext(CurrentStory);
  return (
    <div data-story-canvas="" className="min-h-svh bg-background p-6 text-foreground">
      {Story && <Story />}
    </div>
  );
}

/** A data router (breadcrumbs read route matches), created once per mounted story. */
function createStoryRouter(route: CatalogueRouteParameters | undefined) {
  const crumbs = route?.crumbs ?? [];
  // The story renders on the innermost route; the outer ones only contribute their handle.
  const routes = crumbs.reduceRight<RouteObject[]>(
    (children, { path, breadcrumb }, index) => [
      index === crumbs.length - 1
        ? { path, handle: { breadcrumb }, element: <StorySlot /> }
        : { path, handle: { breadcrumb }, children },
    ],
    [{ path: '*', element: <StorySlot /> }],
  );
  const entry = `/${crumbs.map(({ path }) => path).join('/')}`;
  return createMemoryRouter(routes, { initialEntries: [entry] });
}

/** Provides what composites need: translations, a query client, the theme and a router. */
const withProviders: Decorator = (Story, context) => {
  const { i18n } = context.loaded as { i18n: I18n };
  const theme = context.globals.theme as string;
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: false } } }));
  const [router] = useState(() =>
    createStoryRouter(context.parameters.route as CatalogueRouteParameters | undefined),
  );

  return (
    <QueryClientProvider client={queryClient}>
      <I18nextProvider i18n={i18n}>
        {/* The loader stores the toolbar theme; remounting (key) makes ThemeProvider read it. */}
        <ThemeProvider key={theme}>
          <CurrentStory value={Story}>
            <RouterProvider router={router} />
          </CurrentStory>
        </ThemeProvider>
      </I18nextProvider>
    </QueryClientProvider>
  );
};

const preview: Preview = {
  loaders: [
    // Side effects of the toolbar globals happen here, before rendering.
    async ({ globals }) => {
      i18nInstance ??= createI18n();
      const i18n = await i18nInstance;
      await i18n.changeLanguage(globals.locale as string);
      window.localStorage.setItem(THEME_STORAGE_KEY, globals.theme as string);
      return { i18n };
    },
  ],
  decorators: [withProviders],
  globalTypes: {
    locale: {
      description: 'UI language',
      toolbar: { icon: 'globe', items: [...SUPPORTED_LANGUAGES], dynamicTitle: true },
    },
    theme: {
      description: 'Theme',
      toolbar: { icon: 'mirror', items: ['light', 'dark'], dynamicTitle: true },
    },
  },
  initialGlobals: { locale: 'es-ES', theme: 'light' },
  parameters: {
    layout: 'fullscreen',
    // WCAG 2.2 A/AA (NFR-07), the same tags as src/test/axe.ts and e2e/fixtures.ts.
    a11y: {
      test: 'error',
      options: { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] } },
    },
  },
};

export default preview;
