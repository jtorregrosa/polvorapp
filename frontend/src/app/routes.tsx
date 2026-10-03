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
import { ArquebusierDetailPage } from '@/features/arquebusier-registry/pages/ArquebusierDetailPage';
import { ArquebusierFormPage } from '@/features/arquebusier-registry/pages/ArquebusierFormPage';
import { ArquebusierImportPage } from '@/features/arquebusier-registry/pages/ArquebusierImportPage';
import { ArquebusiersPage } from '@/features/arquebusier-registry/pages/ArquebusiersPage';
import { OwnedWeaponFormPage } from '@/features/arquebusier-registry/pages/OwnedWeaponFormPage';
import { ComparsaDetailPage } from '@/features/federation-catalog/pages/ComparsaDetailPage';
import { ComparsaFormPage } from '@/features/federation-catalog/pages/ComparsaFormPage';
import { ComparsasPage } from '@/features/federation-catalog/pages/ComparsasPage';
import { WeaponModelDetailPage } from '@/features/federation-catalog/pages/WeaponModelDetailPage';
import { WeaponModelFormPage } from '@/features/federation-catalog/pages/WeaponModelFormPage';
import { WeaponModelsPage } from '@/features/federation-catalog/pages/WeaponModelsPage';
import { ErrorPage, RootErrorPage } from '@/features/platform/pages/ErrorPage';
import { DashboardPage } from '@/features/compliance-insights/pages/DashboardPage';
import { EditionCreatePage } from '@/features/festival-editions/pages/EditionCreatePage';
import { EditionDetailPage } from '@/features/festival-editions/pages/EditionDetailPage';
import { EditionsPage } from '@/features/festival-editions/pages/EditionsPage';
import { OrderPage } from '@/features/comparsa-orders/pages/OrderPage';
import { OrdersOverviewPage } from '@/features/comparsa-orders/pages/OrdersOverviewPage';
import { StatisticsPage } from '@/features/compliance-insights/pages/StatisticsPage';
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
              { index: true, Component: DashboardPage },
              {
                path: 'account',
                Component: AccountPage,
                handle: { breadcrumb: 'nav.account' } satisfies RouteHandle,
              },
              {
                path: 'arquebusiers',
                Component: ArquebusiersPage,
                handle: { breadcrumb: 'nav.arquebusiers' } satisfies RouteHandle,
              },
              {
                path: 'arquebusiers/new',
                Component: ArquebusierFormPage,
                handle: { breadcrumb: 'nav.arquebusiers' } satisfies RouteHandle,
              },
              {
                path: 'arquebusiers/:id',
                Component: ArquebusierDetailPage,
                handle: { breadcrumb: 'nav.arquebusiers' } satisfies RouteHandle,
              },
              {
                path: 'arquebusiers/:id/weapons/new',
                Component: OwnedWeaponFormPage,
                handle: { breadcrumb: 'nav.arquebusiers' } satisfies RouteHandle,
              },
              {
                path: 'arquebusiers/:id/weapons/:weaponId',
                Component: OwnedWeaponFormPage,
                handle: { breadcrumb: 'nav.arquebusiers' } satisfies RouteHandle,
              },
              {
                path: 'editions',
                Component: EditionsPage,
                handle: { breadcrumb: 'nav.editions' } satisfies RouteHandle,
              },
              {
                path: 'editions/:id',
                Component: EditionDetailPage,
                handle: { breadcrumb: 'nav.editions' } satisfies RouteHandle,
              },
              {
                path: 'editions/:editionId/orders',
                Component: OrdersOverviewPage,
                handle: { breadcrumb: 'nav.orders' } satisfies RouteHandle,
              },
              {
                path: 'orders',
                Component: OrdersOverviewPage,
                handle: { breadcrumb: 'nav.orders' } satisfies RouteHandle,
              },
              {
                path: 'orders/:orderId',
                Component: OrderPage,
                handle: { breadcrumb: 'nav.orders' } satisfies RouteHandle,
              },
              {
                path: 'statistics',
                Component: StatisticsPage,
                handle: { breadcrumb: 'nav.statistics' } satisfies RouteHandle,
              },
              {
                path: 'comparsas',
                Component: ComparsasPage,
                handle: { breadcrumb: 'nav.comparsas' } satisfies RouteHandle,
              },
              {
                path: 'comparsas/:id',
                Component: ComparsaDetailPage,
                handle: { breadcrumb: 'nav.comparsas' } satisfies RouteHandle,
              },
              {
                Component: RequireAdmin,
                children: [
                  {
                    path: 'arquebusiers/import',
                    Component: ArquebusierImportPage,
                    handle: { breadcrumb: 'nav.arquebusiers' } satisfies RouteHandle,
                  },
                  {
                    path: 'editions/new',
                    Component: EditionCreatePage,
                    handle: { breadcrumb: 'nav.editions' } satisfies RouteHandle,
                  },
                  {
                    path: 'comparsas/new',
                    Component: ComparsaFormPage,
                    handle: { breadcrumb: 'nav.comparsas' } satisfies RouteHandle,
                  },
                  {
                    path: 'weapon-models',
                    Component: WeaponModelsPage,
                    handle: { breadcrumb: 'nav.weaponModels' } satisfies RouteHandle,
                  },
                  {
                    path: 'weapon-models/new',
                    Component: WeaponModelFormPage,
                    handle: { breadcrumb: 'nav.weaponModels' } satisfies RouteHandle,
                  },
                  {
                    path: 'weapon-models/:id',
                    Component: WeaponModelDetailPage,
                    handle: { breadcrumb: 'nav.weaponModels' } satisfies RouteHandle,
                  },
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
