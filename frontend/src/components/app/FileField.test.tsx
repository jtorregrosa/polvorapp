import { zodResolver } from '@hookform/resolvers/zod';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { FileField } from './FileField';
import { fileProblem, formatFileSize } from './file-rules';
import { Form, FormField } from './FormField';
import { useAppForm } from './use-app-form';

const RULES = { accept: '.xlsx', maxBytes: 1024 };

// Each page words the problems for its own files; FormField shows a message that is not a key as it is.
const MESSAGES = { wrongType: 'Solo se aceptan ficheros .xlsx', tooLarge: 'El fichero ocupa más de 1 KB' };

const schema = z.object({
  file: z
    .instanceof(File, { message: 'validation.required' })
    .nullable()
    .superRefine((file, context) => {
      if (!file) {
        context.addIssue({ code: 'custom', message: 'validation.required' });
        return;
      }
      const problem = fileProblem(file, RULES);
      if (problem) context.addIssue({ code: 'custom', message: MESSAGES[problem] });
    }),
});
type Values = z.input<typeof schema>;

function UploadForm({ onSubmit }: { onSubmit: (values: Values) => void }) {
  const form = useAppForm<Values>({ resolver: zodResolver(schema), defaultValues: { file: null } });
  return (
    <Form form={form} onSubmit={onSubmit} requiredNote={false}>
      <FormField control={form.control} name="file" label="Hoja de cálculo" description="Un fichero .xlsx">
        {(field) => (
          <FileField
            {...field}
            accept={RULES.accept}
            onChange={(file) => {
              field.onChange(file);
              void form.trigger('file');
            }}
          />
        )}
      </FormField>
      <Button type="submit">Comprobar</Button>
    </Form>
  );
}

/** The hidden input the button opens; tests upload through it, as the system chooser would. */
function fileInput(): HTMLInputElement {
  const input = document.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input) throw new Error('The file input is missing.');
  return input;
}

const workbook = (name = 'arcabuceros.xlsx', size = 100) =>
  new File([new Uint8Array(size)], name, {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });

describe('FileField', () => {
  it('is named by its label and what it does, and opens the file chooser from the keyboard', async () => {
    await renderWithProviders(<UploadForm onSubmit={vi.fn()} />);
    const input = fileInput();
    const click = vi.spyOn(input, 'click');

    const choose = screen.getByRole('button', { name: 'Hoja de cálculo Elegir fichero' });
    await userEvent.tab();
    expect(choose).toHaveFocus();
    await userEvent.keyboard('{Enter}');

    expect(click).toHaveBeenCalled();
    expect(input.accept).toBe('.xlsx');
  });

  it('shows, describes and announces the chosen file, and submits it', async () => {
    const onSubmit = vi.fn<(values: Values) => void>();
    await renderWithProviders(<UploadForm onSubmit={onSubmit} />);

    await userEvent.upload(fileInput(), workbook());

    expect(screen.getByText('arcabuceros.xlsx')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent(/Fichero elegido: arcabuceros\.xlsx, 100\sB/);
    expect(
      screen.getByRole('button', { name: 'Hoja de cálculo Cambiar fichero' }),
    ).toHaveAccessibleDescription(/arcabuceros\.xlsx 100\sB/);
    await userEvent.click(screen.getByRole('button', { name: 'Comprobar' }));
    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalled();
    });
    expect(onSubmit.mock.lastCall?.[0].file?.name).toBe('arcabuceros.xlsx');
  });

  it('clears the chosen file, announces it and gives focus back to the button', async () => {
    await renderWithProviders(<UploadForm onSubmit={vi.fn()} />);
    await userEvent.upload(fileInput(), workbook());

    await userEvent.click(screen.getByRole('button', { name: 'Quitar el fichero arcabuceros.xlsx' }));

    expect(screen.queryByText('arcabuceros.xlsx')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Fichero quitado: arcabuceros.xlsx');
    expect(screen.getByRole('button', { name: 'Hoja de cálculo Elegir fichero' })).toHaveFocus();
  });

  it('notices the same file chosen again after clearing it', async () => {
    await renderWithProviders(<UploadForm onSubmit={vi.fn()} />);
    const file = workbook();
    await userEvent.upload(fileInput(), file);
    await userEvent.click(screen.getByRole('button', { name: /Quitar el fichero/ }));

    await userEvent.upload(fileInput(), file);

    expect(screen.getByText('arcabuceros.xlsx')).toBeInTheDocument();
  });

  it('says at once that a file of another type is not accepted, without submitting', async () => {
    const onSubmit = vi.fn();
    await renderWithProviders(<UploadForm onSubmit={onSubmit} />);

    await userEvent.upload(fileInput(), workbook('arcabuceros.csv'), { applyAccept: false });

    expect(await screen.findByText(/Solo se aceptan ficheros \.xlsx/)).toBeInTheDocument();
    const choose = screen.getByRole('button', { name: /Hoja de cálculo/ });
    expect(choose).toHaveAccessibleDescription(/Solo se aceptan ficheros \.xlsx/);
    await userEvent.click(screen.getByRole('button', { name: 'Comprobar' }));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('says at once that a file over the limit is too large', async () => {
    await renderWithProviders(<UploadForm onSubmit={vi.fn()} />);

    await userEvent.upload(fileInput(), workbook('grande.xlsx', RULES.maxBytes + 1));

    expect(await screen.findByText(/El fichero ocupa más de 1 KB/)).toBeInTheDocument();
  });

  it('focuses the button when a submission without a file fails', async () => {
    await renderWithProviders(<UploadForm onSubmit={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Comprobar' }));
    await userEvent.click(await screen.findByRole('link', { name: /Hoja de cálculo/ }));

    expect(screen.getByRole('button', { name: 'Hoja de cálculo Elegir fichero' })).toHaveFocus();
  });

  it('can be disabled', async () => {
    await renderWithProviders(
      <FileField value={workbook()} onChange={vi.fn()} onBlur={vi.fn()} accept=".xlsx" disabled />,
    );

    expect(screen.getByRole('button', { name: 'Cambiar fichero' })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Quitar el fichero/ })).toBeDisabled();
  });

  it('shows sizes in the language of the page', async () => {
    await renderWithProviders(
      <FileField
        value={workbook('arcabuceros.xlsx', 48_640)}
        onChange={vi.fn()}
        onBlur={vi.fn()}
        accept=".xlsx"
      />,
      'en',
    );

    expect(screen.getByText('47.5 kB')).toBeInTheDocument();
  });

  it('has no accessibility violations, with a file and with an error', async () => {
    const { container } = await renderWithProviders(<UploadForm onSubmit={vi.fn()} />);
    await userEvent.upload(fileInput(), workbook('arcabuceros.csv'), { applyAccept: false });
    await screen.findByText(/Solo se aceptan/);

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('fileProblem', () => {
  it('accepts an allowed extension in any case, up to the limit itself', () => {
    expect(fileProblem(workbook('ARCABUCEROS.XLSX', 1024), RULES)).toBeUndefined();
    expect(fileProblem(workbook('a.xls'), { accept: '.xlsx, .xls', maxBytes: 1024 })).toBeUndefined();
  });

  it('names the wrong type before the size', () => {
    expect(fileProblem(workbook('a.xls', 2048), RULES)).toBe('wrongType');
    expect(fileProblem(workbook('a.xlsx', 1025), RULES)).toBe('tooLarge');
  });

  it('checks the last extension of the name', () => {
    expect(fileProblem(workbook('a.xlsx.exe'), RULES)).toBe('wrongType');
    expect(fileProblem(workbook('xlsx'), RULES)).toBe('wrongType');
  });

  it('refuses rules that are not dotted extensions', () => {
    expect(() => fileProblem(workbook(), { accept: '', maxBytes: 1 })).toThrow();
    expect(() => fileProblem(workbook(), { accept: '.xlsx,', maxBytes: 1 })).not.toThrow();
    expect(() => fileProblem(workbook(), { accept: 'xlsx', maxBytes: 1 })).toThrow();
    expect(() => fileProblem(workbook(), { accept: 'image/*', maxBytes: 1 })).toThrow();
  });
});

describe('formatFileSize', () => {
  const format = (value: number, options: Intl.NumberFormatOptions) =>
    new Intl.NumberFormat('en-GB', options).format(value);

  it('uses the largest binary unit with one decimal at most', () => {
    expect(formatFileSize(0, format)).toBe('0 byte');
    expect(formatFileSize(1023, format)).toBe('1,023 byte');
    expect(formatFileSize(1024, format)).toBe('1 kB');
    expect(formatFileSize(1536 * 1024, format)).toBe('1.5 MB');
  });

  it('moves to the next unit when rounding reaches it', () => {
    expect(formatFileSize(1024 * 1024 - 1, format)).toBe('1 MB');
  });
});
