import type { RouteObject } from 'react-router';
import { ErrorPage, RootErrorPage } from '@/features/platform/pages/ErrorPage';
import { HomePage } from '@/features/platform/pages/HomePage';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { AppShell } from './shell/AppShell';

/**
 * Route table. Pages render inside the shell; a page that fails to render shows the error page
 * inside the shell, and unknown paths show the not-found page.
 */
export const appRoutes: RouteObject[] = [
  {
    Component: AppShell,
    ErrorBoundary: RootErrorPage,
    children: [
      {
        ErrorBoundary: ErrorPage,
        children: [
          { index: true, Component: HomePage },
          { path: '*', Component: NotFoundPage },
        ],
      },
    ],
  },
];
