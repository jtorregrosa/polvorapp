import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http as mock, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ArquebusierImportReport, ArquebusierImportRow, ComparsaResponse } from '@/api/generated/model';
import { getListArquebusiersQueryKey } from '@/api/generated/arquebusiers/arquebusiers';
import { insightsQueryKeys } from '@/features/compliance-insights/queries';
import { problem, renderApp } from '@/test/app';
import { axeViolations } from '@/test/axe';
import { SYNTHETIC_ADMIN, SYNTHETIC_FIRING_CHIEF } from '@/test/identity';
import { server } from '@/test/server';
import { NORTE, OESTE, SUR } from '../test-data';

// Spec "Import screen" (UC-09). Synthetic data only.

const PATH = '/arquebusiers/import';

const CLEAN: ArquebusierImportReport = {
  rowCount: 12,
  validCount: 12,
  errorRowCount: 0,
  warningRowCount: 0,
  ignoredColumns: [],
  rows: [],
};

const WARNING_ROW: ArquebusierImportRow = {
  rowNumber: 3,
  lastName: 'Sintético',
  firstName: 'Arcabucero',
  errors: [],
  warnings: ['LICENSE_EXPIRED', 'COURSE_MISSING'],
};

const WITH_PROBLEMS: ArquebusierImportReport = {
  rowCount: 3,
  validCount: 1,
  errorRowCount: 2,
  warningRowCount: 1,
  ignoredColumns: ['Observaciones'],
  rows: [
    {
      rowNumber: 2,
      lastName: 'Sintética',
      firstName: 'Arcabucera',
      errors: [{ field: 'nationalId', reason: 'checkLetter' }],
      warnings: [],
    },
    WARNING_ROW,
    {
      rowNumber: 4,
      lastName: null,
      firstName: null,
      errors: [
        { field: 'license.issuedOn', reason: 'invalid' },
        { field: 'federationId', reason: 'duplicateInFile' },
      ],
      warnings: [],
    },
  ],
};

const WARNINGS_ONLY: ArquebusierImportReport = {
  ...CLEAN,
  rowCount: 2,
  validCount: 2,
  warningRowCount: 1,
  rows: [WARNING_ROW],
};

function comparsas(list: ComparsaResponse[] = [NORTE, SUR, OESTE]) {
  server.use(mock.get('/api/comparsas', () => HttpResponse.json(list)));
}

type Answer = (form: FormData) => Response;

interface Received {
  comparsaId: FormDataEntryValue | null;
  file: FormDataEntryValue | null;
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });

const problemAnswer = (status: number, code: string, extra: Record<string, unknown> = {}) =>
  json({ status, title: 'Problem', code, ...extra }, status);

/**
 * Answers the check and the import at fetch, recording the forms they received: jsdom's FormData
 * cannot cross MSW (as in the photo tests). Every other call reaches MSW.
 */
function answerImports({ check: checkAnswer, run }: { check?: Answer; run?: Answer }) {
  const checks: Received[] = [];
  const imports: Received[] = [];
  const realFetch = globalThis.fetch;
  vi.spyOn(globalThis, 'fetch').mockImplementation((input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const form = init?.body instanceof FormData ? init.body : undefined;
    const received = () => ({ comparsaId: form?.get('comparsaId') ?? null, file: form?.get('file') ?? null });
    if (form && checkAnswer && url.endsWith('/api/arquebusiers/import/preview')) {
      checks.push(received());
      return Promise.resolve(checkAnswer(form));
    }
    if (form && run && url.endsWith('/api/arquebusiers/import')) {
      imports.push(received());
      return Promise.resolve(run(form));
    }
    return realFetch(input, init);
  });
  return { checks, imports };
}

/** Answers the check with `report`. */
const checkAnswers = (report: ArquebusierImportReport) => answerImports({ check: () => json(report) }).checks;

const workbook = (name = 'arcabuceros-norte.xlsx', size = 2_048) =>
  new File([new Uint8Array(size)], name, {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });

function fileInput(): HTMLInputElement {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) throw new Error('The file input is missing.');
  return input;
}

/** Opens the page as an Admin, chooses Norte and `file`. */
async function choose(file: File = workbook()) {
  comparsas();
  const app = await renderApp(PATH, { session: SYNTHETIC_ADMIN });
  const comparsa = await screen.findByRole('combobox', { name: 'Comparsa' });
  await within(comparsa).findByRole('option', { name: NORTE.name });
  await userEvent.selectOptions(comparsa, NORTE.id);
  await userEvent.upload(fileInput(), file);
  return app;
}

const check = () => userEvent.click(screen.getByRole('button', { name: 'Comprobar el fichero' }));

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('Import action and access (spec: Import screen)', () => {
  it('offers "Importar" on the Arquebusiers page to Admins only', async () => {
    comparsas([NORTE]);
    server.use(mock.get('/api/arquebusiers', () => HttpResponse.json([])));
    await renderApp('/arquebusiers', { session: SYNTHETIC_ADMIN });

    expect(await screen.findByRole('link', { name: 'Importar' })).toHaveAttribute('href', PATH);
  });

  it('shows no "Importar" to a FiringChief', async () => {
    comparsas([NORTE]);
    server.use(mock.get('/api/arquebusiers', () => HttpResponse.json([])));
    await renderApp('/arquebusiers', { session: SYNTHETIC_FIRING_CHIEF });

    await screen.findByRole('heading', { name: 'Arcabuceros' });
    expect(screen.queryByRole('link', { name: 'Importar' })).not.toBeInTheDocument();
  });

  it('shows the "not allowed" page to a FiringChief who opens the address', async () => {
    await renderApp(PATH, { session: SYNTHETIC_FIRING_CHIEF });

    expect(await screen.findByRole('heading', { name: 'Acceso no permitido' })).toBeInTheDocument();
  });
});

describe('Checking a file (spec: Import screen)', () => {
  it('offers only active comparsas', async () => {
    comparsas();
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });

    const comparsa = await screen.findByRole('combobox', { name: 'Comparsa' });
    expect(await within(comparsa).findByRole('option', { name: NORTE.name })).toBeInTheDocument();
    expect(within(comparsa).queryByRole('option', { name: OESTE.name })).not.toBeInTheDocument();
  });

  it('downloads the template in the language of the page', async () => {
    comparsas();
    let language: string | null = null;
    server.use(
      mock.get('/api/arquebusiers/import/template', ({ request }) => {
        language = request.headers.get('Accept-Language');
        return new HttpResponse(new Uint8Array([1, 2]), {
          headers: { 'Content-Disposition': 'attachment; filename=polvorapp-arquebusiers-template.xlsx' },
        });
      }),
    );
    // jsdom has no object URLs: a stand-in URL class, undone after each test.
    const createObjectURL = vi.fn(() => 'blob:plantilla');
    vi.stubGlobal(
      'URL',
      class extends URL {
        static override createObjectURL = createObjectURL;
        static override revokeObjectURL = vi.fn();
      },
    );
    let saved: string | undefined;
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      saved = this.download;
    });
    await renderApp(PATH, { session: SYNTHETIC_ADMIN, language: 'ca-ES-valencia' });

    await userEvent.click(await screen.findByRole('button', { name: 'Descarrega la plantilla' }));

    await waitFor(() => {
      expect(click).toHaveBeenCalled();
    });
    expect(language).toBe('ca-ES-valencia');
    expect(createObjectURL).toHaveBeenCalled();
    expect(saved).toBe('polvorapp-arquebusiers-template.xlsx');
  });

  it('says when the template cannot be downloaded', async () => {
    comparsas();
    server.use(mock.get('/api/arquebusiers/import/template', () => problem(503, 'registry.busy')));
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });

    const button = await screen.findByRole('button', { name: 'Descargar la plantilla' });
    await userEvent.click(button);

    expect(
      await screen.findByText('No se ha podido descargar la plantilla. Inténtalo de nuevo.'),
    ).toBeInTheDocument();
    expect(button).toHaveFocus();
  });

  it('asks for the comparsa and the file before checking anything', async () => {
    comparsas();
    const received = checkAnswers(CLEAN);
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await check();

    const summary = await screen.findByRole('group', { name: 'Hay un problema' });
    expect(within(summary).getAllByRole('link')).toHaveLength(2);
    expect(received).toHaveLength(0);
  });

  it('refuses a file that is not .xlsx without uploading it', async () => {
    comparsas();
    const received = checkAnswers(CLEAN);
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await userEvent.upload(fileInput(), workbook('arcabuceros.csv'), { applyAccept: false });

    expect(await screen.findByText('Solo se aceptan ficheros .xlsx.')).toBeInTheDocument();
    await check();
    expect(received).toHaveLength(0);
  });

  it('sends the comparsa and the file, and a clean report allows the import', async () => {
    const received = checkAnswers(CLEAN);
    await choose();

    await check();

    const outcome = await screen.findByText('El fichero se puede importar: 12 arcabuceros.');
    await waitFor(() => {
      expect(outcome.closest('[tabindex="-1"]')).toHaveFocus();
    });
    expect(received[0]?.comparsaId).toBe(NORTE.id);
    expect((received[0]?.file as File).name).toBe('arcabuceros-norte.xlsx');
    expect(screen.getByRole('button', { name: 'Importar 12 arcabuceros' })).toBeInTheDocument();
    expect(screen.getByText('Ninguna fila tiene errores ni avisos.')).toBeInTheDocument();
    expect(screen.getByText(/no tendrán fotos ni armas propias/)).toBeInTheDocument();
  });

  it('lists every problem in words with its column, and keeps the import closed', async () => {
    checkAnswers(WITH_PROBLEMS);
    await choose();

    await check();

    const table = await screen.findByRole('table', { name: 'Filas con errores o avisos' });
    expect(within(table).getByText('DNI/NIE: La letra no corresponde a los números.')).toBeInTheDocument();
    expect(
      within(table).getByText('Fecha de expedición: Escribe una fecha completa: día, mes y año.'),
    ).toBeInTheDocument();
    expect(within(table).getByText('ID Unión: Se repite en otra fila del fichero.')).toBeInTheDocument();
    expect(within(table).getByText('Aviso: Licencia caducada')).toBeInTheDocument();
    expect(within(table).getByText('(sin nombre)')).toBeInTheDocument();
    expect(screen.getByText(/2 filas tienen errores/)).toBeInTheDocument();
    expect(screen.getByText(/Observaciones/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Importar \d/ })).not.toBeInTheDocument();
  });

  it('narrows the table to the rows with errors', async () => {
    checkAnswers(WITH_PROBLEMS);
    await choose();
    await check();
    const table = await screen.findByRole('table', { name: 'Filas con errores o avisos' });

    await userEvent.click(screen.getByRole('checkbox', { name: 'Mostrar solo las filas con errores' }));

    expect(within(table).queryByText('Aviso: Licencia caducada')).not.toBeInTheDocument();
    expect(within(table).getByText('DNI/NIE: La letra no corresponde a los números.')).toBeInTheDocument();
  });

  it('allows the import when the rows only have warnings', async () => {
    checkAnswers(WARNINGS_ONLY);
    await choose();

    await check();

    expect(await screen.findByRole('button', { name: 'Importar 2 arcabuceros' })).toBeInTheDocument();
  });

  it('shows a file the server cannot read as one message, with the missing columns by header', async () => {
    answerImports({
      check: () =>
        problemAnswer(400, 'validation', {
          errors: { file: 'missingColumns' },
          columns: ['nationalId', 'license.type'],
        }),
    });
    await choose();

    await check();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^Hoja de cálculo/ })).toHaveAccessibleDescription(
        /Faltan columnas obligatorias: DNI\/NIE, Tipo de licencia\. Usa la plantilla\./,
      );
    });
  });

  it.each([
    ['required', 'No se ha recibido el fichero. Vuelve a elegirlo.'],
    ['tooLarge', 'El fichero ocupa más de 2 MB.'],
    ['invalid', 'No es una hoja de cálculo .xlsx que se pueda leer.'],
    ['empty', 'La primera hoja no tiene ningún arcabucero bajo la fila de cabeceras.'],
    ['tooManyRows', 'El fichero tiene más de 1000 arcabuceros. Divídelo en varios.'],
    ['somethingNew', 'No es una hoja de cálculo .xlsx que se pueda leer.'],
  ])('words the file reason %s at the file field', async (reason, text) => {
    answerImports({ check: () => problemAnswer(400, 'validation', { errors: { file: reason } }) });
    await choose();

    await check();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^Hoja de cálculo/ })).toHaveAccessibleDescription(
        new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')),
      );
    });
  });

  it('names repeated columns by their headers', async () => {
    answerImports({
      check: () =>
        problemAnswer(400, 'validation', {
          errors: { file: 'duplicateColumns' },
          columns: ['federationId', 'trainingCompletedOn'],
        }),
    });
    await choose();

    await check();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^Hoja de cálculo/ })).toHaveAccessibleDescription(
        /Hay columnas repetidas: ID Unión, Fecha del curso\./,
      );
    });
  });

  it('refuses a file over 2 MB without uploading it', async () => {
    comparsas();
    const received = checkAnswers(CLEAN);
    await renderApp(PATH, { session: SYNTHETIC_ADMIN });
    await screen.findByRole('combobox', { name: 'Comparsa' });

    await userEvent.upload(fileInput(), workbook('grande.xlsx', 2 * 1024 * 1024 + 1));

    expect(await screen.findByText('El fichero ocupa más de 2 MB.')).toBeInTheDocument();
    await check();
    expect(received).toHaveLength(0);
  });

  it('puts an inactive comparsa on the comparsa field', async () => {
    answerImports({ check: () => problemAnswer(409, 'arquebusiers.comparsaInactive') });
    await choose();

    await check();

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Comparsa' })).toHaveAccessibleDescription(
        /La comparsa está inactiva y no admite nuevos arcabuceros\./,
      );
    });
  });

  it('says when checks come too quickly', async () => {
    answerImports({ check: () => new Response(null, { status: 429 }) });
    await choose();

    await check();

    expect(await screen.findAllByText(/Espera un minuto/)).not.toHaveLength(0);
  });

  it('discards the report when the file changes', async () => {
    checkAnswers(CLEAN);
    await choose();
    await check();
    await screen.findByText('El fichero se puede importar: 12 arcabuceros.');

    await userEvent.upload(fileInput(), workbook('otro.xlsx'));

    expect(screen.queryByText('El fichero se puede importar: 12 arcabuceros.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Importar \d/ })).not.toBeInTheDocument();
  });

  it('discards the report when the comparsa changes', async () => {
    checkAnswers(CLEAN);
    await choose();
    await check();
    await screen.findByText('El fichero se puede importar: 12 arcabuceros.');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Comparsa' }), SUR.id);

    expect(screen.queryByText('El fichero se puede importar: 12 arcabuceros.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Importar \d/ })).not.toBeInTheDocument();
  });

  it('does not bring the report back when the comparsa changes and changes back', async () => {
    checkAnswers(CLEAN);
    await choose();
    await check();
    await screen.findByText('El fichero se puede importar: 12 arcabuceros.');
    const comparsa = screen.getByRole('combobox', { name: 'Comparsa' });

    await userEvent.selectOptions(comparsa, SUR.id);
    await userEvent.selectOptions(comparsa, NORTE.id);

    expect(screen.queryByText('El fichero se puede importar: 12 arcabuceros.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Comprobar el fichero' })).toBeInTheDocument();
  });

  it('shows every row again when a new check of the same file has no errors left', async () => {
    let answer = WITH_PROBLEMS;
    answerImports({ check: () => json(answer) });
    await choose();
    await check();
    await userEvent.click(
      await screen.findByRole('checkbox', { name: 'Mostrar solo las filas con errores' }),
    );
    expect(await screen.findByText('Se muestran 2 de 3 filas.')).toBeInTheDocument();

    answer = WARNINGS_ONLY;
    await check();

    const table = await screen.findByRole('table', { name: 'Filas con errores o avisos' });
    await waitFor(() => {
      expect(within(table).getByText('Aviso: Licencia caducada')).toBeInTheDocument();
    });
  });

  it('has no accessibility violations with a report', async () => {
    checkAnswers(WITH_PROBLEMS);
    const { container } = await choose();
    await check();
    await screen.findByRole('table', { name: 'Filas con errores o avisos' });

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Importing (spec: Import screen, All-or-nothing import)', () => {
  it('confirms with the comparsa and the count, then opens the list of that comparsa and says how many', async () => {
    const { imports } = answerImports({
      check: () => json(CLEAN),
      run: () => json({ comparsaId: NORTE.id, importedCount: 12 }),
    });
    server.use(mock.get('/api/arquebusiers', () => HttpResponse.json([])));
    const app = await choose();
    const invalidate = vi.spyOn(app.queryClient, 'invalidateQueries');
    await check();

    await userEvent.click(await screen.findByRole('button', { name: 'Importar 12 arcabuceros' }));
    const dialog = await screen.findByRole('alertdialog', {
      name: `¿Importar 12 arcabuceros en ${NORTE.name}?`,
    });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Importar 12 arcabuceros' }));

    expect(await screen.findByText(`12 arcabuceros importados en ${NORTE.name}.`)).toBeInTheDocument();
    expect(app.location()).toBe(`/arquebusiers?comparsaId=${NORTE.id}`);
    expect(imports[0]?.comparsaId).toBe(NORTE.id);
    // The list, and the insights (warning count, dashboard, statistics) that count the new arquebusiers.
    const invalidated = invalidate.mock.calls.map(([filters]) => JSON.stringify(filters?.queryKey));
    expect(invalidated).toContain(JSON.stringify(getListArquebusiersQueryKey()));
    expect(invalidated).toEqual(
      expect.arrayContaining(insightsQueryKeys().map((key) => JSON.stringify(key))),
    );
  });

  it('shows the new report when the registry changed since the check', async () => {
    answerImports({
      check: () => json(CLEAN),
      run: () =>
        problemAnswer(400, 'arquebusierImport.rowErrors', {
          report: { ...WITH_PROBLEMS, errorRowCount: 1, rows: [WITH_PROBLEMS.rows[0]] },
        }),
    });
    const app = await choose();
    await check();

    await userEvent.click(await screen.findByRole('button', { name: 'Importar 12 arcabuceros' }));
    const dialog = await screen.findByRole('alertdialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Importar 12 arcabuceros' }));

    const outcome = await screen.findByText(
      'El registro ha cambiado desde la comprobación: 1 fila tiene ahora errores.',
    );
    await waitFor(() => {
      expect(outcome.closest('[tabindex="-1"]')).toHaveFocus();
    });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(app.location()).toBe(PATH);
  });

  it('keeps the dialog open with the reason when someone registered a person meanwhile', async () => {
    answerImports({ check: () => json(CLEAN), run: () => problemAnswer(409, 'arquebusierImport.conflict') });
    await choose();
    await check();

    await userEvent.click(await screen.findByRole('button', { name: 'Importar 12 arcabuceros' }));
    const dialog = await screen.findByRole('alertdialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Importar 12 arcabuceros' }));

    expect(await within(dialog).findByText(/Comprueba el fichero de nuevo/)).toBeInTheDocument();
  });
});
