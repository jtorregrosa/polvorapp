import { screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { FederationSettingsResponse } from '@/api/generated/model';
import { problem, recordBodies, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { isWebsite } from './settingsSchema';
import { SETTINGS } from '../test-data';

const NAMES = {
  officialNameEs: SETTINGS.identity.officialNameEs,
  officialNameCa: SETTINGS.identity.officialNameCa,
  shortName: SETTINGS.identity.shortName,
};

/** The settings and the identity, updated by later calls to `set` (as a successful save would). */
function settings(initial: FederationSettingsResponse = SETTINGS) {
  let current = initial;
  server.use(
    mock.get('/api/federation-settings', () => HttpResponse.json(current)),
    mock.get('/api/federation', () => HttpResponse.json({ ...NAMES, logo: null })),
  );
  return {
    set: (next: FederationSettingsResponse) => {
      current = next;
    },
  };
}

function asAdmin(language?: string) {
  return renderApp('/settings', { session: SYNTHETIC_ADMIN, language });
}

async function section(name: string): Promise<HTMLElement> {
  return screen.findByRole('region', { name });
}

async function openPanel(user: UserEvent, name: string): Promise<HTMLElement> {
  await user.click(await screen.findByRole('button', { name: `Editar ${name}` }));
  return screen.findByRole('dialog');
}

async function expectSaved(): Promise<void> {
  await waitFor(() => {
    expect(screen.getByText('Cambios guardados', { selector: '[role=status]' })).toBeInTheDocument();
  });
}

describe('SettingsPage (specs: Settings screen, Federation settings)', () => {
  it('shows the five sections with their values and where they are used', async () => {
    settings({
      ...SETTINGS,
      identity: { ...SETTINGS.identity, contactEmail: 'info@federacion.example' },
      emails: { ...SETTINGS.emails, replyTo: 'secretaria@federacion.example' },
      orders: { closeReminderLeadDays: 10 },
      calendar: { milestoneLeadDays: 1 },
    });
    await asAdmin();

    expect(await screen.findByRole('heading', { level: 1, name: 'Ajustes' })).toBeInTheDocument();
    const identity = await section('Identidad');
    expect(identity).toHaveTextContent('Unión Sintética de Comparsas');
    expect(identity).toHaveTextContent('info@federacion.example');
    expect(identity).toHaveTextContent(/se imprime en las acreditaciones/);
    expect(within(identity).getByText('Unió Sintètica de Comparses')).toHaveAttribute('lang', 'ca');
    expect(await section('Logo de la Federación')).toBeInTheDocument();
    const emails = await section('Correos');
    expect(emails).toHaveTextContent('PolvorApp <no-reply@polvorapp.example>');
    expect(emails).toHaveTextContent('secretaria@federacion.example');
    expect(emails).toHaveTextContent(/invitaciones y cambios de contraseña/);
    expect(await section('Pedidos')).toHaveTextContent('10 días antes');
    expect(await section('Calendario')).toHaveTextContent('1 día antes');
  });

  it('edits the short name in its panel, saves it with the version and announces it', async () => {
    const user = userEvent.setup();
    const api = settings();
    const { bodies, resolver } = recordBodies(() => {
      api.set({ ...SETTINGS, version: 1235, identity: { ...SETTINGS.identity, shortName: 'Unión Nueva' } });
      return HttpResponse.json(SETTINGS);
    });
    server.use(mock.put('/api/federation-settings/identity', resolver));
    await asAdmin();

    const panel = await openPanel(user, 'la identidad');
    expect(within(panel).getByText(/nunca los de una persona/)).toBeInTheDocument();
    const shortName = within(panel).getByRole('textbox', { name: 'Nombre corto' });
    await user.clear(shortName);
    await user.type(shortName, 'Unión Nueva');
    await user.type(within(panel).getByRole('textbox', { name: /Sitio web/ }), 'https://federacion.example');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved();
    expect(bodies).toEqual([
      {
        officialNameEs: 'Unión Sintética de Comparsas',
        officialNameCa: 'Unió Sintètica de Comparses',
        shortName: 'Unión Nueva',
        contactEmail: null,
        website: 'https://federacion.example',
        version: 1234,
      },
    ]);
    expect(await section('Identidad')).toHaveTextContent('Unión Nueva');
  });

  it('keeps the panel open with an error summary when the Spanish name is cleared', async () => {
    const user = userEvent.setup();
    settings();
    let called = false;
    server.use(
      mock.put('/api/federation-settings/identity', () => {
        called = true;
        return HttpResponse.json(SETTINGS);
      }),
    );
    await asAdmin();

    const panel = await openPanel(user, 'la identidad');
    await user.clear(within(panel).getByRole('textbox', { name: 'Nombre oficial en castellano' }));
    await user.type(within(panel).getByRole('textbox', { name: /Correo de contacto/ }), 'sin-arroba');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByRole('group', { name: 'Hay un problema' })).toBeInTheDocument();
    expect(
      within(panel).getByText('Este campo es obligatorio.', { selector: '[data-slot="form-message"]' }),
    ).toBeInTheDocument();
    expect(
      within(panel).getByText(/Escribe una dirección de correo completa/, {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('refuses a sender name with a line break or an address before calling the API', async () => {
    const user = userEvent.setup();
    settings();
    let called = false;
    server.use(
      mock.put('/api/federation-settings/emails', () => {
        called = true;
        return HttpResponse.json(SETTINGS);
      }),
    );
    await asAdmin();

    const panel = await openPanel(user, 'los correos');
    expect(within(panel).getByText(/no-reply@polvorapp.example/)).toBeInTheDocument();
    const name = within(panel).getByRole('textbox', { name: 'Nombre del remitente' });
    await user.clear(name);
    await user.type(name, 'soporte@banco.example');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByText('El nombre no puede contener <, >, " ni @.', {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it('shows a field the API refuses next to it', async () => {
    const user = userEvent.setup();
    settings();
    server.use(
      mock.put('/api/federation-settings/emails', () =>
        problem(400, 'validation', { errors: { replyTo: 'invalid' } }),
      ),
    );
    await asAdmin();

    const panel = await openPanel(user, 'los correos');
    await user.type(
      within(panel).getByRole('textbox', { name: /Responder a/ }),
      'secretaria@federacion.example',
    );
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(
      await within(panel).findByText(/Escribe una dirección de correo completa/, {
        selector: '[data-slot="form-message"]',
      }),
    ).toBeInTheDocument();
  });

  it('reloads the settings and keeps the panel open when another Admin saved first', async () => {
    const user = userEvent.setup();
    const api = settings();
    server.use(
      mock.put('/api/federation-settings/calendar', () => {
        api.set({ ...SETTINGS, version: 1300, calendar: { milestoneLeadDays: 3 } });
        return problem(409, 'federationSettings.modified');
      }),
    );
    await asAdmin();

    const panel = await openPanel(user, 'el calendario');
    await user.selectOptions(within(panel).getByRole('combobox', { name: 'Recordatorio de hitos' }), '5');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(panel).findByRole('group', { name: 'Hay un problema' })).toHaveTextContent(
      'Otra persona ha cambiado los ajustes mientras editabas.',
    );
    await user.click(within(panel).getByRole('button', { name: 'Cancelar' }));
    expect(await section('Calendario')).toHaveTextContent('3 días antes');
  });

  it('saves a lead time chosen from the list as a number', async () => {
    const user = userEvent.setup();
    settings();
    const { bodies, resolver } = recordBodies(() => HttpResponse.json(SETTINGS));
    server.use(mock.put('/api/federation-settings/orders', resolver));
    await asAdmin();

    const panel = await openPanel(user, 'los pedidos');
    const select = within(panel).getByRole('combobox', { name: 'Primer recordatorio de cierre' });
    expect(
      within(select)
        .getAllByRole('option')
        .map((option) => option.textContent),
    ).toEqual(Array.from({ length: 13 }, (_, index) => `${index + 2} días`));
    await user.selectOptions(select, '10');
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }));

    await expectSaved();
    expect(bodies).toEqual([{ closeReminderLeadDays: 10, version: 1234 }]);
  });

  it('is not offered to a FiringChief, who gets the not-allowed page without a request', async () => {
    let requested = false;
    server.use(
      mock.get('/api/federation-settings', () => {
        requested = true;
        return HttpResponse.json(SETTINGS);
      }),
    );
    await renderApp('/settings', { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { level: 1, name: 'Acceso no permitido' })).toBeInTheDocument();
    const navigation = screen.getByRole('navigation', { name: 'Navegación principal' });
    expect(within(navigation).queryByRole('link', { name: 'Ajustes' })).not.toBeInTheDocument();
    expect(requested).toBe(false);
  });

  it('says so when the settings cannot be loaded', async () => {
    server.use(
      mock.get('/api/federation-settings', () => problem(500, 'unexpected')),
      mock.get('/api/federation', () => HttpResponse.json({ ...NAMES, logo: null })),
    );
    await asAdmin();

    expect(
      await screen.findByText(/No se han podido cargar los ajustes\./, {}, { timeout: 5000 }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Identidad' })).not.toBeInTheDocument();
  });

  it('speaks Valencian', async () => {
    settings();
    await asAdmin('ca-ES-valencia');

    expect(await screen.findByRole('heading', { level: 1, name: 'Ajustos' })).toBeInTheDocument();
    expect(await section('Identitat')).toBeInTheDocument();
  });

  it('works on a phone with every section', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    try {
      settings();
      await asAdmin();

      for (const name of ['Identidad', 'Logo de la Federación', 'Correos', 'Pedidos', 'Calendario']) {
        expect(await section(name)).toBeInTheDocument();
      }
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1024 });
    }
  });

  it('has no automatically detectable accessibility violations', async () => {
    settings();
    const { container } = await asAdmin();
    await section('Calendario');
    await waitFor(() => {
      expect(screen.queryByText('Cargando el logo…')).not.toBeInTheDocument();
    });

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('isWebsite (design D2)', () => {
  it.each([
    ['https://federacion.example', true],
    ['https://www.federacion.example/fiestas?a=1', true],
    ['http://federacion.example', false],
    ['https:\\\\federacion.example', false],
    ['https://federacion.example/<b>', false],
    ['https://federacion.example/"x', false],
    ['https://localhost', false],
    ['https://192.0.2.10/', false],
    ['https://usuario@federacion.example/', false],
    ['https://аррӏе.example/', false],
    ['federacion.example', false],
  ])('%s → %s', (value, expected) => {
    expect(isWebsite(value)).toBe(expected);
  });
});
