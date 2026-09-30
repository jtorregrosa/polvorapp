import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { AlertBanner } from '@/components/app/AlertBanner';
import { Button } from '@/components/app/Button';
import { PageHeader } from '@/components/app/PageHeader';
import { RecoveryCodeList } from '@/components/app/RecoveryCodeList';
import { useDocumentTitle } from '@/lib/useDocumentTitle';
import { problemMessage } from '../problems';
import { useHandedOverRecoveryCodes } from '../recoveryCodeHandoff';
import { safeReturnTo, useCompleteSignIn } from '../session';

/**
 * The new recovery codes, shown once after enrolment (spec: Mandatory two-factor enrolment). They
 * are handed over in memory: reloading the page or coming back to it does not show them again.
 */
export function RecoveryCodesPage() {
  const { t } = useTranslation('identity');
  useDocumentTitle(t('recoveryCodes.title'));
  const location = useLocation();
  const navigate = useNavigate();
  const completeSignIn = useCompleteSignIn();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<unknown>();
  const { codes, forget } = useHandedOverRecoveryCodes();
  const { returnTo: requested } = (location.state ?? {}) as { returnTo?: unknown };
  const returnTo = safeReturnTo(typeof requested === 'string' ? requested : undefined);

  if (codes.length === 0) {
    return <Navigate to="/" replace />;
  }

  const onContinue = async (): Promise<void> => {
    setPending(true);
    setError(undefined);
    try {
      await completeSignIn();
    } catch (failure) {
      setError(failure); // The codes stay on screen; the person can try again.
      setPending(false);
      return;
    }
    forget();
    await navigate(returnTo, { replace: true, state: null });
  };

  return (
    <>
      <PageHeader title={t('recoveryCodes.title')} description={t('recoveryCodes.description')} />
      <RecoveryCodeList codes={codes} />
      {error !== undefined && (
        <AlertBanner severity="error" className="mt-4">
          {problemMessage(t, error)}
        </AlertBanner>
      )}
      <Button type="button" className="mt-4" pending={pending} onClick={() => void onContinue()}>
        {t('recoveryCodes.saved')}
      </Button>
    </>
  );
}
