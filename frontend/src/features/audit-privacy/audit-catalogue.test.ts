import { describe, expect, it } from 'vitest';
import { AuditActionResponseCode, AuditActionResponseEntityType } from '@/api/generated/model';
import { SUPPORTED_LANGUAGES } from '@/i18n/config';
import { resources } from '@/i18n';

/**
 * Design D2: the contract lists the audit action catalogue, so a code a module adds without a label
 * fails here in every locale that misses it.
 */
describe.each(SUPPORTED_LANGUAGES)('audit labels in %s', (language) => {
  const audit = resources[language].audit;

  it('label every catalogue action', () => {
    const missing = Object.values(AuditActionResponseCode).filter((code) => !(code in audit.actions));

    expect(missing).toEqual([]);
  });

  it('label every catalogue entity type', () => {
    const missing = Object.values(AuditActionResponseEntityType).filter(
      (type) => !(type in audit.entityTypes),
    );

    expect(missing).toEqual([]);
  });

  it('have no label for a code the catalogue does not declare', () => {
    const declared: readonly string[] = Object.values(AuditActionResponseCode);

    expect(Object.keys(audit.actions).filter((code) => !declared.includes(code))).toEqual([]);
  });
});
