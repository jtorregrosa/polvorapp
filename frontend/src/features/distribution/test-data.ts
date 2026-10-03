import type {
  DistributionDayResponse,
  DistributionPlanResponse,
  ProxyCandidateResponse,
  ProxyResponse,
} from '@/api/generated/model';
import { NORTE, SUR } from '@/features/federation-catalog/test-data';
import { CLOSED_2030, CURRENT_2031 } from '@/features/festival-editions/test-data';

/** Synthetic distribution data for component tests (invented places, times and people). */
export const ESTE = { id: '00000000-0000-4000-8000-000000000103', name: 'Comparsa Sintética Este' };

/** Out of time order on purpose: the page sorts slots by time. */
const NORTE_POWDER_SLOT = { comparsaId: NORTE.id, comparsaName: NORTE.name, startsAt: '09:00' };

export const POWDER_DAY: DistributionDayResponse = {
  id: '00000000-0000-4000-8000-000000000801',
  editionId: CURRENT_2031.id,
  type: 'POWDER',
  date: '2031-04-18',
  location: 'Paraje Sintético del Reparto',
  version: 3,
  slots: [{ comparsaId: SUR.id, comparsaName: SUR.name, startsAt: '09:30' }, NORTE_POWDER_SLOT],
  withoutSlot: [ESTE],
};

export const WEAPONS_DAY: DistributionDayResponse = {
  id: '00000000-0000-4000-8000-000000000802',
  editionId: CURRENT_2031.id,
  type: 'WEAPONS',
  date: '2031-04-12',
  location: 'Almacén Sintético de la Federación',
  version: 1,
  slots: [{ comparsaId: NORTE.id, comparsaName: NORTE.name, startsAt: '10:00' }],
  withoutSlot: [
    { id: SUR.id, name: SUR.name },
    { id: ESTE.id, name: ESTE.name },
  ],
};

/** The current edition as an Admin sees it: the powder day planned, the weapons day not yet. */
export const ADMIN_PLAN: DistributionPlanResponse = {
  editionId: CURRENT_2031.id,
  editionYear: CURRENT_2031.year,
  editionStatus: 'IN_PROGRESS',
  days: [POWDER_DAY],
  canPlan: true,
  canManageProxies: true,
  notValidated: [{ comparsaId: SUR.id, comparsaName: SUR.name, status: 'SUBMITTED' }],
};

/** The same edition for Norte's FiringChief: only Norte's slots, no comparsas without a slot. */
export const FIRING_CHIEF_PLAN: DistributionPlanResponse = {
  ...ADMIN_PLAN,
  days: [{ ...POWDER_DAY, slots: [NORTE_POWDER_SLOT], withoutSlot: null }],
  canPlan: false,
  notValidated: null,
};

/** The closed edition 2030, read-only for everybody but its proxies' forms. */
export const CLOSED_PLAN: DistributionPlanResponse = {
  ...FIRING_CHIEF_PLAN,
  editionId: CLOSED_2030.id,
  editionYear: CLOSED_2030.year,
  editionStatus: 'CLOSED',
  canManageProxies: false,
};

export const POWDER_PROXY: ProxyResponse = {
  id: '00000000-0000-4000-8000-000000000811',
  editionId: CURRENT_2031.id,
  comparsaId: NORTE.id,
  comparsaName: NORTE.name,
  type: 'POWDER',
  holder: { entryId: '00000000-0000-4000-8000-000000000910', name: 'Abad Sintética, Ana' },
  proxy: { entryId: '00000000-0000-4000-8000-000000000909', name: 'Zamora Sintético, Bruno' },
  problem: null,
};

export const BROKEN_PROXY: ProxyResponse = {
  id: '00000000-0000-4000-8000-000000000812',
  editionId: CURRENT_2031.id,
  comparsaId: SUR.id,
  comparsaName: SUR.name,
  type: 'WEAPONS',
  holder: { entryId: '00000000-0000-4000-8000-000000000914', name: 'Bernabeu Sintético, Dani' },
  proxy: { entryId: '00000000-0000-4000-8000-000000000915', name: 'Climent Sintética, Eva' },
  problem: 'LICENSE_INVALID',
};

/** Norte's order: Ana and Carla can be held for, Bruno holds a license, Dani does not. */
export const CANDIDATES: ProxyCandidateResponse[] = [
  {
    entryId: '00000000-0000-4000-8000-000000000910',
    name: 'Abad Sintética, Ana',
    isActive: true,
    canBeHeldFor: ['POWDER', 'WEAPONS'],
    cannotCollect: [],
  },
  {
    entryId: '00000000-0000-4000-8000-000000000909',
    name: 'Zamora Sintético, Bruno',
    isActive: true,
    canBeHeldFor: [],
    cannotCollect: [],
  },
  {
    entryId: '00000000-0000-4000-8000-000000000911',
    name: 'Climent Sintética, Carla',
    isActive: true,
    canBeHeldFor: ['POWDER'],
    cannotCollect: [{ type: 'POWDER', reason: 'proxyAbsent' }],
  },
  {
    entryId: '00000000-0000-4000-8000-000000000912',
    name: 'Domènech Sintètic, Dani',
    isActive: false,
    canBeHeldFor: [],
    cannotCollect: [
      { type: 'POWDER', reason: 'licenseInvalid' },
      { type: 'WEAPONS', reason: 'licenseInvalid' },
    ],
  },
];
