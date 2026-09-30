import type { RouteObject } from 'react-router';
import type { RouteHandle } from '@/components/app/Breadcrumbs';
import { AcceptInvitationPage } from '@/features/identity-access/pages/AcceptInvitationPage';
import { AccountPage } from '@/features/identity-access/pages/AccountPage';
import { EnrolmentPage } from '@/features/identity-access/pages/EnrolmentPage';
import { ForgotPasswordPage, ResetPasswordPage } from '@/features/identity-access/pages/PasswordPages';
import { RecoveryCodePage } from '@/features/identity-access/pages/RecoveryCodePage';
import { RecoveryCodesPage } from '@/features/identity-access/pages/RecoveryCodesPage';
import { SecondFactorPage } from '@/features/identity-access/pages/SecondFactorPage';
import { SignInPage } from '@/features/identity-access/pages/SignInPage';
import { InviteUserPage } from '@/features/identity-access/pages/users/InviteUserPage';
import { UserDetailPage } from '@/features/identity-access/pages/users/UserDetailPage';
import { UsersPage } from '@/features/identity-access/pages/users/UsersPage';
import { RequireAdmin, RequireSession } from '@/features/identity-access/RequireSession';
import { ErrorPage, RootErrorPage } from '@/features/platform/pages/ErrorPage';
import { HomePage } from '@/features/platform/pages/HomePage';
import { NotFoundPage } from '@/features/platform/pages/NotFoundPage';
import { AppShell } from './shell/AppShell';
import { PublicShell } from './shell/PublicShell';

/**
 * Route table. Signed-out pages render in the public layout; every other page requires a session
 * and renders inside the shell. A page that fails to render shows the error page in its layout,
 * and unknown paths show the not-found page.
 */
export const appRoutes: RouteObject[] = [
  {
    Component: PublicShell,
    ErrorBoundary: RootErrorPage,
    children: [
      {
        ErrorBoundary: ErrorPage,
        children: [
          { path: 'login', Component: SignInPage },
          { path: 'login/second-factor', Component: SecondFactorPage },
          { path: 'login/recovery-code', Component: RecoveryCodePage },
          { path: 'enrolment', Component: EnrolmentPage },
          { path: 'recovery-codes', Component: RecoveryCodesPage },
          { path: 'invitations/accept', Component: AcceptInvitationPage },
          { path: 'password/forgot', Component: ForgotPasswordPage },
          { path: 'password/reset', Component: ResetPasswordPage },
        ],
      },
    ],
  },
  {
    Component: RequireSession,
    ErrorBoundary: RootErrorPage,
    children: [
      {
        Component: AppShell,
        children: [
          {
            ErrorBoundary: ErrorPage,
            children: [
              { index: true, Component: HomePage },
              {
                path: 'account',
                Component: AccountPage,
                handle: { breadcrumb: 'nav.account' } satisfies RouteHandle,
              },
              {
                Component: RequireAdmin,
                children: [
                  {
                    path: 'users',
                    Component: UsersPage,
                    handle: { breadcrumb: 'nav.users' } satisfies RouteHandle,
                  },
                  {
                    path: 'users/new',
                    Component: InviteUserPage,
                    handle: { breadcrumb: 'nav.users' } satisfies RouteHandle,
                  },
                  {
                    path: 'users/:id',
                    Component: UserDetailPage,
                    handle: { breadcrumb: 'nav.users' } satisfies RouteHandle,
                  },
                ],
              },
              {
                path: '*',
                Component: NotFoundPage,
                handle: { breadcrumb: 'notFound.title' } satisfies RouteHandle,
              },
            ],
          },
        ],
      },
    ],
  },
];
