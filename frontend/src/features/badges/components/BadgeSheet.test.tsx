import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ArquebusierRowResponse } from '@/api/generated/model';
import { problem, recordBodies } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { BadgeSheet } from './BadgeSheet';

const SHEET = '/api/badges/sheet';

// Synthetic data only.
function row(
  id: string,
  lastName: string,
  extra: Partial<ArquebusierRowResponse> = {},
): ArquebusierRowResponse {
  return {
    id,
    firstName: 'Ana',
    lastName,
    nationalId: '00000000T',
    federationId: 1,
    comparsaId: 'c1',
    comparsaName: 'Comparsa Sintética Norte',
    status: 'ACTIVE',
    licenseStatus: 'VALID',
    licenseExpiresOn: '2033-05-31',
    hasIdPhoto: true,
    warnings: [],
    ...extra,
  };
}

const ROWS = [
  row('a1', 'Abad Sintético'),
  row('a2', 'Bravo Sintético', { hasIdPhoto: false }),
  row('a3', 'Castro Sintética', { licenseStatus: 'PENDING', licenseExpiresOn: null }),
  row('a4', 'Durán Sintético', { licenseStatus: null, licenseExpiresOn: null, hasIdPhoto: false }),
];

let saved: string | undefined;

beforeEach(() => {
  saved = undefined;
  // A write sends the anti-forgery token from its cookie.
  document.cookie = 'XSRF-TOKEN=token; path=/';
  vi.stubGlobal(
    'URL',
    class extends URL {
      static override createObjectURL = vi.fn(() => 'blob:badges');
      static override revokeObjectURL = vi.fn();
    },
  );
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved = this.download;
  });
});

afterEach(() => {
  document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function pdf(name: string) {
  return new HttpResponse(new Uint8Array([37, 80, 68, 70]), {
    headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': `attachment; filename=${name}` },
  });
}

async function openComparsaSheet(onUnknownIds = vi.fn()) {
  const user = userEvent.setup();
  const view = await renderWithProviders(
    <BadgeSheet
      batch={{ kind: 'comparsa', comparsaId: 'c1', comparsaName: 'Comparsa Sintética Norte' }}
      rows={ROWS}
      onUnknownIds={onUnknownIds}
    />,
  );
  await user.click(screen.getByRole('button', { name: /Imprimir carnets/ }));
  return { user, view, dialog: await screen.findByRole('dialog', { name: 'Imprimir carnets' }) };
}

describe('BadgeSheet (spec: Badge screens)', () => {
  it('describes the batch, the incomplete badges, the missing logo and the 100 % print', async () => {
    const { dialog } = await openComparsaSheet();

    expect(dialog).toHaveTextContent('4 arcabuceros de Comparsa Sintética Norte, activos y en reserva');
    expect(dialog).toHaveTextContent('2 sin foto de carnet');
    expect(dialog).toHaveTextContent('2 sin licencia emitida');
    expect(await within(dialog).findByText(/No se ha subido el logo de la Federación/)).toBeInTheDocument();
    expect(dialog).toHaveTextContent('Imprime a escala 100 %');
  });

  it('says nothing about incomplete badges when none is', async () => {
    server.use(
      mock.get('/api/federation', () =>
        HttpResponse.json({ logo: { version: 'v1', width: 10, height: 10 } }),
      ),
    );
    const user = userEvent.setup();
    await renderWithProviders(
      <BadgeSheet
        batch={{ kind: 'selection', arquebusierIds: ['a1'] }}
        rows={[row('a1', 'Abad')]}
        onUnknownIds={vi.fn()}
      />,
    );
    await user.click(screen.getByRole('button', { name: /Imprimir carnets/ }));
    const dialog = await screen.findByRole('dialog');

    expect(dialog).toHaveTextContent('1 arcabucero seleccionado');
    await waitFor(() => {
      expect(dialog).not.toHaveTextContent(/incompletos|logo/);
    });
  });

  it('defaults to the user language, lets choose another and downloads with it', async () => {
    const recorded = recordBodies(() => pdf('polvorapp-badges-comparsa-sintetica-norte-20310302.pdf'));
    server.use(mock.post(SHEET, recorded.resolver));
    const { user, dialog } = await openComparsaSheet();

    expect(within(dialog).getByRole('radio', { name: 'Español' })).toBeChecked();
    await user.click(within(dialog).getByRole('radio', { name: 'Valenciano' }));
    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));

    await waitFor(() => {
      expect(saved).toBe('polvorapp-badges-comparsa-sintetica-norte-20310302.pdf');
    });
    expect(recorded.bodies).toEqual([{ comparsaId: 'c1', language: 'ca-ES-valencia' }]);
    expect(within(dialog).getByRole('status')).toHaveTextContent(
      'Archivo guardado: polvorapp-badges-comparsa-sintetica-norte-20310302.pdf',
    );
  });

  it('sends a selection by its ids', async () => {
    const recorded = recordBodies(() => pdf('polvorapp-badges-selection-2-20310302.pdf'));
    server.use(mock.post(SHEET, recorded.resolver));
    const user = userEvent.setup();
    await renderWithProviders(
      <BadgeSheet
        batch={{ kind: 'selection', arquebusierIds: ['a2', 'a1'] }}
        rows={ROWS.slice(0, 2)}
        onUnknownIds={vi.fn()}
      />,
    );
    await user.click(screen.getByRole('button', { name: /Imprimir carnets/ }));
    await user.click(await screen.findByRole('button', { name: 'Descargar PDF' }));

    await waitFor(() => {
      expect(recorded.bodies).toEqual([{ arquebusierIds: ['a2', 'a1'], language: 'es-ES' }]);
    });
  });

  it.each([
    [problem(409, 'badges.tooMany'), 'Un PDF admite como máximo 200 carnets. Imprime una selección.'],
    [problem(409, 'badges.nothingToPrint'), 'La comparsa no tiene arcabuceros.'],
    [problem(404, 'badges.notFound'), 'La comparsa ya no existe.'],
    [problem(503, 'badges.busy'), 'Se están generando otros carnets.'],
    [problem(503, 'badges.auditUnavailable'), 'No se ha podido registrar la descarga.'],
    [problem(503, 'storage.unavailable'), 'No se pueden leer las fotos ni el logo'],
    [problem(429, 'rateLimited'), 'Has descargado muchos documentos seguidos.'],
    [new HttpResponse(null, { status: 500 }), 'Inténtalo de nuevo.'],
  ])('explains a refused download', async (response, message) => {
    server.use(mock.post(SHEET, () => response));
    const { user, dialog } = await openComparsaSheet();

    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));

    const alert = await within(dialog).findByRole('alert');
    expect(alert).toHaveTextContent('No se han podido generar los carnets');
    expect(alert).toHaveTextContent(message);
  });

  it('names the arquebusiers whose photo cannot be read', async () => {
    server.use(
      mock.post(SHEET, () => problem(409, 'badges.photoUnreadable', { arquebusierIds: ['a3', 'a1'] })),
    );
    const { user, dialog } = await openComparsaSheet();

    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'No se puede leer la foto de carnet de: Castro Sintética, Ana; Abad Sintético, Ana.',
    );
  });

  it('hands back the arquebusiers no longer in the registry and says so', async () => {
    const onUnknownIds = vi.fn();
    server.use(
      mock.post(SHEET, () => problem(400, 'validation', { errors: { 'arquebusierIds[1]': 'notFound' } })),
    );
    const user = userEvent.setup();
    await renderWithProviders(
      <BadgeSheet
        batch={{ kind: 'selection', arquebusierIds: ['a1', 'a2'] }}
        rows={ROWS.slice(0, 2)}
        onUnknownIds={onUnknownIds}
      />,
    );
    await user.click(screen.getByRole('button', { name: /Imprimir carnets/ }));
    await user.click(await screen.findByRole('button', { name: 'Descargar PDF' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      '1 arcabucero ya no está en el registro: se quitará de la selección al cerrar.',
    );
    expect(screen.getByRole('alert')).toHaveFocus();
    expect(onUnknownIds).not.toHaveBeenCalled();

    await user.keyboard('{Escape}');
    expect(onUnknownIds).toHaveBeenCalledWith(['a2']);
  });

  it('cannot be opened when the trigger is disabled, which stays focusable with its reason', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <>
        <p id="reason">Demasiados</p>
        <BadgeSheet
          batch={{ kind: 'selection', arquebusierIds: [] }}
          rows={[]}
          onUnknownIds={vi.fn()}
          disabled
          disabledReasonId="reason"
        />
      </>,
    );
    const trigger = screen.getByRole('button', { name: /Imprimir carnets/ });

    expect(trigger).toHaveAttribute('aria-disabled', 'true');
    expect(trigger).toHaveAccessibleDescription('Demasiados');
    await user.click(trigger);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('counts the unreadable photos it cannot name', async () => {
    server.use(
      mock.post(SHEET, () => problem(409, 'badges.photoUnreadable', { arquebusierIds: ['a1', 'zz'] })),
    );
    const { user, dialog } = await openComparsaSheet();

    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'No se puede leer la foto de carnet de 2 arcabuceros.',
    );
  });

  it('starts clean when reopened after a failure', async () => {
    server.use(mock.post(SHEET, () => problem(503, 'badges.busy')));
    const { user, dialog } = await openComparsaSheet();
    await user.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));
    await within(dialog).findByRole('alert');

    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: /Imprimir carnets/ }));

    const reopened = await screen.findByRole('dialog');
    expect(within(reopened).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('names the comparsa in the trigger after its visible text', async () => {
    await renderWithProviders(
      <BadgeSheet
        batch={{ kind: 'comparsa', comparsaId: 'c1', comparsaName: 'Comparsa Sintética Norte' }}
        rows={ROWS}
        context="Comparsa Sintética Norte"
        onUnknownIds={vi.fn()}
      />,
    );

    expect(
      screen.getByRole('button', { name: 'Imprimir carnets de Comparsa Sintética Norte' }),
    ).toBeInTheDocument();
  });

  it('has no accessibility violations when open', async () => {
    const { dialog } = await openComparsaSheet();
    await within(dialog).findByText(/logo/);

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
