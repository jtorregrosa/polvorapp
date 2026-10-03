import { http as mock, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { ComplianceWarning, type ComplianceSummaryResponse } from '@/api/generated/model';

/**
 * The warning summary the shell asks for on every signed-in page (navigation count): nothing
 * pending, unless a test registers its own. Initial handlers have the lowest priority.
 */
const NOTHING_PENDING: ComplianceSummaryResponse = {
  active: 0,
  reserve: 0,
  withWarnings: 0,
  warnings: Object.values(ComplianceWarning).map((code) => ({ code, count: 0 })),
};

/** Network mock for component tests; each test registers the handlers it needs with `server.use`. */
export const server = setupServer(
  mock.get('/api/compliance/summary', () => HttpResponse.json(NOTHING_PENDING)),
  // The start page's current-edition card and the registry pages' lock notice.
  mock.get('/api/editions/current', () => HttpResponse.json({ edition: null })),
  mock.get('/api/registry/lock', () => HttpResponse.json({ locked: false, changedAt: null })),
  // The comparsas page's Federation logo section, for Admins: no logo yet.
  mock.get('/api/federation', () => HttpResponse.json({ logo: null })),
);
