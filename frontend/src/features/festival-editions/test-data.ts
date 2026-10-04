import type { EditionResponse, EditionRowResponse, WeaponModelResponse } from '@/api/generated/model';

/** Synthetic editions and models for component tests (invented dates, prices and titles). */
export const DRAFT_2032: EditionResponse = {
  id: '00000000-0000-4000-8000-000000002032',
  year: 2032,
  status: 'DRAFT',
  ordersOpen: false,
  version: 7,
  festivalStartsOn: '2032-04-22',
  festivalEndsOn: '2032-04-25',
  ordersOpenOn: null,
  ordersCloseOn: null,
  prices: { powderPerKg: 48, capsBox: 3.75, weaponRental: null, flaskRental: null },
  weaponModels: [],
  milestones: [],
  nextWindow: null,
};

export const CURRENT_2031: EditionResponse = {
  id: '00000000-0000-4000-8000-000000002031',
  year: 2031,
  status: 'IN_PROGRESS',
  ordersOpen: true,
  version: 11,
  festivalStartsOn: '2031-04-22',
  festivalEndsOn: '2031-04-25',
  ordersOpenOn: '2031-01-10',
  ordersCloseOn: '2031-02-10',
  prices: { powderPerKg: 48, capsBox: 3.75, weaponRental: 25, flaskRental: 5 },
  weaponModels: [
    {
      id: '00000000-0000-4000-8000-000000000601',
      label: 'ARCABUZ MORO DIESTRO',
      kind: 'ARCABUZ',
      offered: true,
    },
    {
      id: '00000000-0000-4000-8000-000000000602',
      label: 'ARCABUZ MORO ZURDO (PEQUEÑO)',
      kind: 'ARCABUZ',
      offered: false,
    },
  ],
  milestones: [
    {
      id: '00000000-0000-4000-8000-000000000701',
      date: '2030-11-30',
      title: 'Plazo sintético de nuevos arcabuceros',
      notify: true,
    },
    {
      id: '00000000-0000-4000-8000-000000000702',
      date: '2099-03-01',
      title: 'Reparto sintético de pólvora',
      notify: false,
    },
  ],
  nextWindow: { kind: 'CLOSES', date: '2031-02-10' },
};

export const CLOSED_2030: EditionResponse = {
  ...CURRENT_2031,
  id: '00000000-0000-4000-8000-000000002030',
  year: 2030,
  status: 'CLOSED',
  ordersOpen: false,
  festivalStartsOn: '2030-04-22',
  festivalEndsOn: '2030-04-25',
  ordersOpenOn: '2030-01-10',
  ordersCloseOn: '2030-02-10',
  milestones: [],
  nextWindow: null,
};

export function rowOf(edition: EditionResponse): EditionRowResponse {
  return {
    id: edition.id,
    year: edition.year,
    festivalStartsOn: edition.festivalStartsOn,
    festivalEndsOn: edition.festivalEndsOn,
    status: edition.status,
    ordersOpen: edition.ordersOpen,
    isCurrent: edition.status === 'IN_PROGRESS',
  };
}

/** Catalogue models for the offered-models panel. */
export const CATALOGUE: WeaponModelResponse[] = [
  {
    id: '00000000-0000-4000-8000-000000000601',
    kind: 'ARCABUZ',
    side: 'MOORISH',
    handedness: 'RIGHT',
    size: 'NORMAL',
    rentable: true,
    label: 'ARCABUZ MORO DIESTRO',
    active: true,
  },
  {
    id: '00000000-0000-4000-8000-000000000603',
    kind: 'TRABUCO',
    side: 'CHRISTIAN',
    handedness: 'RIGHT',
    size: 'NORMAL',
    rentable: true,
    label: 'TRABUCO CRISTIANO DIESTRO',
    active: true,
  },
  {
    id: '00000000-0000-4000-8000-000000000604',
    kind: 'PISTOL',
    side: null,
    handedness: null,
    size: null,
    rentable: false,
    label: 'PISTOLA',
    active: true,
  },
];
