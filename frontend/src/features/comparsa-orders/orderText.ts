import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { EntryArquebusierResponse, EntryResponse } from '@/api/generated/model';
import { useFormatters } from '@/lib/format';

/** Last name first, as lists sort them: "Sintético Uno, Arcabucero". */
export function personName(person: Pick<EntryArquebusierResponse, 'firstName' | 'lastName'>): string {
  return [person.lastName, person.firstName].filter(Boolean).join(', ');
}

/** The words for an entry's values, in the user's language (spec: Orders screens). */
export function useEntryText() {
  const { t } = useTranslation('orders');
  const { number } = useFormatters();

  return useMemo(
    () => ({
      powder: (entry: EntryResponse) => t('totals.kg', { value: number(entry.powderKg) }),
      caps: (entry: EntryResponse) =>
        entry.capsBoxes === 0 || !entry.capsType
          ? t('entry.noCaps')
          : t(`entry.caps.${entry.capsType}`, { count: entry.capsBoxes }),
      weapon: (entry: EntryResponse): string => {
        switch (entry.weaponSource) {
          case 'OWNED': {
            const weapon = entry.ownedWeapon;
            const text = t('entry.weapon.OWNED', {
              weapon: [weapon?.modelLabel, weapon?.weaponNumber].filter(Boolean).join(' '),
            });
            if (!weapon?.removed) return text;
            // A removed weapon shows its ownership guide from the entry's copy.
            const guide = weapon.ownershipGuideNumber
              ? `, ${t('entry.weapon.guide', { guide: weapon.ownershipGuideNumber })}`
              : '';
            return `${text}${guide} ${t('entry.weapon.removed')}`;
          }
          case 'RENTAL':
            return t('entry.weapon.RENTAL', { model: entry.rentalWeaponModel?.label ?? '' });
          case 'LOAN': {
            const loan = entry.loan;
            if (!loan) return t('entry.weapon.LOAN_MISSING');
            const lender = personName({ firstName: loan.lenderFirstName, lastName: loan.lenderLastName });
            const text =
              loan.lenderKind === 'EXTERNAL'
                ? t('entry.weapon.LOAN_EXTERNAL', { lender })
                : t('entry.weapon.LOAN', { lender, comparsa: loan.lenderComparsaName ?? '' });
            return loan.weaponRemoved ? `${text} ${t('entry.weapon.removed')}` : text;
          }
          default:
            return t('entry.weapon.NONE');
        }
      },
      flask: (entry: EntryResponse) => t(`entry.flask.${entry.flask}`),
      /**
       * The arquebusier's DNI/NIE and ID Unión (from the entry's copy once they left the registry;
       * none once erased), then the first-year flag or that they are no longer in the registry.
       */
      notes: (entry: EntryResponse): string[] => {
        const { nationalId, federationId, inRegistry } = entry.arquebusier;
        return [
          nationalId ? t('entry.nationalId', { value: nationalId }) : null,
          federationId === null ? null : t('entry.federationId', { value: String(federationId) }),
          !inRegistry ? t('entry.notInRegistry') : entry.firstYear ? t('entry.firstYear') : null,
        ].filter((note) => note !== null);
      },
      issue: (reason: string) =>
        reason === 'ownedWeaponMissing' ||
        reason === 'loanWeaponMissing' ||
        reason === 'rentalModelNotOffered'
          ? t(`issues.${reason}`)
          : t('issues.unknown'),
    }),
    [t, number],
  );
}
