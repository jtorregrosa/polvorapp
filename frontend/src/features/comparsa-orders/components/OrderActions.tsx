import { useQueryClient } from '@tanstack/react-query';
import { CircleCheck, Send, Undo2 } from 'lucide-react';
import { useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useReturnComparsaOrder,
  useSubmitComparsaOrder,
  useValidateComparsaOrder,
} from '@/api/generated/comparsa-orders/comparsa-orders';
import type { OrderResponse } from '@/api/generated/model';
import { Button } from '@/components/app/Button';
import { CheckboxField } from '@/components/app/CheckboxField';
import { ConfirmDialog } from '@/components/app/ConfirmDialog';
import { ConfirmFailure } from '@/components/app/confirm-failure';
import { StatusBadge } from '@/components/app/StatusBadge';
import { TextArea } from '@/components/app/TextArea';
import { personName } from '../orderText';
import { invalidEntries, needsReload, problemCode, problemMessage } from '../problems';
import { orderChanged, reloadOrder } from '../queries';

/** The longest return reason the API takes (ComparsaOrder.ReturnReasonMaxLength). */
export const MAX_RETURN_REASON = 500;

type Move = 'submit' | 'validate' | 'return';
type Announce = (severity: 'success' | 'error', text: string) => void;

/**
 * One status move's dialog plumbing. Every move changes the order's status, which removes the action
 * that started it, so nothing on the page changes while its dialog is open: the new order is cached
 * and announced once the dialog has closed, and an out-of-date order is reloaded when the person
 * closes the dialog that explains the refusal.
 */
function useMove(move: Move, order: OrderResponse, announce: Announce) {
  const { t } = useTranslation('orders');
  const queryClient = useQueryClient();
  const moved = useRef<OrderResponse | undefined>(undefined);
  const stale = useRef(false);

  return {
    run: async (call: () => Promise<{ data: unknown }>) => {
      try {
        moved.current = (await call()).data as OrderResponse;
      } catch (error) {
        if (needsReload(error) || problemCode(error) === 'orders.entriesInvalid') stale.current = true;
        const names = invalidEntries(error).flatMap(({ entryId }) => {
          const person = order.entries.find((entry) => entry.id === entryId)?.arquebusier;
          return person ? [personName(person)] : [];
        });
        throw new ConfirmFailure(
          names.length > 0
            ? t('actions.entriesInvalid', { names: names.join('; ') })
            : problemMessage(t, error),
        );
      }
    },
    onConfirmed: () => {
      const next = moved.current;
      moved.current = undefined;
      if (!next) return;
      void orderChanged(queryClient, next);
      announce('success', t(`actions.${move}.done`));
    },
    onOpenChange: (open: boolean) => {
      if (open || !stale.current) return;
      stale.current = false;
      void reloadOrder(queryClient, order.id);
    },
  };
}

/** The pending compliance warnings of the order's active entries, in words (BR-04). */
function PendingWarnings({ order }: { order: OrderResponse }) {
  const { t } = useTranslation('orders');
  const warned = order.entries.filter((entry) => entry.warnings.length > 0);
  if (warned.length === 0) return <p>{t('actions.submit.noWarnings')}</p>;
  return (
    <div className="flex flex-col gap-2">
      <p>{t('actions.submit.warnings', { count: warned.length })}</p>
      {/* eslint-disable-next-line jsx-a11y/no-redundant-roles -- Safari drops list semantics under `list-style: none`. */}
      <ul role="list" className="flex flex-col gap-2">
        {warned.map((entry) => (
          <li key={entry.id} className="flex flex-col gap-1">
            <span className="font-semibold">{personName(entry.arquebusier)}</span>
            <span className="flex flex-wrap gap-1">
              {entry.warnings.map((warning) => (
                <StatusBadge key={warning} kind="warning" value={warning} />
              ))}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

interface ActionProps {
  order: OrderResponse;
  isAdmin: boolean;
  announce: Announce;
}

/** "Submit order" (FiringChiefs, with the attestation) or "Submit on behalf of the comparsa" (Admins). */
function SubmitAction({ order, isAdmin, announce }: ActionProps) {
  const { t } = useTranslation('orders');
  const submit = useSubmitComparsaOrder();
  const move = useMove('submit', order, announce);
  const [attested, setAttested] = useState(false);
  return (
    <ConfirmDialog
      tone="primary"
      trigger={
        <Button icon={Send}>{isAdmin ? t('actions.submit.adminLabel') : t('actions.submit.label')}</Button>
      }
      title={
        isAdmin
          ? t('actions.submit.adminTitle')
          : t('actions.submit.title', { comparsa: order.comparsa.name })
      }
      description={isAdmin ? t('actions.submit.adminDescription') : t('actions.submit.description')}
      confirmLabel={isAdmin ? t('actions.submit.adminConfirm') : t('actions.submit.confirm')}
      confirmDisabled={!isAdmin && !attested}
      onOpenChange={(open) => {
        if (open) setAttested(false);
        move.onOpenChange(open);
      }}
      onConfirm={() =>
        move.run(() =>
          submit.mutateAsync({
            id: order.id,
            data: { version: order.version, attestation: isAdmin ? null : true },
          }),
        )
      }
      onConfirmed={move.onConfirmed}
    >
      <PendingWarnings order={order} />
      {!isAdmin && (
        <CheckboxField
          label={t('actions.submit.attestation')}
          description={t('actions.submit.attestationHint')}
          checked={attested}
          onCheckedChange={setAttested}
        />
      )}
    </ConfirmDialog>
  );
}

/** "Validate" (Admins): for an order the comparsa did not submit, the dialog says so. */
function ValidateAction({ order, announce }: ActionProps) {
  const { t } = useTranslation('orders');
  const validate = useValidateComparsaOrder();
  const move = useMove('validate', order, announce);
  return (
    <ConfirmDialog
      tone="primary"
      trigger={
        <Button variant="secondary" icon={CircleCheck}>
          {t('actions.validate.label')}
        </Button>
      }
      title={t('actions.validate.title', { comparsa: order.comparsa.name })}
      description={
        order.status === 'SUBMITTED' ? t('actions.validate.description') : t('actions.validate.notSubmitted')
      }
      confirmLabel={t('actions.validate.confirm')}
      onOpenChange={move.onOpenChange}
      onConfirm={() =>
        move.run(() => validate.mutateAsync({ id: order.id, data: { version: order.version } }))
      }
      onConfirmed={move.onConfirmed}
    />
  );
}

/** "Return" (Admins), with the reason the FiringChief will read. */
function ReturnAction({ order, announce }: ActionProps) {
  const { t } = useTranslation('orders');
  const returnOrder = useReturnComparsaOrder();
  const move = useMove('return', order, announce);
  const [reason, setReason] = useState('');
  const [problem, setProblem] = useState<string>();
  const field = useRef<HTMLTextAreaElement>(null);
  const fieldId = useId();
  const errorId = `${fieldId}-error`;
  return (
    <ConfirmDialog
      tone="primary"
      trigger={
        <Button variant="secondary" icon={Undo2}>
          {t('actions.return.label')}
        </Button>
      }
      title={t('actions.return.title', { comparsa: order.comparsa.name })}
      description={t('actions.return.description')}
      confirmLabel={t('actions.return.confirm')}
      initialFocus={field}
      onOpenChange={(open) => {
        if (open) {
          setReason('');
          setProblem(undefined);
        }
        move.onOpenChange(open);
      }}
      onConfirm={() => {
        const text = reason.trim();
        if (text === '' || text.length > MAX_RETURN_REASON) {
          setProblem(text === '' ? t('validation.required') : t('validation.tooLong'));
          // The field says what is missing: focus goes there, so its error is read (WCAG 3.3.1).
          field.current?.focus();
          return false;
        }
        setProblem(undefined);
        return move.run(() =>
          returnOrder.mutateAsync({ id: order.id, data: { version: order.version, reason: text } }),
        );
      }}
      onConfirmed={move.onConfirmed}
    >
      <div className="flex flex-col gap-field">
        <label htmlFor={fieldId} className="text-label text-foreground">
          {t('actions.return.reason')}
        </label>
        {problem && (
          <p id={errorId} className="text-help font-semibold text-destructive">
            {problem}
          </p>
        )}
        <TextArea
          ref={field}
          id={fieldId}
          value={reason}
          onChange={(event) => {
            setReason(event.target.value);
            setProblem(undefined);
          }}
          maxLength={MAX_RETURN_REASON}
          rows={5}
          required
          aria-required
          aria-invalid={problem !== undefined}
          aria-describedby={problem ? errorId : undefined}
        />
      </div>
    </ConfirmDialog>
  );
}

/**
 * The order's status moves, each confirmed in a dialog (spec: Submitting an order (UC-14),
 * Reviewing orders (UC-15), Orders screens): FiringChiefs submit with the attestation while they
 * may edit; Admins submit on the comparsa's behalf, validate and return. The outcome is announced.
 */
export function OrderActions({ order, isAdmin, announce }: ActionProps) {
  const props = { order, isAdmin, announce };
  const draftOrReturned = order.status === 'DRAFT' || order.status === 'RETURNED';
  return (
    <>
      {draftOrReturned && (isAdmin || order.canEdit) && <SubmitAction {...props} />}
      {isAdmin && (draftOrReturned || order.status === 'SUBMITTED') && <ValidateAction {...props} />}
      {isAdmin && (order.status === 'SUBMITTED' || order.status === 'VALIDATED') && (
        <ReturnAction {...props} />
      )}
    </>
  );
}
