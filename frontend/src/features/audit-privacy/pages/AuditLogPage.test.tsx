import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  AuditEntryResponse,
  AuditPageResponse,
  ComparsaResponse,
  UserResponse,
} from '@/api/generated/model';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';

// Spec audit-privacy "Audit log screens" (UC-25; design D12).
const NOW = new Date('2030-07-14T10:00:00Z');
const ADMIN_ID = '00000000-0000-4000-8000-000000000001';
const ARQUEBUSIER_ID = '00000000-0000-4000-8000-000000000301';
const TRACE = '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01';

const NORTE: ComparsaResponse = {
  id: '00000000-0000-4000-8000-000000000101',
  name: 'Comparsa Sintética Norte',
  side: 'MOORISH',
  active: true,
  logo: null,
};

const ADMIN_USER: UserResponse = {
  id: ADMIN_ID,
  name: 'Administradora Sintética',
  email: 'admin@polvorapp.example',
  role: 'ADMIN',
  locale: 'es-ES',
  status: 'ACTIVE',
  twoFactorEnabled: true,
  lastSignInAt: null,
  createdAt: '2030-01-01T09:00:00Z',
};

function entry(overrides: Partial<AuditEntryResponse>): AuditEntryResponse {
  return {
    id: '00000000-0000-4000-8000-000000000a01',
    occurredAt: '2030-07-12T16:05:00Z',
    action: 'ArquebusierUpdated',
    entityType: 'Arquebusier',
    entityId: ARQUEBUSIER_ID,
    recordExists: true,
    comparsaId: NORTE.id,
    comparsaName: NORTE.name,
    traceId: TRACE,
    actor: { id: ADMIN_ID, name: 'Administradora Sintética', erased: false },
    data: { changed: ['lastName', 'phone'] },
    ...overrides,
  };
}

const UPDATED = entry({});
const DELETED = entry({
  id: '00000000-0000-4000-8000-000000000a02',
  action: 'ArquebusierDeleted',
  recordExists: false,
  data: null,
});
const PURGED = entry({
  id: '00000000-0000-4000-8000-000000000a03',
  action: 'AuditEntriesPurged',
  entityType: 'AuditTrail',
  entityId: null,
  comparsaId: null,
  comparsaName: null,
  traceId: null,
  actor: null,
  data: { deleted: 12 },
});
const FAILED_SIGN_IN = entry({
  id: '00000000-0000-4000-8000-000000000a04',
  action: 'SignInFailed',
  entityType: 'User',
  entityId: null,
  comparsaId: null,
  comparsaName: null,
  actor: null,
  data: null,
});
const BY_ERASED = entry({
  id: '00000000-0000-4000-8000-000000000a05',
  action: 'PasswordChanged',
  entityType: 'User',
  entityId: '00000000-0000-4000-8000-000000000002',
  recordExists: true,
  comparsaId: null,
  comparsaName: null,
  actor: { id: '00000000-0000-4000-8000-000000000002', name: null, erased: true },
  data: null,
});
const RETIRED = entry({ id: '00000000-0000-4000-8000-000000000a06', action: 'RetiredAction', data: null });

/** Answers the page's lookups and the audit entries, recording each audit query string. */
function auditApi(pages: Record<string, AuditPageResponse>) {
  const queries: URLSearchParams[] = [];
  server.use(
    mock.get('/api/audit-entries/actions', () =>
      HttpResponse.json([
        { code: 'ArquebusierUpdated', entityType: 'Arquebusier' },
        { code: 'ComparsaOrderValidated', entityType: 'ComparsaOrder' },
        { code: 'SignInFailed', entityType: 'User' },
      ]),
    ),
    mock.get('/api/users', () => HttpResponse.json([ADMIN_USER])),
    mock.get('/api/comparsas', () => HttpResponse.json([NORTE])),
    mock.get('/api/audit-entries', ({ request }) => {
      const search = new URL(request.url).searchParams;
      queries.push(search);
      return HttpResponse.json(pages[search.get('cursor') ?? ''] ?? { items: [], nextCursor: null });
    }),
  );
  return queries;
}

/** The table row of a cell. */
function rowOf(cell: HTMLElement): HTMLElement {
  const row = cell.closest('tr');
  if (!row) throw new Error('The cell is not in a table row.');
  return row;
}

const asAdmin = (path: string) => renderApp(path, { session: SYNTHETIC_ADMIN });

async function table(): Promise<HTMLElement> {
  return screen.findByRole('table', {
    name: 'Entradas del registro de auditoría, de la más reciente a la más antigua',
  });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  document.documentElement.classList.remove('dark');
});

describe('AuditLogPage (spec: Audit log screens)', () => {
  it('asks for the last 30 days by default and shows each entry with its user, action, record and comparsa', async () => {
    const queries = auditApi({ '': { items: [UPDATED], nextCursor: null } });
    await asAdmin('/audit-log');

    const row = rowOf(await within(await table()).findByRole('rowheader', { name: 'Arcabucero modificado' }));
    expect(queries[0]?.get('from')).toBe('2030-06-15');
    expect(queries[0]?.has('to')).toBe(false);
    expect(within(row).getByText('Administradora Sintética')).toBeInTheDocument();
    expect(within(row).getByText('Comparsa Sintética Norte')).toBeInTheDocument();
    expect(within(row).getAllByRole('cell')[0]).toHaveTextContent(/12 jul 2030/);
    expect(within(row).getByRole('link', { name: /Arcabucero/ })).toHaveAttribute(
      'href',
      `/arquebusiers/${ARQUEBUSIER_ID}`,
    );
  });

  it('keeps the filters in the address and sends them to the API', async () => {
    const user = userEvent.setup();
    const queries = auditApi({ '': { items: [UPDATED], nextCursor: null } });
    const app = await asAdmin('/audit-log');
    await table();

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Acción' }),
      'ComparsaOrderValidated',
    );
    await user.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), NORTE.id);
    await user.selectOptions(screen.getByRole('combobox', { name: 'Usuario' }), ADMIN_ID);

    await waitFor(() => {
      expect(app.location()).toBe(
        `/audit-log?action=ComparsaOrderValidated&comparsaId=${NORTE.id}&actorUserId=${ADMIN_ID}`,
      );
    });
    await waitFor(() => {
      const last = queries.at(-1);
      expect([last?.get('action'), last?.get('comparsaId'), last?.get('actorUserId')]).toEqual([
        'ComparsaOrderValidated',
        NORTE.id,
        ADMIN_ID,
      ]);
    });
  });

  it('reads the filters from the address, including a record from "View history"', async () => {
    const queries = auditApi({ '': { items: [UPDATED], nextCursor: null } });
    await asAdmin(
      `/audit-log?entityType=Arquebusier&entityId=${ARQUEBUSIER_ID}&from=2030-01-01&to=2030-03-31`,
    );
    await table();

    expect([
      queries[0]?.get('entityType'),
      queries[0]?.get('entityId'),
      queries[0]?.get('from'),
      queries[0]?.get('to'),
    ]).toEqual(['Arquebusier', ARQUEBUSIER_ID, '2030-01-01', '2030-03-31']);
    expect(screen.getByText('Filtrado por un registro: Arcabucero')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Área' })).toHaveValue('Arquebusier');
    });
  });

  it('"Show more" appends the next page', async () => {
    const user = userEvent.setup();
    const queries = auditApi({
      '': { items: [UPDATED], nextCursor: 'next-1' },
      'next-1': { items: [DELETED], nextCursor: null },
    });
    await asAdmin('/audit-log');
    const list = await table();
    await within(list).findByRole('rowheader', { name: 'Arcabucero modificado' });

    await user.click(screen.getByRole('button', { name: 'Mostrar más' }));

    expect(await within(list).findByRole('rowheader', { name: 'Arcabucero eliminado' })).toBeInTheDocument();
    expect(within(list).getByRole('rowheader', { name: 'Arcabucero modificado' })).toBeInTheDocument();
    expect(queries.at(-1)?.get('cursor')).toBe('next-1');
    expect(queries.at(-1)?.get('from')).toBe('2030-06-15');
    expect(screen.queryByRole('button', { name: 'Mostrar más' })).not.toBeInTheDocument();
  });

  it('moves focus to the first new entry after "Show more"', async () => {
    const user = userEvent.setup();
    auditApi({
      '': { items: [UPDATED], nextCursor: 'next-1' },
      'next-1': { items: [DELETED], nextCursor: null },
    });
    await asAdmin('/audit-log');
    const list = await table();
    await within(list).findByRole('rowheader', { name: 'Arcabucero modificado' });

    await user.click(screen.getByRole('button', { name: 'Mostrar más' }));

    const added = rowOf(await within(list).findByRole('rowheader', { name: 'Arcabucero eliminado' }));
    await waitFor(() => {
      expect(within(added).getByRole('button', { name: /Ver detalles/ })).toHaveFocus();
    });
  });

  it('keeps the loaded entries when "Show more" fails, so it can be tried again', async () => {
    const user = userEvent.setup();
    auditApi({ '': { items: [UPDATED], nextCursor: 'next-1' } });
    server.use(
      mock.get('/api/audit-entries', ({ request }) =>
        new URL(request.url).searchParams.get('cursor')
          ? problem(503, 'audit.unavailable')
          : HttpResponse.json({ items: [UPDATED], nextCursor: 'next-1' }),
      ),
    );
    await asAdmin('/audit-log');
    const list = await table();
    await within(list).findByRole('rowheader', { name: 'Arcabucero modificado' });

    await user.click(screen.getByRole('button', { name: 'Mostrar más' }));

    expect(
      await screen.findByText('No se ha podido completar la acción. Vuelve a intentarlo.'),
    ).toBeInTheDocument();
    expect(within(list).getByRole('rowheader', { name: 'Arcabucero modificado' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mostrar más' })).toBeInTheDocument();
  });

  it('asks for no lower bound once "From" is cleared, and says when the period is inverted', async () => {
    const user = userEvent.setup();
    const queries = auditApi({ '': { items: [UPDATED], nextCursor: null } });
    const app = await asAdmin('/audit-log');
    await table();

    await user.click(screen.getByRole('button', { name: 'Borrar fecha de Desde' }));

    await waitFor(() => {
      expect(app.location()).toBe('/audit-log?from=');
    });
    await waitFor(() => {
      expect(queries.at(-1)?.has('from')).toBe(false);
    });
    expect(screen.getByLabelText('Desde')).toHaveValue('');

    const count = queries.length;
    await app.router.navigate('/audit-log?from=2030-07-10&to=2030-07-01');
    expect(await screen.findByText('Debe ser igual o posterior a «Desde».')).toBeInTheDocument();
    expect(screen.getByLabelText('Hasta')).toHaveAttribute('aria-invalid', 'true');
    expect(queries).toHaveLength(count);
  });

  it('moves focus to the filters when the record filter is removed', async () => {
    const user = userEvent.setup();
    const app = await asAdmin(`/audit-log?entityType=Arquebusier&entityId=${ARQUEBUSIER_ID}`);
    auditApi({ '': { items: [UPDATED], nextCursor: null } });
    await table();

    await user.click(screen.getByRole('button', { name: 'Quitar el filtro del registro' }));

    await waitFor(() => {
      expect(app.location()).toBe('/audit-log?entityType=Arquebusier');
    });
    expect(screen.getByLabelText('Desde')).toHaveFocus();
  });

  it('links only records that still exist and have a page', async () => {
    auditApi({ '': { items: [DELETED, PURGED], nextCursor: null } });
    await asAdmin('/audit-log');
    const list = await table();

    const deleted = rowOf(await within(list).findByRole('rowheader', { name: 'Arcabucero eliminado' }));
    const purged = rowOf(
      within(list).getByRole('rowheader', { name: 'Registros de auditoría caducados eliminados' }),
    );
    expect(within(deleted).queryByRole('link')).not.toBeInTheDocument();
    expect(within(deleted).getByText('Arcabucero')).toBeInTheDocument();
    expect(within(purged).queryByRole('link')).not.toBeInTheDocument();
  });

  it('names the system, an anonymous request and an erased user, and shows an undeclared code as is', async () => {
    auditApi({ '': { items: [PURGED, FAILED_SIGN_IN, BY_ERASED, RETIRED], nextCursor: null } });
    await asAdmin('/audit-log');
    const list = await table();

    const row = async (name: string) => rowOf(await within(list).findByRole('rowheader', { name }));
    expect(
      within(await row('Registros de auditoría caducados eliminados')).getByText('Sistema'),
    ).toBeInTheDocument();
    expect(within(await row('Inicio de sesión fallido')).getByText('Anónimo')).toBeInTheDocument();
    expect(within(await row('Contraseña cambiada')).getByText('Usuario borrado')).toBeInTheDocument();
    expect(await row('RetiredAction')).toBeInTheDocument();
  });

  it('opens an entry with its data fields and its trace id', async () => {
    const user = userEvent.setup();
    auditApi({ '': { items: [UPDATED], nextCursor: null } });
    await asAdmin('/audit-log');
    const list = await table();
    await within(list).findByRole('rowheader', { name: 'Arcabucero modificado' });

    await user.click(within(list).getByRole('button', { name: /Ver detalles.*Arcabucero modificado/ }));

    const sheet = await screen.findByRole('dialog', { name: 'Detalles de la entrada' });
    expect(within(sheet).getByText(TRACE)).toBeInTheDocument();
    expect(within(sheet).getByText('changed')).toBeInTheDocument();
    expect(within(sheet).getByText('lastName, phone')).toBeInTheDocument();
    expect(within(sheet).getByText('Comparsa Sintética Norte')).toBeInTheDocument();
    expect(await axeViolations(sheet)).toEqual([]);
  });

  it('says when no entry matches and when the log cannot be loaded', async () => {
    auditApi({ '': { items: [], nextCursor: null } });
    const first = await asAdmin('/audit-log');
    expect(await screen.findByText('No hay entradas con estos filtros.')).toBeInTheDocument();
    first.unmount();

    auditApi({});
    server.use(
      mock.get('/api/audit-entries', () => problem(400, 'validation', { errors: { from: ['invalid'] } })),
    );
    await asAdmin('/audit-log?from=2030-02-30');
    expect(await screen.findByText('Revisa los filtros: alguno no es válido.')).toBeInTheDocument();
  });

  it.each(['light', 'dark'])('has no accessibility violations in the %s theme', async (theme) => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
    auditApi({ '': { items: [UPDATED, PURGED], nextCursor: 'next-1' } });
    const { container } = await asAdmin('/audit-log');
    await within(await table()).findByRole('rowheader', { name: 'Arcabucero modificado' });

    expect(await axeViolations(container)).toEqual([]);
  });

  it('is not allowed for a FiringChief, who asks for no audit data', async () => {
    let requested = false;
    server.use(
      mock.get('/api/audit-entries', () => {
        requested = true;
        return HttpResponse.json({ items: [], nextCursor: null });
      }),
    );
    await renderApp('/audit-log', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Navegación principal' })).queryByRole('link', {
        name: 'Auditoría',
      }),
    ).not.toBeInTheDocument();
    expect(requested).toBe(false);
  });

  it('is in the Admin navigation', async () => {
    auditApi({ '': { items: [], nextCursor: null } });
    await asAdmin('/audit-log');

    const navigation = await screen.findByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).getByRole('link', { name: 'Auditoría' })).toHaveAttribute('href', '/audit-log');
  });
});
