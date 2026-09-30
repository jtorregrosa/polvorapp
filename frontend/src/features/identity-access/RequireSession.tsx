import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import { setUnauthorizedHandler } from '@/api/http';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { SESSION_QUERY_KEY, signInPath, useSession } from './session';

/**
 * Route element for every signed-in page: waits for the session, sends signed-out visitors to the
 * sign-in page with a way back, and turns a 401 during use into "session expired".
 */
export function RequireSession() {
  const session = useSession();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { t } = useTranslation();

  useEffect(
    () =>
      setUnauthorizedHandler(() => {
        if (!queryClient.getQueryData(SESSION_QUERY_KEY)) {
          return;
        }
        queryClient.clear();
        queryClient.setQueryData(SESSION_QUERY_KEY, null);
        void navigate(signInPath(window.location.pathname, window.location.search, 'expired'), {
          replace: true,
        });
      }),
    [queryClient, navigate],
  );

  if (session.status === 'loading') {
    return (
      <p role="status" className="p-6 text-sm text-muted-foreground">
        {t('session.loading')}
      </p>
    );
  }
  if (session.status === 'signedOut') {
    return <Navigate to={signInPath(location.pathname, location.search)} replace />;
  }
  return <Outlet />;
}

/** Route element for Admin-only pages: FiringChiefs see "not allowed" and nothing is requested. */
export function RequireAdmin() {
  const session = useSession();
  return session.status === 'signedIn' && session.account.role === 'ADMIN' ? <Outlet /> : <ForbiddenPage />;
}
